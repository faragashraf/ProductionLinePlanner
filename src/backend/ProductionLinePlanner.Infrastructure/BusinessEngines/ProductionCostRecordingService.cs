using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class ProductionCostRecordingService(
    AppDbContext db,
    IAuditEngine audit,
    IAssignmentEngine assignmentEngine,
    IAttendanceEngine attendanceEngine,
    IPermissionService permissionService) : IProductionCostRecordingService
{
    private const int QuantityScale = 3;
    private const int MoneyScale = 4;
    private const string ManualParticipantOverridePermission = "assignments.manage";

    public async Task<ProductionOrderDto> CreateOrderAsync(CreateProductionOrderRequest request, Guid actorId, CancellationToken ct)
    {
        if (!await db.Set<ProductModel>().AnyAsync(x => x.Id == request.ProductModelId && x.IsActive, ct)) throw new InvalidOperationException("An active product model is required.");
        if (!request.ProductionLineId.HasValue || !await db.Set<ProductionLine>().AnyAsync(x => x.Id == request.ProductionLineId && x.IsActive, ct)) throw new ProductionConflictException("يجب اختيار خط إنتاج نشط وصالح لأمر الإنتاج.");
        if (await db.Set<ProductionOrder>().AnyAsync(x => x.OrderNumber == request.OrderNumber.Trim(), ct)) throw new ProductionConflictException("Order number already exists.");
        var order = new ProductionOrder(Guid.NewGuid(), request.OrderNumber, request.ProductModelId, request.ProductionLineId, request.ProductionDate, request.PlannedQuantity, request.Notes, actorId, DateTime.UtcNow);
        db.Add(order); await AuditAsync(actorId, AuditActionType.Create, "ProductionOrder", order.Id, null, OrderAudit(order), ct); await db.SaveChangesAsync(ct); return await OrderDtoAsync(order.Id, ct);
    }

    public async Task<IReadOnlyCollection<ProductionOrderDto>> ListOrdersAsync(ProductionOrderStatus? status, CancellationToken ct) =>
        await db.Set<ProductionOrder>().AsNoTracking().Include(x => x.ProductModel).Where(x => !status.HasValue || x.Status == status).OrderByDescending(x => x.ProductionDate).Select(x => new ProductionOrderDto(x.Id, x.OrderNumber, x.ProductModelId, x.ProductModel!.Code, x.ProductionLineId, x.ProductionDate, x.PlannedQuantity, x.Status.ToString(), x.Notes, x.SourceImportBatchId != null, x.RecordedAtUtc, x.ApprovedAtUtc)).ToListAsync(ct);

    public async Task<ProductionOrderDto> UpdateOrderAsync(Guid id, UpdateProductionOrderRequest request, Guid actorId, CancellationToken ct)
    { var order = await OrderAsync(id, ct); if (order.SourceImportBatchId.HasValue) throw new ProductionConflictException("Imported production-day dates and line quantities are controlled by the intake workflow."); var before = OrderAudit(order); order.UpdateDraft(request.ProductionDate, request.PlannedQuantity, request.Notes, actorId, DateTime.UtcNow); await AuditAsync(actorId, AuditActionType.Update, "ProductionOrder", id, before, OrderAudit(order), ct); await db.SaveChangesAsync(ct); return await OrderDtoAsync(id, ct); }

    public async Task<ProductionOrderDto> TransitionOrderAsync(Guid id, ProductionOrderStatus status, Guid actorId, CancellationToken ct)
    {
        var order = await OrderAsync(id, ct, includeRecords: true); var before = OrderAudit(order); var now = DateTime.UtcNow;
        if (order.SourceImportBatchId.HasValue) throw new ProductionConflictException("Imported production days must be reviewed and approved through the daily intake workflow.");
        if (status == ProductionOrderStatus.Active) order.Activate(actorId, now);
        else if (status == ProductionOrderStatus.Completed)
        {
            if (order.StageProductionRecords.Any(x => x.Status == StageProductionRecordStatus.Draft)) throw new ProductionConflictException("Production orders with draft records cannot be completed.");
            order.Complete(actorId, now);
        }
        else if (status == ProductionOrderStatus.Cancelled)
        {
            if (order.StageProductionRecords.Any(x => x.Status == StageProductionRecordStatus.Approved)) throw new ProductionConflictException("Approved production records must be cancelled before cancelling the order.");
            order.Cancel(actorId, now);
        }
        else throw new InvalidOperationException("Unsupported order transition.");
        await AuditAsync(actorId, status == ProductionOrderStatus.Cancelled ? AuditActionType.Cancel : AuditActionType.Update, "ProductionOrder", id, before, OrderAudit(order), ct); await db.SaveChangesAsync(ct); return await OrderDtoAsync(id, ct);
    }

    public async Task<StageProductionRecordDto> CreateDraftAsync(CreateStageProductionRecordRequest request, Guid actorId, CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty) throw new ProductionConflictException("ClientRequestId is required for production recording.");
        var existing = await db.Set<StageProductionRecord>().AsNoTracking().AnyAsync(x => x.ProductionOrderId == request.ProductionOrderId && x.ClientRequestId == request.ClientRequestId, ct);
        if (existing) return await GetRecordByClientRequestAsync(request.ProductionOrderId, request.ClientRequestId, ct);
        var (order, stage) = await LoadOrderAndStageAsync(request.ProductionOrderId, request.ProductModelStageId, ct);
        EnsureRecordableOrder(order); var now = DateTime.UtcNow;
        var record = CreateSnapshotRecord(order, stage, request.ProductionDate, request.ProducedQuantity, request.AcceptedQuantity, request.RejectedQuantity, request.ClientRequestId, request.Notes, actorId, now);
        record.ReplaceAllocations(await BuildAllocationsAsync(stage, record.AcceptedQuantity, request.Workers, actorId, ProductionDateEvidenceAtUtc(request.ProductionDate), ct)); db.Add(record);
        await AuditAsync(actorId, AuditActionType.Create, "StageProductionRecord", record.Id, null, RecordAudit(record), ct); await AuditAsync(actorId, AuditActionType.Update, "StageProductionWorkerAllocation", record.Id, null, AllocationAudit(record), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var concurrentRecord = await db.Set<StageProductionRecord>()
                .AsNoTracking()
                .AnyAsync(x => x.ProductionOrderId == request.ProductionOrderId && x.ClientRequestId == request.ClientRequestId, ct);
            if (concurrentRecord)
            {
                return await GetRecordByClientRequestAsync(request.ProductionOrderId, request.ClientRequestId, ct);
            }

            throw new ProductionConflictException("تعذر حفظ دفعة الإنتاج بسبب تعارض متزامن. حدّث البيانات وحاول مرة أخرى.");
        }

        return await GetRecordAsync(record.Id, ct);
    }

    public async Task<StageProductionRecordDto> CalculatePreviewAsync(CreateStageProductionRecordRequest request, Guid actorId, CancellationToken ct)
    {
        var (order, stage) = await LoadOrderAndStageAsync(request.ProductionOrderId, request.ProductModelStageId, ct); EnsureRecordableOrder(order);
        var record = CreateSnapshotRecord(order, stage, request.ProductionDate, request.ProducedQuantity, request.AcceptedQuantity, request.RejectedQuantity, request.ClientRequestId == Guid.Empty ? Guid.NewGuid() : request.ClientRequestId, request.Notes, actorId, DateTime.UtcNow);
        record.ReplaceAllocations(await BuildAllocationsAsync(stage, record.AcceptedQuantity, request.Workers, actorId, ProductionDateEvidenceAtUtc(request.ProductionDate), ct));
        record.SetCalculationPreview(record.WorkerAllocations.Sum(x => x.CalculatedEarning));
        return ToRecordDto(record);
    }

    public async Task<StageProductionRecordDto> UpdateDraftAsync(Guid id, UpdateStageProductionRecordRequest request, Guid actorId, CancellationToken ct)
    {
        var record = await RecordAsync(id, ct); EnsureCurrentVersion(record, request.ConcurrencyToken); db.Entry(record).Property(x => x.ConcurrencyToken).OriginalValue = request.ConcurrencyToken; EnsureRecordableOrder(record.ProductionOrder!); var before = RecordAudit(record); var allocationsBefore = AllocationAudit(record);
        var (_, stage) = await LoadOrderAndStageAsync(record.ProductionOrderId, record.ProductModelStageId, ct);
        record.UpdateDraft(request.ProductionDate, RoundQuantity(request.ProducedQuantity), RoundQuantity(request.AcceptedQuantity), RoundQuantity(request.RejectedQuantity), request.Notes); var allocations = await BuildAllocationsAsync(stage, record.AcceptedQuantity, request.Workers, actorId, ProductionDateEvidenceAtUtc(request.ProductionDate), ct); var removedAllocations = record.ReplaceAllocations(allocations); db.RemoveRange(removedAllocations);
        await AuditAsync(actorId, AuditActionType.Update, "StageProductionRecord", id, before, RecordAudit(record), ct); await AuditAsync(actorId, AuditActionType.Update, "StageProductionWorkerAllocation", id, allocationsBefore, AllocationAudit(record), ct); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new ProductionConflictException("The production record changed while it was being saved. Refresh and try again."); } return await GetRecordAsync(id, ct);
    }

    public async Task<StageProductionRecordDto> GetRecordAsync(Guid id, CancellationToken ct) => ToRecordDto(await RecordAsync(id, ct));

    public async Task<IReadOnlyCollection<StageProductionRecordDto>> ListRecordsAsync(DateOnly? from, DateOnly? to, StageProductionRecordStatus? status, CancellationToken ct)
    { var q = db.Set<StageProductionRecord>().AsNoTracking().Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).Where(x => (!from.HasValue || x.ProductionDate >= from) && (!to.HasValue || x.ProductionDate <= to) && (!status.HasValue || x.Status == status)); return (await q.OrderByDescending(x => x.ProductionDate).ToListAsync(ct)).Select(ToRecordDto).ToList(); }

    public async Task<StageProductionRecordDto> ApproveAsync(Guid id, Guid concurrencyToken, Guid actorId, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var record = await RecordAsync(id, ct);
        if (record.ProductionOrder!.SourceImportBatchId.HasValue) throw new ProductionConflictException("Imported production stages can only be approved with their complete production day.");
        EnsureCurrentVersion(record, concurrencyToken);
        if (record.Status == StageProductionRecordStatus.Approved) return ToRecordDto(record);
        if (record.Status == StageProductionRecordStatus.Cancelled) throw new ProductionConflictException("لا يمكن اعتماد سجل تم إلغاء اعتماد الإنتاج له. أنشئ مسودة تصحيح مستقلة عند الحاجة.");
        db.Entry(record).Property(x => x.ConcurrencyToken).OriginalValue = concurrencyToken; EnsureRecordableOrder(record.ProductionOrder!);
        var currentAccepted = await db.Set<StageProductionRecord>().Where(x => x.ProductionOrderId == record.ProductionOrderId && x.Id != record.Id && x.Status == StageProductionRecordStatus.Approved).SumAsync(x => (decimal?)x.AcceptedQuantity, ct) ?? 0m;
        if (currentAccepted + record.AcceptedQuantity > record.ProductionOrder!.PlannedQuantity) throw new ProductionConflictException("Approved accepted quantity exceeds the production order planned quantity.");
        EnsurePersistedFinancialConsistency(record);
        var before = FinancialAudit(record);
        var (_, currentStage) = await LoadOrderAndStageAsync(record.ProductionOrderId, record.ProductModelStageId, ct);
        // Validate current attendance and assignment eligibility without recalculating or
        // rewriting the draft's stored financial snapshot.
        await BuildAllocationsAsync(currentStage, record.AcceptedQuantity, record.WorkerAllocations.Select(x => new WorkerAllocationRequest(x.WorkerId, x.Percentage, x.FixedAmount, x.Notes, x.ManualOverrideReason, x.InputQuantity)).ToList(), actorId, ProductionDateEvidenceAtUtc(record.ProductionDate), ct);
        var now = DateTime.UtcNow;
        record.Approve(actorId, now);
        await AuditAsync(actorId, AuditActionType.Update, "StageProductionRecord", id, before, FinancialAudit(record), ct);
        await AuditAsync(actorId, AuditActionType.Update, "StageProductionWorkerAllocation", id, null, AllocationAudit(record), ct);
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ProductionConflictException("تم تغيير سجل الإنتاج أثناء الاعتماد. حدّث البيانات وحاول مرة أخرى.");
        }
        return ToRecordDto(record);
    }

    public async Task<StageProductionRecordDto> CancelProductionApprovalAsync(Guid id, Guid concurrencyToken, string reason, Guid actorId, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var record = await RecordAsync(id, ct);
        EnsureCurrentVersion(record, concurrencyToken);
        EnsureProductionApprovalCanBeCancelled(record, reason);
        db.Entry(record).Property(x => x.ConcurrencyToken).OriginalValue = concurrencyToken;
        var before = FinancialAudit(record);
        record.CancelProductionApproval(reason, actorId, DateTime.UtcNow);
        await AuditAsync(actorId, AuditActionType.Cancel, "StageProductionRecord", id, before, FinancialAudit(record), ct);
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ProductionConflictException("تم تغيير حالة اعتماد الإنتاج بواسطة مستخدم آخر. حدّث البيانات وحاول مرة أخرى.");
        }
        return ToRecordDto(record);
    }

    public async Task<IReadOnlyCollection<DailyProductionCostReportRowDto>> DailyReportAsync(DateOnly from, DateOnly to, Guid? orderId, Guid? modelId, Guid? workerId, CancellationToken ct)
    { var q = db.Set<StageProductionRecord>().AsNoTracking().Include(x => x.ProductionOrder).Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).Where(x => x.Status == StageProductionRecordStatus.Approved && x.ProductionDate >= from && x.ProductionDate <= to && (!orderId.HasValue || x.ProductionOrderId == orderId) && (!modelId.HasValue || x.ProductionOrder!.ProductModelId == modelId) && (!workerId.HasValue || x.WorkerAllocations.Any(a => a.WorkerId == workerId))); return (await q.ToListAsync(ct)).Select(x => new DailyProductionCostReportRowDto(x.Id, x.ProductionDate, x.ProductionOrder!.OrderNumber, x.SnapshotProductModelCode, x.SnapshotStageCode, x.SnapshotStageName, x.ProducedQuantity, x.AcceptedQuantity, x.RejectedQuantity, x.TotalWorkerEarnings, x.SnapshotCompensationMode.ToString(), x.Status.ToString(), x.WorkerAllocations.Select(ToAllocationDto).ToList())).ToList(); }

    public async Task<DailyProductionOperationsDto> LoadDailyOperationsAsync(
        Guid factoryId,
        Guid productionLineId,
        Guid productModelId,
        DateOnly productionDate,
        CancellationToken ct)
    {
        var context = await LoadDailyContextAsync(factoryId, productionLineId, productModelId, productionDate, ct);
        return context.ToDto();
    }

    public async Task<DailyProductionPreviewDto> PreviewDailyOperationsAsync(
        DailyProductionOperationRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        var preview = await BuildDailyPreviewAsync(request, actorId, ct);
        return preview.ToDto();
    }

    public async Task<DailyProductionDraftDto> SaveDailyDraftAsync(
        DailyProductionOperationRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty)
            throw new ProductionConflictException("معرّف طلب الحفظ مطلوب لمنع تكرار مسودة تشغيل اليوم.");

        var sourceReference = DailySourceReference(request.ClientRequestId);
        var idempotentExisting = await FindDailyOrderBySourceReferenceAsync(sourceReference, ct);
        if (idempotentExisting is not null)
            return ToDailyDraftDto(idempotentExisting, wasAlreadySaved: true);

        var preview = await BuildDailyPreviewAsync(request, actorId, ct);
        if (string.IsNullOrWhiteSpace(request.PreviewToken) ||
            !string.Equals(request.PreviewToken, preview.PreviewToken, StringComparison.Ordinal))
        {
            throw new ProductionConflictException("تم تغيير بيانات تشغيل اليوم أو لم تعد المعاينة الحالية صالحة. أعد حساب المعاينة قبل الحفظ.");
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        try
        {
            var existingDay = await db.Set<ProductionOrder>()
                .Include(order => order.ProductModel)
                .Include(order => order.StageProductionRecords)
                    .ThenInclude(record => record.WorkerAllocations)
                        .ThenInclude(allocation => allocation.Worker)
                .SingleOrDefaultAsync(order => order.ProductionDate == request.ProductionDate
                    && order.ProductionLineId == request.ProductionLineId
                    && order.ProductModelId == request.ProductModelId, ct);

            if (existingDay is not null)
            {
                if (string.Equals(existingDay.SourceReference, sourceReference, StringComparison.Ordinal))
                    return ToDailyDraftDto(existingDay, wasAlreadySaved: true);

                throw new ProductionConflictException("توجد بالفعل مسودة أو عملية تشغيل لهذا الخط والموديل في تاريخ الإنتاج المحدد.");
            }

            var now = DateTime.UtcNow;
            var order = new ProductionOrder(
                Guid.NewGuid(),
                DailyOrderNumber(request.ProductionDate, request.ProductionLineId, request.ProductModelId),
                request.ProductModelId,
                request.ProductionLineId,
                request.ProductionDate,
                RoundQuantity(request.LineQuantity),
                request.Notes,
                actorId,
                now);
            order.MarkDailyOperation(sourceReference, now);
            db.Add(order);
            await AuditAsync(actorId, AuditActionType.Create, "ProductionOrder", order.Id, null, OrderAudit(order), ct);

            foreach (var stagePreview in preview.Stages)
            {
                var record = CreateDailySnapshotRecord(order, preview.Context, stagePreview, request, actorId, now);
                record.ProductionOrder = order;
                record.ReplaceAllocations(stagePreview.Allocations);
                db.Add(record);
                await AuditAsync(actorId, AuditActionType.Create, "StageProductionRecord", record.Id, null, RecordAudit(record), ct);
                await AuditAsync(actorId, AuditActionType.Update, "StageProductionWorkerAllocation", record.Id, null, AllocationAudit(record), ct);
            }

            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);

            return ToDailyDraftDto(order, wasAlreadySaved: false);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var concurrent = await FindDailyOrderBySourceReferenceAsync(sourceReference, ct);
            if (concurrent is not null)
                return ToDailyDraftDto(concurrent, wasAlreadySaved: true);

            throw new ProductionConflictException("تعذر حفظ مسودة تشغيل اليوم بسبب تعارض متزامن. حدّث البيانات وحاول مرة أخرى.");
        }
    }

    private async Task<DailyPreview> BuildDailyPreviewAsync(
        DailyProductionOperationRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        if (request.LineQuantity <= 0)
            throw new ProductionConflictException("كمية تشغيل الخط يجب أن تكون أكبر من صفر.");

        var context = await LoadDailyContextAsync(
            request.FactoryId,
            request.ProductionLineId,
            request.ProductModelId,
            request.ProductionDate,
            ct);
        ValidateDailyStages(request, context);

        var requestByStage = request.Stages.ToDictionary(stage => stage.ProductModelStageId);
        var stagePreviews = new List<DailyStagePreview>(context.Stages.Count);
        foreach (var stage in context.Stages)
        {
            var stageRequest = requestByStage[stage.Entity.Id];
            var allocations = await BuildAllocationsAsync(
                stage.Entity,
                RoundQuantity(request.LineQuantity),
                stageRequest.Workers,
                actorId,
                ProductionDateEvidenceAtUtc(request.ProductionDate),
                ct);
            var warnings = StageWarnings(stage.Dto);
            stagePreviews.Add(new DailyStagePreview(
                stage,
                allocations,
                RoundQuantity(request.LineQuantity),
                RoundMoney(allocations.Sum(allocation => allocation.CalculatedEarning)),
                warnings));
        }

        return new DailyPreview(
            context,
            RoundQuantity(request.LineQuantity),
            PreviewToken(context.ContextVersion, request),
            stagePreviews);
    }

    private async Task<DailyContext> LoadDailyContextAsync(
        Guid factoryId,
        Guid productionLineId,
        Guid productModelId,
        DateOnly productionDate,
        CancellationToken ct)
    {
        if (factoryId == Guid.Empty || productionLineId == Guid.Empty || productModelId == Guid.Empty)
            throw new ProductionConflictException("اختر المصنع وخط الإنتاج والموديل قبل تحميل تشغيل اليوم.");

        var factory = await db.Set<Factory>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == factoryId && candidate.IsActive, ct)
            ?? throw new ProductionConflictException("المصنع المحدد غير نشط أو غير متاح.");
        var line = await db.Set<ProductionLine>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == productionLineId && candidate.FactoryId == factoryId && candidate.IsActive, ct)
            ?? throw new ProductionConflictException("خط الإنتاج المحدد غير نشط أو لا يتبع المصنع.");
        var product = await db.Set<ProductModel>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == productModelId && candidate.IsActive, ct)
            ?? throw new ProductionConflictException("الموديل المحدد غير نشط أو غير متاح.");

        var stages = await db.Set<ProductModelStage>()
            .AsNoTracking()
            .Include(stage => stage.SubStage)
                .ThenInclude(subStage => subStage!.MainStage)
            .Where(stage => stage.ProductModelId == productModelId
                            && stage.IsActive
                            && stage.SubStage != null
                            && stage.SubStage.IsActive
                            && stage.SubStage.MainStage != null
                            && stage.SubStage.MainStage.IsActive
                            && stage.SubStage.MainStage.ProductionLineId == productionLineId)
            .OrderBy(stage => stage.StageOrder)
            .ToArrayAsync(ct);
        if (stages.Length == 0)
            throw new ProductionConflictException("لا توجد مراحل موديل نشطة مرتبطة بخط الإنتاج المحدد.");

        var workers = await db.Set<Worker>().AsNoTracking()
            .Where(worker => worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active)
            .OrderBy(worker => worker.EmployeeCode)
            .Select(worker => new ActiveWorker(worker.Id, worker.EmployeeCode, worker.FullName))
            .ToArrayAsync(ct);
        var workerIds = workers.Select(worker => worker.Id).ToArray();
        var evidenceAtUtc = ProductionDateEvidenceAtUtc(productionDate);
        var assignments = await assignmentEngine.ResolveCurrentAssignmentsAsync(workerIds, evidenceAtUtc, ct);
        if (assignments.IsFailure)
            throw new ProductionConflictException("تعذر احتساب التعيين الفعلي للعمال في تاريخ الإنتاج.");
        var attendance = await attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(workerIds, evidenceAtUtc, ct);
        if (attendance.IsFailure)
            throw new ProductionConflictException("تعذر قراءة حضور العمال لتاريخ الإنتاج المحدد. نفّذ مزامنة الحضور ثم أعد المحاولة.");

        var workerContexts = workers.Select(worker =>
        {
            var assignment = assignments.Value!.GetValueOrDefault(worker.Id);
            var hasAttendance = attendance.Value!.TryGetValue(worker.Id, out var attendanceRecord);
            var attendanceStatus = hasAttendance
                ? AttendanceLabel(attendanceRecord!.Status)
                : "NoSourceCheckIn";
            var isPresent = hasAttendance && attendanceRecord!.Status is AttendanceStatus.Present or AttendanceStatus.Late;
            return new DailyWorkerContext(
                worker.Id,
                worker.Code,
                worker.Name,
                assignment?.EffectiveSubStageId,
                assignment?.AssignmentType?.ToString(),
                attendanceStatus,
                hasAttendance,
                isPresent,
                assignment?.AssignmentId,
                attendanceRecord?.AttendanceTimeUtc);
        }).ToArray();

        var stageContexts = stages.Select(stage =>
        {
            var stageWorkers = workerContexts
                .Where(worker => worker.EffectiveSubStageId == stage.SubStageId)
                .OrderBy(worker => worker.WorkerCode, StringComparer.Ordinal)
                .ThenBy(worker => worker.WorkerId)
                .ToArray();
            var suggestedPercentages = SuggestedSharedPercentages(stage.CompensationMode, stageWorkers);
            var workerDtos = stageWorkers.Select(worker => worker.ToDto(suggestedPercentages.GetValueOrDefault(worker.WorkerId))).ToArray();
            var hasAbsent = workerDtos.Any(worker => worker.AttendanceStatus == "Absent");
            var hasNoCheckIn = workerDtos.Any(worker => worker.AttendanceStatus == "NoSourceCheckIn");
            var staffed = workerDtos.Length > 0;
            var ready = staffed && workerDtos.All(worker => worker.IsPresent);
            var staffingStatus = staffed ? "Staffed" : "NoStaffing";
            var attendanceStatus = !staffed
                ? "NoStaffing"
                : hasAbsent ? "AbsentWorker"
                    : hasNoCheckIn ? "NoSourceCheckIn"
                        : ready ? "Ready" : "AttendanceUnavailable";
            var subStage = stage.SubStage!;
            var mainStage = subStage.MainStage!;
            return new DailyStageContext(
                stage,
                new DailyProductionStageDto(
                    stage.Id,
                    stage.SubStageId,
                    mainStage.Name,
                    subStage.Code,
                    subStage.Name,
                    stage.StageOrder,
                    RoundMoney(stage.PiecePrice),
                    stage.CompensationMode.ToString(),
                    staffingStatus,
                    attendanceStatus,
                    hasAbsent,
                    hasNoCheckIn,
                    stage.CompensationMode == CompensationMode.SharedPercentage,
                    ready,
                    workerDtos));
        }).ToArray();

        var allWorkers = workerContexts
            .OrderBy(worker => worker.WorkerCode, StringComparer.Ordinal)
            .ThenBy(worker => worker.WorkerId)
            .Select(worker => worker.ToDto(null))
            .ToArray();
        var version = StaffingContextVersion(factory, line, product, productionDate, stageContexts, workerContexts);
        return new DailyContext(factory, line, product, productionDate, version, stageContexts, allWorkers);
    }

    private static void ValidateDailyStages(DailyProductionOperationRequest request, DailyContext context)
    {
        if (request.Stages is null || request.Stages.Count != context.Stages.Count)
            throw new ProductionConflictException("يجب أن تتضمن معاينة تشغيل اليوم كل مراحل الموديل المحمّلة.");
        if (request.Stages.Any(stage => stage.ProductModelStageId == Guid.Empty) ||
            request.Stages.Select(stage => stage.ProductModelStageId).Distinct().Count() != request.Stages.Count)
            throw new ProductionConflictException("توجد مرحلة مكررة أو غير صالحة في تشغيل اليوم.");

        var expected = context.Stages.Select(stage => stage.Entity.Id).ToHashSet();
        if (request.Stages.Any(stage => !expected.Contains(stage.ProductModelStageId)))
            throw new ProductionConflictException("تغيرت مراحل الموديل أو لا تتطابق مع خط الإنتاج. أعد تحميل تشغيل اليوم.");
    }

    private static StageProductionRecord CreateDailySnapshotRecord(
        ProductionOrder order,
        DailyContext context,
        DailyStagePreview preview,
        DailyProductionOperationRequest request,
        Guid actorId,
        DateTime recordedAtUtc)
    {
        var stage = preview.Stage.Entity;
        var subStage = stage.SubStage!;
        var mainStage = subStage.MainStage!;
        return new StageProductionRecord(
            Guid.NewGuid(),
            order.Id,
            stage.Id,
            request.ProductionDate,
            preview.StageQuantity,
            preview.StageQuantity,
            0m,
            subStage.Code,
            subStage.Name,
            RoundMoney(stage.PiecePrice),
            stage.StandardSeconds,
            stage.CompensationMode,
            context.Product.Code,
            context.Product.Name,
            context.Factory.Code,
            context.Factory.Name,
            context.Line.LineCode ?? string.Empty,
            context.Line.Name,
            mainStage.Name,
            Guid.NewGuid(),
            request.Notes,
            actorId,
            recordedAtUtc);
    }

    private async Task<ProductionOrder?> FindDailyOrderBySourceReferenceAsync(string sourceReference, CancellationToken ct) =>
        await db.Set<ProductionOrder>()
            .Include(order => order.ProductModel)
            .Include(order => order.StageProductionRecords)
                .ThenInclude(record => record.WorkerAllocations)
                    .ThenInclude(allocation => allocation.Worker)
            .SingleOrDefaultAsync(order => order.SourceReference == sourceReference, ct);

    private static DailyProductionDraftDto ToDailyDraftDto(ProductionOrder order, bool wasAlreadySaved) => new(
        order.Id,
        order.OrderNumber,
        order.ProductionDate,
        order.RecordedAtUtc,
        order.PlannedQuantity,
        wasAlreadySaved,
        order.StageProductionRecords
            .OrderBy(record => record.SnapshotStageCode, StringComparer.Ordinal)
            .Select(ToRecordDto)
            .ToArray());

    private static IReadOnlyCollection<string> StageWarnings(DailyProductionStageDto stage)
    {
        var warnings = new List<string>();
        if (stage.StaffingStatus == "NoStaffing") warnings.Add("لا يوجد عامل فعّال مسكّن لهذه المرحلة في تاريخ الإنتاج.");
        if (stage.HasAbsentWorkers) warnings.Add("يوجد عامل مسكّن بحالة غياب في تاريخ الإنتاج.");
        if (stage.HasNoSourceCheckInWorkers) warnings.Add("يوجد عامل مسكّن بلا تسجيل حضور من المصدر في تاريخ الإنتاج.");
        if (stage.IsFinancialReviewPending) warnings.Add("توزيع النسب لهذه المرحلة يحتاج مراجعة مدير قبل الاحتساب.");
        return warnings;
    }

    private static string AttendanceLabel(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present or AttendanceStatus.Late => "Present",
        AttendanceStatus.Absent => "Absent",
        _ => "NoSourceCheckIn"
    };

    private static IReadOnlyDictionary<Guid, decimal> SuggestedSharedPercentages(
        CompensationMode mode,
        IReadOnlyCollection<DailyWorkerContext> workers)
    {
        if (mode != CompensationMode.SharedPercentage || workers.Count == 0)
            return new Dictionary<Guid, decimal>();

        const decimal step = 0.0001m;
        var baseShare = decimal.Floor((100m / workers.Count) / step) * step;
        var remainingUnits = (int)decimal.Round((100m - baseShare * workers.Count) / step, 0, MidpointRounding.AwayFromZero);
        return workers
            .OrderBy(worker => worker.WorkerCode, StringComparer.Ordinal)
            .ThenBy(worker => worker.WorkerId)
            .Select((worker, index) => new { worker.WorkerId, Percentage = baseShare + (index < remainingUnits ? step : 0m) })
            .ToDictionary(item => item.WorkerId, item => item.Percentage);
    }

    private static string StaffingContextVersion(
        Factory factory,
        ProductionLine line,
        ProductModel product,
        DateOnly productionDate,
        IReadOnlyCollection<DailyStageContext> stages,
        IReadOnlyCollection<DailyWorkerContext> workers)
    {
        var builder = new StringBuilder();
        builder.Append(factory.Id).Append('|').Append(line.Id).Append('|').Append(product.Id).Append('|').Append(productionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var stage in stages.OrderBy(stage => stage.Entity.StageOrder).ThenBy(stage => stage.Entity.Id))
            builder.Append('|').Append(stage.Entity.Id).Append('|').Append(stage.Entity.PiecePrice.ToString(CultureInfo.InvariantCulture)).Append('|').Append(stage.Entity.CompensationMode);
        foreach (var worker in workers.OrderBy(worker => worker.WorkerId))
            builder.Append('|').Append(worker.WorkerId).Append('|').Append(worker.EffectiveSubStageId).Append('|').Append(worker.AssignmentId).Append('|').Append(worker.AttendanceStatus).Append('|').Append(worker.AttendanceAtUtc?.Ticks);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string PreviewToken(string contextVersion, DailyProductionOperationRequest request)
    {
        var builder = new StringBuilder(contextVersion);
        builder.Append('|').Append(request.FactoryId).Append('|').Append(request.ProductionLineId).Append('|').Append(request.ProductModelId)
            .Append('|').Append(request.ProductionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append('|').Append(RoundQuantity(request.LineQuantity).ToString(CultureInfo.InvariantCulture));
        foreach (var stage in request.Stages.OrderBy(stage => stage.ProductModelStageId))
        {
            builder.Append('|').Append(stage.ProductModelStageId);
            foreach (var worker in stage.Workers.OrderBy(worker => worker.WorkerId))
                builder.Append('|').Append(worker.WorkerId)
                    .Append('|').Append(worker.Percentage?.ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(worker.FixedAmount?.ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(worker.InputQuantity?.ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(worker.Notes ?? string.Empty)))
                    .Append('|').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(worker.ManualOverrideReason ?? string.Empty)));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string DailySourceReference(Guid clientRequestId) => $"DailyProductionOperations/{clientRequestId:D}";
    private static string DailyOrderNumber(DateOnly date, Guid lineId, Guid modelId) => $"DLY-{date:yyyyMMdd}-{lineId:N}-{modelId:N}";

    private sealed record ActiveWorker(Guid Id, string Code, string Name);
    private sealed record DailyWorkerContext(
        Guid WorkerId,
        string WorkerCode,
        string WorkerName,
        Guid? EffectiveSubStageId,
        string? EffectiveAssignmentType,
        string AttendanceStatus,
        bool HasSourceCheckIn,
        bool IsPresent,
        Guid? AssignmentId,
        DateTime? AttendanceAtUtc)
    {
        public DailyProductionWorkerDto ToDto(decimal? suggestedPercentage) => new(
            WorkerId,
            WorkerCode,
            WorkerName,
            IsOnActiveService: true,
            EffectiveAssignmentType,
            AttendanceStatus,
            HasSourceCheckIn,
            IsPresent,
            RequiresAuthorizedOverride: !IsPresent,
            suggestedPercentage);
    }
    private sealed record DailyStageContext(ProductModelStage Entity, DailyProductionStageDto Dto);
    private sealed record DailyContext(
        Factory Factory,
        ProductionLine Line,
        ProductModel Product,
        DateOnly ProductionDate,
        string ContextVersion,
        IReadOnlyCollection<DailyStageContext> Stages,
        IReadOnlyCollection<DailyProductionWorkerDto> ActiveWorkers)
    {
        public DailyProductionOperationsDto ToDto() => new(
            Factory.Id,
            Factory.Name,
            Line.Id,
            Line.Name,
            Product.Id,
            Product.Code,
            Product.Name,
            ProductionDate,
            ContextVersion,
            Stages.Count,
            Stages.Count(stage => stage.Dto.IsReady),
            Stages.Count(stage => stage.Dto.HasAbsentWorkers),
            Stages.Count(stage => stage.Dto.HasNoSourceCheckInWorkers),
            Stages.Count(stage => stage.Dto.StaffingStatus == "NoStaffing"),
            Stages.Count(stage => stage.Dto.IsFinancialReviewPending),
            Stages.Select(stage => stage.Dto).ToArray(),
            ActiveWorkers);
    }
    private sealed record DailyStagePreview(
        DailyStageContext Stage,
        IReadOnlyCollection<StageProductionWorkerAllocation> Allocations,
        decimal StageQuantity,
        decimal StageCost,
        IReadOnlyCollection<string> Warnings);
    private sealed record DailyPreview(
        DailyContext Context,
        decimal LineQuantity,
        string PreviewToken,
        IReadOnlyCollection<DailyStagePreview> Stages)
    {
        public DailyProductionPreviewDto ToDto() => new(
            Context.ProductionDate,
            LineQuantity,
            PreviewToken,
            RoundMoney(Stages.Sum(stage => stage.StageCost)),
            Stages.Select(stage => new DailyProductionStagePreviewDto(
                stage.Stage.Entity.Id,
                stage.Stage.Dto.StageCode,
                stage.Stage.Dto.StageName,
                LineQuantity,
                stage.StageCost,
                stage.Stage.Dto.CompensationMode,
                stage.Allocations.Select(ToAllocationDto).ToArray(),
                stage.Warnings)).ToArray(),
            Stages.SelectMany(stage => stage.Warnings).Distinct().ToArray());
    }

    private async Task<List<StageProductionWorkerAllocation>> BuildAllocationsAsync(
        ProductModelStage stage,
        decimal accepted,
        IReadOnlyCollection<WorkerAllocationRequest> workers,
        Guid actorId,
        DateTime eventAtUtc,
        CancellationToken ct)
    {
        ValidateAllocationInputs(stage.CompensationMode, workers);
        var ids = workers.Select(x => x.WorkerId).ToList();
        var workerSnapshots = await db.Set<Worker>()
            .Where(x => ids.Contains(x.Id) && x.IsActive && x.EmploymentStatus == EmploymentStatus.Active)
            .Select(x => new { x.Id, x.EmployeeCode, x.FullName })
            .ToDictionaryAsync(x => x.Id, ct);
        if (workerSnapshots.Count != ids.Count) throw new ProductionConflictException("يجب أن يكون كل عامل مشارك نشطًا وموجودًا في مصدر العمال المعتمد.");

        var assignments = await assignmentEngine.ResolveCurrentAssignmentsAsync(ids, eventAtUtc, ct);
        if (assignments.IsFailure) throw new ProductionConflictException("تعذر التحقق من تعيينات العمال الفعلية.");
        var attendance = await attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(ids, eventAtUtc, ct);
        if (attendance.IsFailure) throw new ProductionConflictException("تعذر التحقق من حضور العمال من مصدر الحضور.");

        var workersRequiringOverride = workers.Where(worker =>
        {
            var assignedToStage = assignments.Value!.TryGetValue(worker.WorkerId, out var assignment) && assignment.EffectiveSubStageId == stage.SubStageId;
            var isPresent = attendance.Value!.TryGetValue(worker.WorkerId, out var state) && state.Status is AttendanceStatus.Present or AttendanceStatus.Late;
            return !assignedToStage || !isPresent;
        }).ToArray();

        if (workersRequiringOverride.Length > 0)
        {
            if (workersRequiringOverride.Any(worker => string.IsNullOrWhiteSpace(worker.ManualOverrideReason)))
                throw new ProductionConflictException("العامل غير الحاضر أو غير المعيّن للمرحلة يتطلب سبب تجاوز يدوي واضح.");

            var permissions = await permissionService.GetEffectivePermissionsAsync(actorId, ct);
            if (!permissions.Contains(ManualParticipantOverridePermission, StringComparer.OrdinalIgnoreCase))
                throw new ProductionConflictException("لا تملك صلاحية التجاوز اليدوي لتعيين أو حضور العامل.");
        }

        var calculatedAmounts = CalculateAllocationAmounts(stage.CompensationMode, accepted, stage.PiecePrice, workers);
        return workers.Select(x =>
        {
            var worker = workerSnapshots[x.WorkerId];
            var calculated = calculatedAmounts.Single(amount => amount.WorkerId == x.WorkerId);
            var allocation = new StageProductionWorkerAllocation(Guid.NewGuid(), x.WorkerId, worker.EmployeeCode, worker.FullName, x.Percentage, x.FixedAmount, x.Notes, x.ManualOverrideReason, x.InputQuantity);
            allocation.SetCalculatedAmounts(calculated.EquivalentQuantity, calculated.CalculatedEarning);
            return allocation;
        }).ToList();
    }

    private async Task<(ProductionOrder Order, ProductModelStage Stage)> LoadOrderAndStageAsync(Guid orderId, Guid stageId, CancellationToken ct)
    {
        var order = await OrderAsync(orderId, ct);
        var stage = await db.Set<ProductModelStage>()
            .Include(x => x.SubStage)
            .ThenInclude(x => x!.MainStage)
            .ThenInclude(x => x!.ProductionLine)
            .ThenInclude(x => x!.Factory)
            .SingleOrDefaultAsync(x => x.Id == stageId && x.ProductModelId == order.ProductModelId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Active model stage was not found for this production order.");

        var line = stage.SubStage?.MainStage?.ProductionLine;
        if (stage.SubStage is null || !stage.SubStage.IsActive || stage.SubStage.MainStage is null || !stage.SubStage.MainStage.IsActive || line is null || !line.IsActive || line.Factory is null || !line.Factory.IsActive)
            throw new ProductionConflictException("The selected production stage is no longer active in the factory structure.");
        if (!order.ProductionLineId.HasValue)
            throw new ProductionConflictException("لا يمكن تسجيل دفعة لأمر إنتاج لا يحتوي على خط إنتاج صالح.");
        if (order.ProductionLineId.Value != line.Id)
            throw new ProductionConflictException("The selected stage does not belong to the production order line.");

        return (order, stage);
    }

    private static StageProductionRecord CreateSnapshotRecord(ProductionOrder order, ProductModelStage stage, DateOnly productionDate, decimal producedQuantity, decimal acceptedQuantity, decimal rejectedQuantity, Guid clientRequestId, string? notes, Guid actorId, DateTime atUtc)
    {
        var subStage = stage.SubStage!;
        var mainStage = subStage.MainStage!;
        var line = mainStage.ProductionLine!;
        var factory = line.Factory!;
        return new StageProductionRecord(Guid.NewGuid(), order.Id, stage.Id, productionDate, RoundQuantity(producedQuantity), RoundQuantity(acceptedQuantity), RoundQuantity(rejectedQuantity), subStage.Code, subStage.Name, RoundMoney(stage.PiecePrice), stage.StandardSeconds, stage.CompensationMode, order.ProductModel!.Code, order.ProductModel.Name, factory.Code, factory.Name, line.LineCode ?? string.Empty, line.Name, mainStage.Name, clientRequestId, notes, actorId, atUtc);
    }
    private static void EnsureRecordableOrder(ProductionOrder order)
    {
        if (order.Status == ProductionOrderStatus.Active) return;
        if (order.Status == ProductionOrderStatus.Draft && order.SourceImportBatchId.HasValue) return;
        throw new ProductionConflictException("Production can only be recorded against an active production order.");
    }
    private static void EnsurePersistedFinancialConsistency(StageProductionRecord record)
    {
        try
        {
            record.EnsureFinancialConsistency();
        }
        catch (InvalidOperationException)
        {
            throw new ProductionConflictException("لا يمكن اعتماد السجل لأن إجمالي المستحقات لا يطابق مجموع مستحقات العمال المحفوظة. أعد حساب المعاينة واحفظ المسودة من جديد.");
        }
    }
    private static void EnsureProductionApprovalCanBeCancelled(StageProductionRecord record, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ProductionConflictException("سبب إلغاء اعتماد الإنتاج مطلوب.");
        if (record.Status == StageProductionRecordStatus.Cancelled) throw new ProductionConflictException("تم إلغاء اعتماد الإنتاج لهذا السجل بالفعل.");
        if (record.Status != StageProductionRecordStatus.Approved) throw new ProductionConflictException("لا يمكن إلغاء اعتماد إلا لسجل إنتاج معتمد.");
        // A future financial-approval state is checked here. Once that state exists,
        // it must block this transition and require a financial reversal instead.
    }
    private static void EnsureCurrentVersion(StageProductionRecord record, Guid supplied) { if (supplied == Guid.Empty || record.ConcurrencyToken != supplied) throw new ProductionConflictException("The production record has changed. Refresh and try again."); }
    private async Task<ProductionOrder> OrderAsync(Guid id, CancellationToken ct, bool includeRecords = false) { var query = db.Set<ProductionOrder>().Include(x => x.ProductModel).AsQueryable(); if (includeRecords) query = query.Include(x => x.StageProductionRecords); return await query.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Production order was not found."); }
    private async Task<ProductionOrderDto> OrderDtoAsync(Guid id, CancellationToken ct) { var x = await OrderAsync(id, ct); return new(x.Id, x.OrderNumber, x.ProductModelId, x.ProductModel!.Code, x.ProductionLineId, x.ProductionDate, x.PlannedQuantity, x.Status.ToString(), x.Notes, x.SourceImportBatchId.HasValue, x.RecordedAtUtc, x.ApprovedAtUtc); }
    private async Task<StageProductionRecord> RecordAsync(Guid id, CancellationToken ct) => await db.Set<StageProductionRecord>().Include(x => x.ProductionOrder).ThenInclude(x => x!.ProductModel).Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Production record was not found.");
    private async Task<StageProductionRecordDto> GetRecordByClientRequestAsync(Guid orderId, Guid clientRequestId, CancellationToken ct) => ToRecordDto(await db.Set<StageProductionRecord>().Include(x => x.ProductionOrder).ThenInclude(x => x!.ProductModel).Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).SingleAsync(x => x.ProductionOrderId == orderId && x.ClientRequestId == clientRequestId, ct));
    private static StageProductionRecordDto ToRecordDto(StageProductionRecord x) => new(x.Id, x.ProductionOrderId, x.ProductModelStageId, x.ProductionDate, x.ProducedQuantity, x.AcceptedQuantity, x.RejectedQuantity, x.Status.ToString(), x.SnapshotStageCode, x.SnapshotStageName, x.SnapshotProductModelCode, x.SnapshotProductModelName, x.SnapshotFactoryCode, x.SnapshotFactoryName, x.SnapshotProductionLineCode, x.SnapshotProductionLineName, x.SnapshotMainStageName, x.SnapshotPiecePrice, x.SnapshotStandardSeconds, x.SnapshotCompensationMode.ToString(), x.TotalWorkerEarnings, x.ConcurrencyToken, x.WorkerAllocations.Select(ToAllocationDto).ToList(), x.Notes, x.ApprovedBy, x.ApprovedAtUtc, x.CancelledBy, x.CancelledAtUtc, x.ApprovalCancellationReason);
    private static ProductionWorkerAllocationDto ToAllocationDto(StageProductionWorkerAllocation x) => new(x.WorkerId, x.SnapshotWorkerCode, x.SnapshotWorkerName, x.Percentage, x.FixedAmount, x.InputQuantity, x.EquivalentQuantity, x.CalculatedEarning, x.Notes, x.ManualOverrideReason);
    private async Task AuditAsync(Guid actorId, AuditActionType action, string entityType, Guid entityId, object? before, object? after, CancellationToken ct) { var result = await audit.RecordAsync(actorId, action, entityType, entityId.ToString(), before, after, "ProductionCostRecording", ct); if (result.IsFailure) throw new InvalidOperationException(result.Error?.Message ?? "Production audit failed."); }
    private static object OrderAudit(ProductionOrder x) => new { x.Id, x.OrderNumber, x.Status, x.ProductionDate, x.PlannedQuantity, x.ConcurrencyToken, Result = "Success" };
    private static object RecordAudit(StageProductionRecord x) => new { x.Id, x.ProductionOrderId, x.ProductModelStageId, x.Status, x.ProductionDate, x.ProducedQuantity, x.AcceptedQuantity, x.RejectedQuantity, x.SnapshotProductModelCode, x.SnapshotProductModelName, x.SnapshotFactoryCode, x.SnapshotFactoryName, x.SnapshotProductionLineCode, x.SnapshotProductionLineName, x.SnapshotMainStageName, x.SnapshotStageCode, x.SnapshotStageName, x.SnapshotPiecePrice, x.SnapshotStandardSeconds, x.SnapshotCompensationMode, x.TotalWorkerEarnings, x.ApprovedBy, x.ApprovedAtUtc, x.CancelledBy, x.CancelledAtUtc, x.ApprovalCancellationReason, x.ClientRequestId, x.ConcurrencyToken, Result = "Success" };
    private static object AllocationAudit(StageProductionRecord x) => new { x.Id, x.Status, WorkerCount = x.WorkerAllocations.Count, TotalEarnings = x.WorkerAllocations.Sum(a => a.CalculatedEarning), Result = "Success" };
    private static object FinancialAudit(StageProductionRecord x) => new { RecordId = x.Id, OrderId = x.ProductionOrderId, OrderNumber = x.ProductionOrder?.OrderNumber, x.SnapshotProductModelCode, x.SnapshotProductModelName, x.SnapshotFactoryCode, x.SnapshotFactoryName, x.SnapshotProductionLineCode, x.SnapshotProductionLineName, x.SnapshotMainStageName, x.SnapshotStageCode, x.SnapshotStageName, x.SnapshotPiecePrice, x.SnapshotStandardSeconds, CompensationMode = x.SnapshotCompensationMode, x.Status, x.ProducedQuantity, x.AcceptedQuantity, x.RejectedQuantity, TotalEarnings = x.TotalWorkerEarnings, x.ApprovedBy, x.ApprovedAtUtc, x.CancelledBy, x.CancelledAtUtc, x.ApprovalCancellationReason, x.ClientRequestId, Allocations = x.WorkerAllocations.Take(100).Select(a => new { a.WorkerId, a.SnapshotWorkerCode, a.SnapshotWorkerName, a.Percentage, a.FixedAmount, a.EquivalentQuantity, a.CalculatedEarning, a.ManualOverrideReason }).ToArray(), Result = "Success" };

    private static void ValidateAllocationInputs(CompensationMode mode, IReadOnlyCollection<WorkerAllocationRequest> workers)
    {
        if (workers is null || workers.Count == 0) throw new ProductionConflictException("يجب إضافة عامل واحد على الأقل إلى دفعة الإنتاج.");
        if (workers.Select(x => x.WorkerId).Distinct().Count() != workers.Count) throw new ProductionConflictException("لا يمكن إضافة العامل نفسه أكثر من مرة في دفعة الإنتاج.");

        var name = mode.ToString();
        if (name == "SharedPercentage" && workers.Any(x => !x.Percentage.HasValue || x.Percentage <= 0 || x.FixedAmount.HasValue) || name != "SharedPercentage" && workers.Any(x => x.Percentage.HasValue)) throw new ProductionConflictException("بيانات توزيع العمال لا تطابق طريقة احتساب التكلفة المعتمدة.");
        if (name == "SharedPercentage" && workers.Sum(x => x.Percentage!.Value) != 100m) throw new ProductionConflictException("يجب أن يكون مجموع نسب العمال 100٪ تمامًا.");
        if (name == "FixedAmount" && workers.Any(x => !x.FixedAmount.HasValue || x.FixedAmount < 0)) throw new ProductionConflictException("طريقة القيمة الثابتة تتطلب قيمة صالحة لكل عامل.");
        if (name != "FixedAmount" && workers.Any(x => x.FixedAmount.HasValue)) throw new ProductionConflictException("القيمة الثابتة متاحة فقط لطريقة احتساب التكلفة الثابتة.");
    }

    private static IReadOnlyCollection<CalculatedAllocation> CalculateAllocationAmounts(CompensationMode mode, decimal acceptedQuantity, decimal piecePrice, IReadOnlyCollection<WorkerAllocationRequest> workers)
    {
        ValidateAllocationInputs(mode, workers);
        var accepted = RoundQuantity(acceptedQuantity);
        var price = RoundMoney(piecePrice);
        var modeName = mode.ToString();

        return workers.Select(worker =>
        {
            var equivalentQuantity = modeName == "SharedPercentage"
                ? RoundQuantity(accepted * worker.Percentage!.Value / 100m)
                : 0m;
            var calculatedEarning = modeName == "SharedPercentage"
                ? RoundMoney(equivalentQuantity * price)
                : modeName == "FixedAmount"
                    ? RoundMoney(worker.FixedAmount!.Value)
                    : RoundMoney(accepted * price);
            return new CalculatedAllocation(worker.WorkerId, equivalentQuantity, calculatedEarning);
        }).ToArray();
    }

    private static decimal RoundQuantity(decimal value) => decimal.Round(value, QuantityScale, MidpointRounding.AwayFromZero);
    private static decimal RoundMoney(decimal value) => decimal.Round(value, MoneyScale, MidpointRounding.AwayFromZero);
    private static DateTime ProductionDateEvidenceAtUtc(DateOnly productionDate)
    {
        var egypt = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var localEnd = productionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(localEnd, egypt).AddTicks(-1);
    }

    private sealed record CalculatedAllocation(Guid WorkerId, decimal EquivalentQuantity, decimal CalculatedEarning);
}
