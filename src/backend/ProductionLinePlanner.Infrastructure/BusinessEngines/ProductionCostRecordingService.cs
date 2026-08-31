using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    IPermissionService permissionService,
    ICairoTimeZoneProvider cairoTimeZoneProvider,
    ILogger<ProductionCostRecordingService>? logger = null) : IProductionCostRecordingService
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
        record.ReplaceAllocations(await BuildAllocationsAsync(stage, order.ProductionLineId!.Value, record.AcceptedQuantity, request.Workers, actorId, ProductionDateEvidenceAtUtc(request.ProductionDate), ct)); db.Add(record);
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
        record.ReplaceAllocations(await BuildAllocationsAsync(stage, order.ProductionLineId!.Value, record.AcceptedQuantity, request.Workers, actorId, ProductionDateEvidenceAtUtc(request.ProductionDate), ct));
        record.SetCalculationPreview(record.WorkerAllocations.Sum(x => x.CalculatedEarning));
        return ToRecordDto(record);
    }

    public async Task<StageProductionRecordDto> UpdateDraftAsync(Guid id, UpdateStageProductionRecordRequest request, Guid actorId, CancellationToken ct)
    {
        var record = await RecordAsync(id, ct); EnsureCurrentVersion(record, request.ConcurrencyToken); db.Entry(record).Property(x => x.ConcurrencyToken).OriginalValue = request.ConcurrencyToken; EnsureRecordableOrder(record.ProductionOrder!); var before = RecordAudit(record); var allocationsBefore = AllocationAudit(record);
        var (order, stage) = await LoadOrderAndStageAsync(record.ProductionOrderId, record.ProductModelStageId, ct);
        record.UpdateDraft(request.ProductionDate, RoundQuantity(request.ProducedQuantity), RoundQuantity(request.AcceptedQuantity), RoundQuantity(request.RejectedQuantity), request.Notes); var allocations = await BuildAllocationsAsync(stage, order.ProductionLineId!.Value, record.AcceptedQuantity, request.Workers, actorId, ProductionDateEvidenceAtUtc(request.ProductionDate), ct); var allocationChanges = record.ReplaceAllocations(allocations); db.RemoveRange(allocationChanges.Removed); db.AddRange(allocationChanges.Added);
        await AuditAsync(actorId, AuditActionType.Update, "StageProductionRecord", id, before, RecordAudit(record), ct); await AuditAsync(actorId, AuditActionType.Update, "StageProductionWorkerAllocation", id, allocationsBefore, AllocationAudit(record), ct); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new ProductionConflictException("The production record changed while it was being saved. Refresh and try again."); } return await GetRecordAsync(id, ct);
    }

    public async Task<StageProductionRecordDto> GetRecordAsync(Guid id, CancellationToken ct) => ToRecordDto(await RecordAsync(id, ct));

    public async Task<IReadOnlyCollection<StageProductionRecordDto>> ListRecordsAsync(DateOnly? from, DateOnly? to, StageProductionRecordStatus? status, CancellationToken ct)
    { var q = db.Set<StageProductionRecord>().AsNoTracking().Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).Where(x => (!from.HasValue || x.ProductionDate >= from) && (!to.HasValue || x.ProductionDate <= to) && (!status.HasValue || x.Status == status)); return (await q.OrderByDescending(x => x.ProductionDate).ToListAsync(ct)).Select(ToRecordDto).ToList(); }

    public async Task<StageProductionRecordDto> ApproveAsync(Guid id, Guid concurrencyToken, Guid actorId, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var record = await RecordAsync(id, ct);
        if (IsDailyOperation(record.ProductionOrder!))
            throw new ProductionConflictException("يجب اعتماد مسودة تشغيل اليوم كاملة من إجراء اعتماد تشغيل اليوم.");
        if (record.ProductionOrder!.SourceImportBatchId.HasValue) throw new ProductionConflictException("Imported production stages can only be approved with their complete production day.");
        EnsureCurrentVersion(record, concurrencyToken);
        if (record.Status == StageProductionRecordStatus.Approved) return ToRecordDto(record);
        if (record.Status == StageProductionRecordStatus.Cancelled) throw new ProductionConflictException("لا يمكن اعتماد سجل تم إلغاء اعتماد الإنتاج له. أنشئ مسودة تصحيح مستقلة عند الحاجة.");
        db.Entry(record).Property(x => x.ConcurrencyToken).OriginalValue = concurrencyToken; EnsureRecordableOrder(record.ProductionOrder!);
        var currentAccepted = await db.Set<StageProductionRecord>().Where(x => x.ProductionOrderId == record.ProductionOrderId && x.Id != record.Id && x.Status == StageProductionRecordStatus.Approved).SumAsync(x => (decimal?)x.AcceptedQuantity, ct) ?? 0m;
        if (currentAccepted + record.AcceptedQuantity > record.ProductionOrder!.PlannedQuantity) throw new ProductionConflictException("Approved accepted quantity exceeds the production order planned quantity.");
        EnsurePersistedFinancialConsistency(record);
        var before = FinancialAudit(record);
        var (order, currentStage) = await LoadOrderAndStageAsync(record.ProductionOrderId, record.ProductModelStageId, ct);
        // Validate current attendance and assignment eligibility without recalculating or
        // rewriting the draft's stored financial snapshot.
        await BuildAllocationsAsync(currentStage, order.ProductionLineId!.Value, record.AcceptedQuantity, record.WorkerAllocations.Select(x => new WorkerAllocationRequest(x.WorkerId, x.Percentage, x.FixedAmount, x.Notes, x.ManualOverrideReason, x.InputQuantity)).ToList(), actorId, ProductionDateEvidenceAtUtc(record.ProductionDate), ct);
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

    public async Task<DailyProductionDraftDto> CreateDailyDraftAsync(
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

        var draftPlan = await BuildDailyDraftSavePlanAsync(request, actorId, ct);

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

                throw new ProductionConflictException("يوجد تشغيل يومي بالفعل لهذا التاريخ والخط والموديل. حدّث التشغيل الموجود باستخدام معرّفه بدل إنشاء مسودة جديدة.");
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

            foreach (var stagePreview in draftPlan.Stages)
            {
                var record = CreateDailySnapshotRecord(order, draftPlan.Context, stagePreview, request, actorId, now);
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

    public async Task<DailyProductionDraftDto> UpdateDailyDraftAsync(
        Guid productionOrderId,
        DailyProductionDraftUpdateRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        logger?.LogDebug(
            "Daily draft update service entered {ProductionOrderId} {StageCount} {WorkerAllocationCount}",
            productionOrderId,
            request.Stages?.Count ?? 0,
            request.Stages?.Sum(stage => stage.Workers?.Count ?? 0) ?? 0);
        if (productionOrderId == Guid.Empty)
            throw new ArgumentException("معرّف تشغيل اليوم مطلوب.", nameof(productionOrderId));
        if (request.ConcurrencyToken == Guid.Empty)
            throw new ProductionConflictException("رمز تزامن تشغيل اليوم مطلوب للتحديث.");
        if (request.Stages is null || request.Stages.Count == 0 ||
            request.Stages.Any(stage => stage.StageProductionRecordId == Guid.Empty || stage.ProductModelStageId == Guid.Empty || stage.ConcurrencyToken == Guid.Empty) ||
            request.Stages.Select(stage => stage.StageProductionRecordId).Distinct().Count() != request.Stages.Count ||
            request.Stages.Select(stage => stage.ProductModelStageId).Distinct().Count() != request.Stages.Count)
        {
            throw new ProductionConflictException("معرّف ورمز تزامن كل مرحلة محفوظة مطلوبان لتحديث تشغيل اليوم.");
        }

        if (request.LineQuantity <= 0)
            throw new ProductionConflictException("كمية تشغيل الخط يجب أن تكون أكبر من صفر.");

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        var order = await db.Set<ProductionOrder>()
            .AsTracking()
            .Include(current => current.ProductModel)
            .Include(current => current.ProductionLine)
            .Include(current => current.StageProductionRecords)
                .ThenInclude(record => record.WorkerAllocations)
                    .ThenInclude(allocation => allocation.Worker)
            .SingleOrDefaultAsync(current => current.Id == productionOrderId, ct)
            ?? throw new KeyNotFoundException("لم يتم العثور على تشغيل اليوم المطلوب.");

        if (!IsDailyOperation(order))
            throw new ProductionConflictException("هذا الأمر ليس تشغيلًا يوميًا قابلًا للتحديث.");
        if (order.Status != ProductionOrderStatus.Draft)
            throw new ProductionConflictException("لم يعد تشغيل اليوم في حالة مسودة قابلة للتحديث.");
        if (order.ProductionDate != request.ProductionDate ||
            order.ProductionLineId != request.ProductionLineId ||
            order.ProductModelId != request.ProductModelId ||
            order.ProductionLine?.FactoryId != request.FactoryId)
            throw new ProductionConflictException("لا تطابق بيانات التحديث تشغيل اليوم المحدد في المسار.");
        if (order.ConcurrencyToken != request.ConcurrencyToken)
            throw new ProductionConflictException("تغير تشغيل اليوم منذ آخر تحميل. حدّث البيانات وحاول مرة أخرى.");

        if (order.StageProductionRecords.Select(record => record.ProductModelStageId).Distinct().Count() != order.StageProductionRecords.Count ||
            order.StageProductionRecords.Select(record => record.Id).Distinct().Count() != order.StageProductionRecords.Count)
        {
            throw new ProductionConflictException("تحتوي المسودة المحفوظة على هويات مراحل مكررة ولا يمكن تحديثها بأمان.");
        }

        var recordsByStage = order.StageProductionRecords.ToDictionary(record => record.ProductModelStageId);
        var updatesByStage = request.Stages.ToDictionary(stage => stage.ProductModelStageId);
        if (recordsByStage.Count != updatesByStage.Count || !recordsByStage.Keys.ToHashSet().SetEquals(updatesByStage.Keys))
            throw new ProductionConflictException("يجب أن يطابق التحديث هويات مراحل المسودة المحفوظة دون إضافة أو حذف أو استبدال.");

        var currentActiveStages = order.ProductionLine?.DepartmentId is Guid departmentId
            ? (await CurrentActiveDailyStagesQuery(order.ProductModelId, order.ProductionLineId!.Value, departmentId)
                .AsNoTracking()
                .Select(stage => new DailyDraftStageConfiguration(
                    stage.Id,
                    stage.SubStage!.Code,
                    stage.SubStage.Name,
                    stage.SubStage.MainStage!.Name,
                    stage.PiecePrice,
                    stage.StandardSeconds,
                    stage.CompensationMode))
                .ToArrayAsync(ct)).ToDictionary(stage => stage.ProductModelStageId)
            : new Dictionary<Guid, DailyDraftStageConfiguration>();

        foreach (var (productModelStageId, record) in recordsByStage)
        {
            var update = updatesByStage[productModelStageId];
            if (record.Id != update.StageProductionRecordId || record.ConcurrencyToken != update.ConcurrencyToken)
                throw new ProductionConflictException("تغيرت إحدى مراحل تشغيل اليوم منذ آخر تحميل. حدّث البيانات وحاول مرة أخرى.");
            if (record.Status is not (StageProductionRecordStatus.Draft or StageProductionRecordStatus.Cancelled))
                throw new ProductionConflictException("تحتوي مسودة تشغيل اليوم على مرحلة غير قابلة للتصحيح.");
        }

        db.Entry(order).Property(current => current.ConcurrencyToken).OriginalValue = request.ConcurrencyToken;
        foreach (var (productModelStageId, record) in recordsByStage)
        {
            var token = db.Entry(record).Property(current => current.ConcurrencyToken);
            token.OriginalValue = updatesByStage[productModelStageId].ConcurrencyToken;
            if (!currentActiveStages.ContainsKey(productModelStageId))
            {
                // Historical records remain in the atomic update contract. Marking
                // the token as a no-op update makes EF include its original value in
                // the optimistic-concurrency check without recalculating the record.
                token.IsModified = true;
            }
        }

        var updatedAtUtc = DateTime.UtcNow;
        var orderBefore = DailyDraftAudit(order);
        order.UpdateDraft(request.ProductionDate, RoundQuantity(request.LineQuantity), request.Notes, actorId, updatedAtUtc);

        foreach (var (productModelStageId, record) in recordsByStage)
        {
            var update = updatesByStage[productModelStageId];
            if (!currentActiveStages.TryGetValue(productModelStageId, out var currentStage))
            {
                if (record.Status == StageProductionRecordStatus.Cancelled)
                {
                    var historicalRecordBefore = RecordAudit(record);
                    record.ReopenDailyDraftAfterApprovalCancellation();
                    await AuditAsync(actorId, AuditActionType.Update, "StageProductionRecord", record.Id, historicalRecordBefore, RecordAudit(record), ct);
                }
                continue;
            }

            var recordBefore = RecordAudit(record);
            if (record.Status == StageProductionRecordStatus.Cancelled)
                record.ReopenDailyDraftAfterApprovalCancellation();
            record.RefreshDraftStageConfiguration(
                currentStage.StageCode,
                currentStage.StageName,
                currentStage.MainStageName,
                currentStage.PiecePrice,
                currentStage.StandardSeconds,
                currentStage.CompensationMode);
            record.UpdateDraft(request.ProductionDate, RoundQuantity(request.LineQuantity), RoundQuantity(request.LineQuantity), 0m, request.Notes);
            var allocations = await BuildPermissiveDailyDraftAllocationsAsync(
                currentStage.CompensationMode,
                currentStage.PiecePrice,
                request.LineQuantity,
                update.Workers,
                record.WorkerAllocations,
                ct);
            var allocationChanges = record.ReplaceAllocations(allocations);
            db.RemoveRange(allocationChanges.Removed);
            db.AddRange(allocationChanges.Added);
            await AuditAsync(actorId, AuditActionType.Update, "StageProductionRecord", record.Id, recordBefore, RecordAudit(record), ct);
            await AuditAsync(actorId, AuditActionType.Update, "StageProductionWorkerAllocation", record.Id, null, AllocationAudit(record), ct);
        }

        await AuditAsync(actorId, AuditActionType.Update, "ProductionOrder", order.Id, orderBefore, DailyDraftAudit(order), ct);
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger?.LogWarning(
                exception,
                "Daily draft update concurrency conflict {ProductionOrderId} {Entries}",
                productionOrderId,
                string.Join(", ", exception.Entries.Select(entry => $"{entry.Metadata.ClrType.Name}:{entry.State}")));
            throw new ProductionConflictException("تغير تشغيل اليوم أثناء الحفظ. حدّث البيانات وحاول مرة أخرى.");
        }

        return ToDailyDraftDto(order, wasAlreadySaved: false);
    }

    public async Task<DailyProductionApprovalDto> ApproveDailyOperationAsync(
        Guid productionOrderId,
        DailyProductionApprovalRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        if (productionOrderId == Guid.Empty)
            throw new ArgumentException("معرّف تشغيل اليوم مطلوب.", nameof(productionOrderId));
        if (request.StageApprovals is null || request.StageApprovals.Count == 0 ||
            request.StageApprovals.Any(stage => stage.StageProductionRecordId == Guid.Empty || stage.ConcurrencyToken == Guid.Empty) ||
            request.StageApprovals.Select(stage => stage.StageProductionRecordId).Distinct().Count() != request.StageApprovals.Count)
            throw new ArgumentException("يجب إرسال معرّف وتزامن كل مرحلة محفوظة لاعتماد تشغيل اليوم.", nameof(request));

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        var order = await db.Set<ProductionOrder>()
            .Include(current => current.StageProductionRecords)
                .ThenInclude(record => record.WorkerAllocations)
            .SingleOrDefaultAsync(current => current.Id == productionOrderId, ct)
            ?? throw new KeyNotFoundException("لم يتم العثور على تشغيل اليوم المطلوب.");

        if (!IsDailyOperation(order))
            throw new ProductionConflictException("هذا الأمر ليس مسودة تشغيل يومي مدعومة للاعتماد الذري.");
        if (order.Status == ProductionOrderStatus.Cancelled)
            throw new ProductionConflictException("لا يمكن اعتماد تشغيل يوم ملغى.");
        if (order.Status != ProductionOrderStatus.Draft)
            throw new ProductionConflictException("لم تعد مسودة تشغيل اليوم في حالة قابلة للاعتماد.");

        var records = order.StageProductionRecords.OrderBy(record => record.Id).ToArray();
        if (records.Length == 0)
            throw new ProductionConflictException("لا يمكن اعتماد تشغيل يوم بلا مراحل محفوظة.");

        var suppliedTokens = request.StageApprovals.ToDictionary(stage => stage.StageProductionRecordId, stage => stage.ConcurrencyToken);
        if (suppliedTokens.Count != records.Length || records.Any(record => !suppliedTokens.ContainsKey(record.Id)))
            throw new ProductionConflictException("تغيرت مراحل المسودة أو لم تُرسل جميع رموز التزامن. حدّث التشغيل وحاول مرة أخرى.");

        // Validate all records before changing any status. A daily line quantity is
        // intentionally repeated as a stage snapshot, so it is never summed here.
        foreach (var record in records)
        {
            if (record.Status != StageProductionRecordStatus.Draft)
                throw new ProductionConflictException("تغيرت حالة إحدى مراحل تشغيل اليوم. حدّث التشغيل وحاول مرة أخرى.");
            if (record.ConcurrencyToken != suppliedTokens[record.Id])
                throw new ProductionConflictException("تغيرت إحدى مراحل تشغيل اليوم. حدّث التشغيل وحاول مرة أخرى.");

            db.Entry(record).Property(current => current.ConcurrencyToken).OriginalValue = suppliedTokens[record.Id];
            EnsurePersistedFinancialConsistency(record);
            EnsureDailyApprovalBusinessConsistency(record);
        }

        var now = DateTime.UtcNow;
        var orderBefore = DailyApprovalOrderAudit(order, records.Length);
        var recordBefore = records.ToDictionary(record => record.Id, record => DailyApprovalRecordAudit(record, records.Length));

        foreach (var record in records)
            record.Approve(actorId, now);
        order.ApproveDay(actorId, now);

        foreach (var record in records)
            await AuditAsync(actorId, AuditActionType.Update, "StageProductionRecord", record.Id, recordBefore[record.Id], DailyApprovalRecordAudit(record, records.Length), ct);
        await AuditAsync(actorId, AuditActionType.Update, "ProductionOrder", order.Id, orderBefore, DailyApprovalOrderAudit(order, records.Length), ct);

        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ProductionConflictException("تغيرت مسودة تشغيل اليوم أثناء الاعتماد. حدّث البيانات وحاول مرة أخرى.");
        }

        return new DailyProductionApprovalDto(order.Id, order.Status.ToString(), order.ApprovedAtUtc!.Value, records.Length);
    }

    public async Task<DailyProductionDraftDto> CancelDailyOperationApprovalAsync(
        Guid productionOrderId,
        DailyProductionApprovalCancellationRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        if (productionOrderId == Guid.Empty)
            throw new ArgumentException("معرّف تشغيل اليوم مطلوب.", nameof(productionOrderId));
        if (string.IsNullOrWhiteSpace(request.Reason) || request.StageApprovals is null || request.StageApprovals.Count == 0 ||
            request.StageApprovals.Any(stage => stage.StageProductionRecordId == Guid.Empty || stage.ConcurrencyToken == Guid.Empty) ||
            request.StageApprovals.Select(stage => stage.StageProductionRecordId).Distinct().Count() != request.StageApprovals.Count)
            throw new ProductionConflictException("سبب إلغاء الاعتماد ورموز تزامن جميع مراحل التشغيل مطلوبة.");

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        var order = await db.Set<ProductionOrder>()
            .Include(current => current.StageProductionRecords)
                .ThenInclude(record => record.WorkerAllocations)
            .SingleOrDefaultAsync(current => current.Id == productionOrderId, ct)
            ?? throw new KeyNotFoundException("لم يتم العثور على تشغيل اليوم المطلوب.");

        if (!IsDailyOperation(order) || order.Status != ProductionOrderStatus.Completed)
            throw new ProductionConflictException("لا يمكن إلغاء اعتماد إلا لتشغيل يوم معتمد.");

        var records = order.StageProductionRecords.OrderBy(record => record.Id).ToArray();
        var suppliedTokens = request.StageApprovals.ToDictionary(stage => stage.StageProductionRecordId, stage => stage.ConcurrencyToken);
        if (records.Length == 0 || suppliedTokens.Count != records.Length || records.Any(record => record.Status != StageProductionRecordStatus.Approved || !suppliedTokens.TryGetValue(record.Id, out var token) || record.ConcurrencyToken != token))
            throw new ProductionConflictException("تغيرت مراحل تشغيل اليوم أو حالة اعتمادها. حدّث البيانات وحاول مرة أخرى.");

        foreach (var record in records)
            db.Entry(record).Property(current => current.ConcurrencyToken).OriginalValue = suppliedTokens[record.Id];

        var now = DateTime.UtcNow;
        var orderBefore = DailyApprovalOrderAudit(order, records.Length);
        var recordBefore = records.ToDictionary(record => record.Id, record => DailyApprovalRecordAudit(record, records.Length));
        foreach (var record in records)
            record.CancelProductionApproval(request.Reason, actorId, now);
        order.ReopenDailyOperationAfterApprovalCancellation(actorId, now);

        foreach (var record in records)
            await AuditAsync(actorId, AuditActionType.Cancel, "StageProductionRecord", record.Id, recordBefore[record.Id], DailyApprovalCancellationRecordAudit(record, records.Length), ct);
        await AuditAsync(actorId, AuditActionType.Cancel, "ProductionOrder", order.Id, orderBefore, DailyApprovalCancellationOrderAudit(order, records.Length, request.Reason), ct);

        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ProductionConflictException("تغيرت حالة تشغيل اليوم أثناء إلغاء الاعتماد. حدّث البيانات وحاول مرة أخرى.");
        }

        return ToDailyDraftDto(order, wasAlreadySaved: false);
    }

    private async Task<DailyPreview> BuildDailyPreviewAsync(
        DailyProductionOperationRequest request,
        Guid actorId,
        CancellationToken ct,
        string? stageConfigurationConflictMessage = null)
    {
        if (request.LineQuantity <= 0)
            throw new ProductionConflictException("كمية تشغيل الخط يجب أن تكون أكبر من صفر.");

        var context = await LoadDailyContextAsync(
            request.FactoryId,
            request.ProductionLineId,
            request.ProductModelId,
            request.ProductionDate,
            ct,
            stageConfigurationConflictMessage);
        ValidateDailyStages(request, context, stageConfigurationConflictMessage);

        var requestByStage = request.Stages.ToDictionary(stage => stage.ProductModelStageId);
        // LoadDailyContextAsync has already resolved the authoritative active-worker,
        // assignment, and attendance state for this exact production date. Reusing
        // that immutable read context keeps a full production-day preview read-only
        // and avoids repeating those source lookups once for every mapped stage.
        var activeWorkers = context.ActiveWorkers.ToDictionary(worker => worker.WorkerId);
        var stagePreviews = new List<DailyStagePreview>(context.Stages.Count);
        foreach (var stage in context.Stages)
        {
            var stageRequest = requestByStage[stage.Entity.Id];
            var requestedWorkerIds = stageRequest.Workers.Select(worker => worker.WorkerId).ToArray();
            if (requestedWorkerIds.Distinct().Count() != requestedWorkerIds.Length)
                throw new ProductionConflictException("لا يمكن تكرار العامل داخل المرحلة نفسها في تشغيل اليوم.");
            if (requestedWorkerIds.Any(workerId => !activeWorkers.ContainsKey(workerId)))
                throw new ProductionConflictException("يجب أن يكون كل عامل مشارك نشطًا وموجودًا في مصدر العمال المعتمد.");
            var assignedWorkers = stage.Workers.ToDictionary(worker => worker.WorkerId);
            var readyAssignedWorkerIds = assignedWorkers.Values
                .Where(worker => worker.Contribution.IsProductionReady)
                .Select(worker => worker.WorkerId)
                .ToHashSet();
            var requestedWorkerIdSet = requestedWorkerIds.ToHashSet();
            var removedAssignedWorkers = readyAssignedWorkerIds.Except(requestedWorkerIdSet).ToArray();
            var addedDailyOverrides = stageRequest.Workers
                .Where(worker => !assignedWorkers.ContainsKey(worker.WorkerId))
                .ToArray();
            var requestedNonReadyAssignedWorkers = stageRequest.Workers
                .Where(worker => assignedWorkers.TryGetValue(worker.WorkerId, out var assigned) && !assigned.Contribution.IsProductionReady)
                .ToArray();
            if (requestedNonReadyAssignedWorkers.Length > 0)
                throw new ProductionConflictException("العامل المسكّن غير الجاهز يبقى ظاهرًا في لقطة اليوم، لكنه لا يُحتسب ضمن المشاركين أو دقائق العمل.");
            if (addedDailyOverrides.Any(worker => string.IsNullOrWhiteSpace(worker.ManualOverrideReason)))
                throw new ProductionConflictException("إضافة عامل يومية خارج تسكين المرحلة تتطلب سببًا واضحًا.");
            if (addedDailyOverrides.Any(worker => !activeWorkers[worker.WorkerId].IsProductionReady))
                throw new ProductionConflictException("لا يمكن احتساب إضافة يومية بلا نافذة حضور مكتملة وصالحة.");
            if (removedAssignedWorkers.Length > 0 || addedDailyOverrides.Length > 0)
            {
                var permissions = await permissionService.GetEffectivePermissionsAsync(actorId, ct);
                if (!permissions.Contains(ManualParticipantOverridePermission, StringComparer.OrdinalIgnoreCase))
                    throw new ProductionConflictException("لا تملك صلاحية تعديل المشاركين في تشغيل اليوم.");
            }

            var participantContributions = stageRequest.Workers.ToDictionary(
                worker => worker.WorkerId,
                worker => assignedWorkers.TryGetValue(worker.WorkerId, out var assigned)
                    ? assigned.Contribution
                    : ContributionFromDto(activeWorkers[worker.WorkerId]));

            var isSharedPercentage = stage.Entity.CompensationMode == CompensationMode.SharedPercentage;
            var suppliedQuantityCount = stageRequest.Workers.Count(worker => worker.InputQuantity.HasValue);
            if (isSharedPercentage && suppliedQuantityCount > 0 && suppliedQuantityCount != stageRequest.Workers.Count)
                throw new ProductionConflictException("يجب إرسال كمية كل عامل في المرحلة أو ترك جميع الكميات للاحتساب التلقائي.");
            var useSubmittedQuantities = isSharedPercentage && suppliedQuantityCount == stageRequest.Workers.Count;

            IReadOnlyDictionary<Guid, WorkerQuantityShare> defaultShares = isSharedPercentage && !useSubmittedQuantities
                ? TimeAwareProductionAllocation.AllocateEqually(
                    RoundQuantity(request.LineQuantity),
                    participantContributions.Values).ToDictionary(share => share.WorkerId)
                : new Dictionary<Guid, WorkerQuantityShare>();
            var authoritativeWorkers = stageRequest.Workers.Select(worker => isSharedPercentage
                ? useSubmittedQuantities
                    ? worker with { FixedAmount = null, InputQuantity = RoundQuantity(worker.InputQuantity!.Value) }
                    : worker with { Percentage = defaultShares[worker.WorkerId].Percentage, FixedAmount = null }
                : worker).ToArray();
            ValidateAllocationInputs(stage.Entity.CompensationMode, authoritativeWorkers);

            if (useSubmittedQuantities)
            {
                if (authoritativeWorkers.Any(worker => worker.InputQuantity <= 0) ||
                    RoundQuantity(authoritativeWorkers.Sum(worker => worker.InputQuantity!.Value)) != RoundQuantity(request.LineQuantity))
                {
                    throw new ProductionConflictException("يجب أن تكون كمية كل عامل أكبر من صفر وأن يساوي مجموع كميات العمال كمية المرحلة.");
                }

                if (authoritativeWorkers.Any(worker =>
                        Math.Abs(RoundQuantity(request.LineQuantity * worker.Percentage!.Value / 100m) - worker.InputQuantity!.Value) > 0.001m))
                {
                    throw new ProductionConflictException("كميات العمال لا تتطابق مع نسب التوزيع المرسلة للمرحلة.");
                }
            }

            var calculatedAmounts = isSharedPercentage
                ? useSubmittedQuantities
                    ? authoritativeWorkers.ToDictionary(
                        worker => worker.WorkerId,
                        worker => new CalculatedAllocation(
                            worker.WorkerId,
                            worker.InputQuantity!.Value,
                            RoundMoney(worker.InputQuantity.Value * stage.Entity.PiecePrice)))
                    : defaultShares.ToDictionary(
                        pair => pair.Key,
                        pair => new CalculatedAllocation(pair.Key, pair.Value.Quantity, RoundMoney(pair.Value.Quantity * stage.Entity.PiecePrice)))
                : CalculateAllocationAmounts(
                    stage.Entity.CompensationMode,
                    RoundQuantity(request.LineQuantity),
                    stage.Entity.PiecePrice,
                    authoritativeWorkers).ToDictionary(amount => amount.WorkerId);
            var allocations = authoritativeWorkers.Select(worker =>
            {
                var snapshot = activeWorkers[worker.WorkerId];
                var calculated = calculatedAmounts[worker.WorkerId];
                var allocation = new StageProductionWorkerAllocation(
                    Guid.NewGuid(),
                    worker.WorkerId,
                    snapshot.WorkerCode,
                    snapshot.WorkerName,
                    worker.Percentage,
                    worker.FixedAmount,
                    worker.Notes,
                    worker.ManualOverrideReason,
                    worker.InputQuantity);
                allocation.SetCalculatedAmounts(calculated.EquivalentQuantity, calculated.CalculatedEarning);
                return allocation;
            }).ToArray();
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

    private async Task<DailyPreview> BuildDailyDraftPlanAsync(
        DailyProductionOperationRequest request,
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

        var requestsByStage = request.Stages.ToDictionary(stage => stage.ProductModelStageId);
        var stagePlans = new List<DailyStagePreview>(context.Stages.Count);
        foreach (var stage in context.Stages)
        {
            var stageRequest = requestsByStage[stage.Entity.Id];
            var allocations = await BuildPermissiveDailyDraftAllocationsAsync(
                stage.Entity.CompensationMode,
                stage.Entity.PiecePrice,
                request.LineQuantity,
                stageRequest.Workers,
                persistedAllocations: null,
                ct);
            var warnings = StageWarnings(stage.Dto)
                .Concat(DailyDraftAllocationWarnings(stage.Entity.CompensationMode, stageRequest.Workers))
                .Distinct()
                .ToArray();
            stagePlans.Add(new DailyStagePreview(
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
            stagePlans);
    }

    private async Task<DailyPreview> BuildDailyDraftSavePlanAsync(
        DailyProductionOperationRequest request,
        Guid actorId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.PreviewToken))
        {
            try
            {
                var preview = await BuildDailyPreviewAsync(request, actorId, ct);
                if (string.Equals(request.PreviewToken, preview.PreviewToken, StringComparison.Ordinal))
                    return preview;
            }
            catch (ProductionConflictException)
            {
                // Operational preview validation remains strict. Draft persistence
                // deliberately falls through to its permissive plan instead.
            }
        }

        return await BuildDailyDraftPlanAsync(request, ct);
    }

    private async Task<IReadOnlyCollection<StageProductionWorkerAllocation>> BuildPermissiveDailyDraftAllocationsAsync(
        CompensationMode compensationMode,
        decimal piecePrice,
        decimal lineQuantity,
        IReadOnlyCollection<WorkerAllocationRequest> workers,
        IReadOnlyCollection<StageProductionWorkerAllocation>? persistedAllocations,
        CancellationToken ct)
    {
        if (workers is null)
            throw new ProductionConflictException("قائمة عمال المرحلة مطلوبة ولو كانت فارغة في المسودة.");
        if (workers.Any(worker => worker.WorkerId == Guid.Empty) ||
            workers.Select(worker => worker.WorkerId).Distinct().Count() != workers.Count)
        {
            throw new ProductionConflictException("توجد هوية عامل مكررة أو غير صالحة في مرحلة المسودة.");
        }
        if (workers.Any(worker => worker.Percentage < 0 || worker.FixedAmount < 0 || worker.InputQuantity < 0))
            throw new ProductionConflictException("لا يمكن حفظ قيم توزيع سالبة في مسودة تشغيل اليوم.");

        var workerIds = workers.Select(worker => worker.WorkerId).ToArray();
        var currentWorkers = await db.Set<Worker>()
            .AsNoTracking()
            .Where(worker => workerIds.Contains(worker.Id))
            .Select(worker => new { worker.Id, worker.EmployeeCode, worker.FullName, worker.IsActive, worker.EmploymentStatus })
            .ToDictionaryAsync(worker => worker.Id, ct);
        var persistedByWorker = (persistedAllocations ?? [])
            .ToDictionary(allocation => allocation.WorkerId);
        var sharedQuantities = new Dictionary<Guid, decimal>();
        if (compensationMode == CompensationMode.SharedPercentage)
        {
            var hasCompleteInputQuantities = workers.Count > 0 && workers.All(worker => worker.InputQuantity.HasValue);
            var hasCompletePercentages = workers.Count > 0 &&
                                         workers.All(worker => !worker.InputQuantity.HasValue && worker.Percentage > 0m) &&
                                         workers.Sum(worker => worker.Percentage ?? 0m) == 100m;
            if (hasCompletePercentages)
            {
                sharedQuantities = TimeAwareProductionAllocation.AllocateQuantitiesByPercentage(
                        RoundQuantity(lineQuantity),
                        workers.Select(worker => (worker.WorkerId, worker.Percentage!.Value)))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }
            else
            {
                foreach (var worker in workers)
                {
                    sharedQuantities[worker.WorkerId] = hasCompleteInputQuantities
                        ? RoundQuantity(worker.InputQuantity!.Value)
                        : worker.InputQuantity.HasValue
                            ? RoundQuantity(worker.InputQuantity.Value)
                            : worker.Percentage.HasValue
                                ? RoundQuantity(lineQuantity * worker.Percentage.Value / 100m)
                                : 0m;
                }
            }
        }

        var allocations = new List<StageProductionWorkerAllocation>(workers.Count);
        foreach (var worker in workers)
        {
            string workerCode;
            string workerName;
            if (currentWorkers.TryGetValue(worker.WorkerId, out var current) &&
                current.IsActive && current.EmploymentStatus == EmploymentStatus.Active)
            {
                workerCode = current.EmployeeCode;
                workerName = current.FullName;
            }
            else if (persistedByWorker.TryGetValue(worker.WorkerId, out var persisted))
            {
                workerCode = persisted.SnapshotWorkerCode;
                workerName = persisted.SnapshotWorkerName;
            }
            else
            {
                throw new ProductionConflictException("هوية أحد العمال غير صالحة لهذه المسودة ولا يمكن حفظها بأمان.");
            }

            var equivalentQuantity = compensationMode == CompensationMode.SharedPercentage
                ? sharedQuantities[worker.WorkerId]
                : 0m;
            var calculatedEarning = compensationMode switch
            {
                CompensationMode.SharedPercentage => RoundMoney(equivalentQuantity * piecePrice),
                CompensationMode.FixedAmount => RoundMoney(worker.FixedAmount ?? 0m),
                _ => RoundMoney(lineQuantity * piecePrice)
            };
            var allocation = new StageProductionWorkerAllocation(
                Guid.NewGuid(),
                worker.WorkerId,
                workerCode,
                workerName,
                worker.Percentage,
                worker.FixedAmount,
                worker.Notes,
                worker.ManualOverrideReason,
                worker.InputQuantity);
            allocation.SetCalculatedAmounts(equivalentQuantity, calculatedEarning);
            allocations.Add(allocation);
        }

        return allocations;
    }

    private static IReadOnlyCollection<string> DailyDraftAllocationWarnings(
        CompensationMode compensationMode,
        IReadOnlyCollection<WorkerAllocationRequest> workers)
    {
        var warnings = new List<string>();
        if (workers.Count == 0)
            warnings.Add("لا يوجد عامل جاهز محتسب في تشغيل هذه المرحلة. يمكن حفظ المسودة، لكن يلزم استكمالها قبل الاعتماد.");
        if (compensationMode == CompensationMode.SharedPercentage &&
            (workers.Any(worker => !worker.Percentage.HasValue || worker.Percentage <= 0) || workers.Sum(worker => worker.Percentage ?? 0m) != 100m))
        {
            warnings.Add("توزيع نسب العمال غير مكتمل. يمكن حفظ المسودة، لكن يلزم ضبط المجموع إلى 100٪ قبل الاعتماد.");
        }
        if (compensationMode == CompensationMode.FixedAmount && workers.Any(worker => !worker.FixedAmount.HasValue))
            warnings.Add("توجد قيمة ثابتة غير مكتملة. يمكن حفظ المسودة، لكن يلزم استكمالها قبل الاعتماد.");
        return warnings;
    }

    private async Task<DailyContext> LoadDailyContextAsync(
        Guid factoryId,
        Guid productionLineId,
        Guid productModelId,
        DateOnly productionDate,
        CancellationToken ct,
        string? missingStagesMessage = null)
    {
        if (factoryId == Guid.Empty || productionLineId == Guid.Empty || productModelId == Guid.Empty)
            throw new ProductionConflictException("اختر المصنع وخط الإنتاج والموديل قبل تحميل تشغيل اليوم.");

        var factory = await db.Set<Factory>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == factoryId && candidate.IsActive, ct)
            ?? throw new ProductionConflictException("المصنع المحدد غير نشط أو غير متاح.");
        var line = await db.Set<ProductionLine>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == productionLineId && candidate.FactoryId == factoryId && candidate.IsActive, ct)
            ?? throw new ProductionConflictException("خط الإنتاج المحدد غير نشط أو لا يتبع المصنع.");
        if (line.DepartmentId is null)
            throw new ProductionConflictException("يجب ربط خط الإنتاج بقسم قبل تحميل تشغيل اليوم.");
        var product = await db.Set<ProductModel>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == productModelId && candidate.IsActive, ct)
            ?? throw new ProductionConflictException("الموديل المحدد غير نشط أو غير متاح.");

        var stages = await CurrentActiveDailyStagesQuery(productModelId, productionLineId, line.DepartmentId.Value)
            .AsNoTracking()
            .Include(stage => stage.SubStage)
                .ThenInclude(subStage => subStage!.MainStage)
            .OrderBy(stage => stage.StageOrder)
            .ToArrayAsync(ct);
        var existingOrder = await db.Set<ProductionOrder>()
            .AsNoTracking()
            .Include(order => order.ProductModel)
            .Include(order => order.StageProductionRecords)
                .ThenInclude(record => record.WorkerAllocations)
                    .ThenInclude(allocation => allocation.Worker)
            .Where(order => order.ProductionDate == productionDate
                && order.ProductionLineId == productionLineId
                && order.ProductModelId == productModelId
                && order.StageProductionRecords.Any())
            .OrderByDescending(order => order.RecordedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (stages.Length == 0 && existingOrder is null)
            throw new ProductionConflictException(missingStagesMessage ?? "لا توجد مراحل موديل نشطة مرتبطة بقسم خط الإنتاج المحدد.");

        var workers = await db.Set<Worker>().AsNoTracking()
            .Where(worker => worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active)
            .OrderBy(worker => worker.EmployeeCode)
            .Select(worker => new ActiveWorker(worker.Id, worker.EmployeeCode, worker.FullName))
            .ToArrayAsync(ct);
        var workerIds = workers.Select(worker => worker.Id).ToArray();
        var assignmentWindows = await LoadAssignmentWindowsAsync(productionLineId, workerIds, productionDate, ct);
        var attendance = await attendanceEngine.GetPresenceWindowsByWorkerAsync(workerIds, productionDate, ct);
        var attendanceUnavailable = attendance.IsFailure;
        var attendanceByWorker = attendance.IsSuccess
            ? attendance.Value!
            : new Dictionary<Guid, AttendancePresenceWindowDto>();

        var workersById = workers.ToDictionary(worker => worker.Id);
        var staffingCandidates = assignmentWindows.SelectMany(pair => pair.Value.Select(window =>
        {
            var worker = workersById[pair.Key.WorkerId];
            return new DailyStaffingCandidate(
                worker.Id,
                worker.Code,
                worker.Name,
                pair.Key.SubStageId,
                window.AssignmentType,
                window.AssignmentId,
                [new UtcTimeWindow(window.StartUtc, window.EndUtc)]);
        })).ToArray();
        var staffingSnapshot = DailyStageStaffingBuilder.Build(staffingCandidates, attendanceByWorker);

        var stageContexts = stages.Select(stage =>
        {
            var stageWorkers = staffingSnapshot
                .Where(worker => worker.SubStageId == stage.SubStageId)
                .Select(worker => new DailyWorkerContext(
                    worker.WorkerId,
                    worker.WorkerCode,
                    worker.WorkerName,
                    stage.SubStageId,
                    worker.AssignmentType,
                    attendanceUnavailable ? "AttendanceUnavailable" : worker.Attendance is null ? "NoSourceCheckIn" : AttendanceLabel(worker.Attendance.Status),
                    worker.Attendance?.HasSourceCheckIn == true,
                    worker.Attendance?.Status is AttendanceStatus.Present or AttendanceStatus.Late,
                    worker.AssignmentId,
                    worker.Attendance?.FirstInUtc,
                    worker.Contribution,
                    IsAssignedWorker: true,
                    IsDailyOverride: false))
                .OrderBy(worker => worker.WorkerCode, StringComparer.Ordinal)
                .ThenBy(worker => worker.WorkerId)
                .ToArray();
            var suggestedPercentages = SuggestedSharedPercentages(stage.CompensationMode, stageWorkers);
            var workerDtos = stageWorkers.Select(worker => worker.ToDto(suggestedPercentages.GetValueOrDefault(worker.WorkerId))).ToArray();
            var hasAbsent = workerDtos.Any(worker => worker.AttendanceStatus == "Absent");
            var hasNoCheckIn = !attendanceUnavailable && workerDtos.Any(worker => worker.AttendanceStatus == "NoSourceCheckIn");
            var staffed = workerDtos.Length > 0;
            var ready = staffed && workerDtos.Any(worker => worker.IsProductionReady);
            var staffingStatus = staffed ? "Staffed" : "NoStaffing";
            var attendanceStatus = !staffed
                ? "NoStaffing"
                : attendanceUnavailable ? "AttendanceUnavailable"
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
                    workerDtos),
                stageWorkers);
        }).ToArray();

        var (dayStartUtc, dayEndUtc) = ProductionDayBoundsUtc(productionDate);
        var allWorkers = workers
            .OrderBy(worker => worker.Code, StringComparer.Ordinal)
            .ThenBy(worker => worker.Id)
            .Select(worker =>
            {
                attendanceByWorker.TryGetValue(worker.Id, out var presence);
                var contribution = TimeAwareProductionAllocation.CalculateContribution(
                    worker.Id,
                    [new UtcTimeWindow(dayStartUtc, dayEndUtc)],
                    presence);
                return new DailyProductionWorkerDto(
                    worker.Id, worker.Code, worker.Name, true, null,
                    attendanceUnavailable ? "AttendanceUnavailable" : presence is null ? "NoSourceCheckIn" : AttendanceLabel(presence.Status),
                    presence?.HasSourceCheckIn == true,
                    presence?.Status is AttendanceStatus.Present or AttendanceStatus.Late,
                    false, null,
                    contribution.ContributionStartsAtUtc,
                    contribution.ContributionEndsAtUtc,
                    contribution.WorkerMinutes,
                    contribution.IsProductionReady,
                    contribution.ExclusionReason,
                    IsAssignedWorker: false,
                    IsDailyOverride: false);
            })
            .ToArray();
        var existingDraft = existingOrder is null ? null : ToDailyDraftDto(existingOrder, wasAlreadySaved: true);
        var version = StaffingContextVersion(factory, line, product, productionDate, stageContexts, stageContexts.SelectMany(stage => stage.Workers).ToArray());
        return new DailyContext(factory, line, product, productionDate, version, stageContexts, allWorkers, existingDraft);
    }

    private IQueryable<ProductModelStage> CurrentActiveDailyStagesQuery(
        Guid productModelId,
        Guid productionLineId,
        Guid departmentId) =>
        db.Set<ProductModelStage>()
            .Where(stage => stage.ProductModelId == productModelId
                            && stage.ProductionLineId == productionLineId
                            && stage.IsActive
                            && stage.SubStage != null
                            && stage.SubStage.IsActive
                            && stage.SubStage.MainStage != null
                            && stage.SubStage.MainStage.IsActive
                            && stage.SubStage.DepartmentId == departmentId);

    private async Task<Dictionary<(Guid WorkerId, Guid SubStageId), List<AssignmentWindow>>> LoadAssignmentWindowsAsync(
        Guid productionLineId,
        IReadOnlyCollection<Guid> workerIds,
        DateOnly productionDate,
        CancellationToken ct)
    {
        var (dayStartUtc, dayEndUtc) = ProductionDayBoundsUtc(productionDate);
        var defaults = await db.Set<WorkerDefaultAssignment>().AsNoTracking()
            .Where(assignment => workerIds.Contains(assignment.WorkerId) && assignment.ProductionLineId == productionLineId && assignment.IsActive)
            .Select(assignment => new
            {
                assignment.Id,
                assignment.WorkerId,
                assignment.SubStageId
            })
            .ToArrayAsync(ct);
        var temporary = await db.Set<WorkerTemporaryAssignment>().AsNoTracking()
            .Where(assignment => workerIds.Contains(assignment.WorkerId)
                                 && (assignment.Status == "Scheduled" || assignment.Status == "Active")
                                 && assignment.StartAtUtc < dayEndUtc
                                 && assignment.EndAtUtc > dayStartUtc)
            .Select(assignment => new
            {
                assignment.Id,
                assignment.WorkerId,
                assignment.FromSubStageId,
                assignment.ToSubStageId,
                assignment.StartAtUtc,
                assignment.EndAtUtc,
                assignment.ReplacementForWorkerId,
                assignment.ParticipationMode
            })
            .ToArrayAsync(ct);

        var result = new Dictionary<(Guid WorkerId, Guid SubStageId), List<AssignmentWindow>>();
        foreach (var assignment in defaults)
        {
            AddWindow(result, assignment.WorkerId, assignment.SubStageId, new AssignmentWindow(assignment.Id, "Default", dayStartUtc, dayEndUtc));
        }

        foreach (var assignment in temporary.OrderBy(item => item.StartAtUtc).ThenBy(item => item.Id))
        {
            var start = assignment.StartAtUtc > dayStartUtc ? assignment.StartAtUtc : dayStartUtc;
            var end = assignment.EndAtUtc < dayEndUtc ? assignment.EndAtUtc : dayEndUtc;
            if (start >= end) continue;
            if (assignment.ParticipationMode == TemporaryAssignmentMode.TemporaryMove && assignment.FromSubStageId.HasValue)
                SubtractWindow(result, assignment.WorkerId, assignment.FromSubStageId.Value, start, end);
            AddWindow(result, assignment.WorkerId, assignment.ToSubStageId, new AssignmentWindow(
                assignment.Id,
                assignment.ReplacementForWorkerId.HasValue ? "Replacement" : "Temporary",
                start,
                end));
        }
        return result;
    }

    private static void AddWindow(
        IDictionary<(Guid WorkerId, Guid SubStageId), List<AssignmentWindow>> windows,
        Guid workerId,
        Guid subStageId,
        AssignmentWindow window)
    {
        var key = (workerId, subStageId);
        if (!windows.TryGetValue(key, out var list)) windows[key] = list = [];
        list.Add(window);
    }

    private static void SubtractWindow(
        IDictionary<(Guid WorkerId, Guid SubStageId), List<AssignmentWindow>> windows,
        Guid workerId,
        Guid subStageId,
        DateTime subtractStart,
        DateTime subtractEnd)
    {
        var key = (workerId, subStageId);
        if (!windows.TryGetValue(key, out var list)) return;
        var replacements = new List<AssignmentWindow>();
        foreach (var window in list)
        {
            if (subtractEnd <= window.StartUtc || subtractStart >= window.EndUtc)
            {
                replacements.Add(window);
                continue;
            }
            if (window.StartUtc < subtractStart)
                replacements.Add(window with { EndUtc = subtractStart });
            if (subtractEnd < window.EndUtc)
                replacements.Add(window with { StartUtc = subtractEnd });
        }
        windows[key] = replacements;
    }

    private static void ValidateDailyStages(
        DailyProductionOperationRequest request,
        DailyContext context,
        string? stageConfigurationConflictMessage = null)
    {
        var requestedStages = request.Stages ?? [];
        var requestedIds = requestedStages.Select(stage => stage.ProductModelStageId).ToArray();
        if (requestedIds.Any(stageId => stageId == Guid.Empty) ||
            requestedIds.Distinct().Count() != requestedIds.Length)
            throw new ProductionConflictException("توجد مرحلة مكررة أو غير صالحة في تشغيل اليوم.");

        var expectedIds = context.Stages.Select(stage => stage.Entity.Id).ToHashSet();
        var requestedIdSet = requestedIds.ToHashSet();
        var missingIds = expectedIds.Except(requestedIdSet).Order().ToArray();
        var unexpectedIds = requestedIdSet.Except(expectedIds).Order().ToArray();
        if (requestedIds.Length != expectedIds.Count || missingIds.Length > 0 || unexpectedIds.Length > 0)
        {
            var message = stageConfigurationConflictMessage ?? "يجب أن تتضمن معاينة تشغيل اليوم كل مراحل الموديل المحمّلة.";
            throw new ProductionConflictException(
                $"{message} عدد المراحل الحالية: {expectedIds.Count}؛ عدد المراحل المرسلة: {requestedIds.Length}؛ " +
                $"المعرّفات الناقصة: {DailyStageIdsForDiagnostic(missingIds)}؛ " +
                $"المعرّفات غير المتوقعة: {DailyStageIdsForDiagnostic(unexpectedIds)}.");
        }
    }

    private static string DailyStageIdsForDiagnostic(IEnumerable<Guid> stageIds)
    {
        var values = stageIds.Select(stageId => stageId.ToString("D", CultureInfo.InvariantCulture)).ToArray();
        return values.Length == 0 ? "لا يوجد" : string.Join(", ", values);
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
        order.Status.ToString(),
        order.ConcurrencyToken,
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
        if (stage.AttendanceStatus == "AttendanceUnavailable") warnings.Add("تعذر قراءة مصدر الحضور لهذه المرحلة. يمكن حفظ المسودة، لكن يجب مراجعة الحضور قبل الاعتماد.");
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

    private static WorkerContributionResult ContributionFromDto(DailyProductionWorkerDto worker) => new(
        worker.WorkerId,
        worker.ContributionStartsAtUtc,
        worker.ContributionEndsAtUtc,
        worker.WorkerMinutes,
        worker.ExclusionReason);

    private static IReadOnlyDictionary<Guid, decimal> SuggestedSharedPercentages(
        CompensationMode mode,
        IReadOnlyCollection<DailyWorkerContext> workers)
    {
        if (mode != CompensationMode.SharedPercentage || workers.Count == 0)
            return new Dictionary<Guid, decimal>();
        return TimeAwareProductionAllocation.AllocateEqually(100m, workers.Select(worker => worker.Contribution))
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
        {
            builder.Append('|').Append(stage.Entity.Id).Append('|').Append(stage.Entity.PiecePrice.ToString(CultureInfo.InvariantCulture)).Append('|').Append(stage.Entity.CompensationMode);
            foreach (var worker in stage.Dto.Workers.OrderBy(worker => worker.WorkerId))
                builder.Append('|').Append(stage.Entity.Id).Append('|').Append(worker.WorkerId).Append('|').Append(worker.EffectiveAssignmentType).Append('|').Append(worker.AttendanceStatus);
        }
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
    private static bool IsDailyOperation(ProductionOrder order) => !order.SourceImportBatchId.HasValue && !string.IsNullOrWhiteSpace(order.SourceReference);

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
        DateTime? AttendanceAtUtc,
        WorkerContributionResult Contribution,
        bool IsAssignedWorker,
        bool IsDailyOverride)
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
            RequiresAuthorizedOverride: false,
            suggestedPercentage,
            Contribution.ContributionStartsAtUtc,
            Contribution.ContributionEndsAtUtc,
            Contribution.WorkerMinutes,
            Contribution.IsProductionReady,
            Contribution.ExclusionReason,
            IsAssignedWorker,
            IsDailyOverride);
    }
    private sealed record AssignmentWindow(Guid AssignmentId, string AssignmentType, DateTime StartUtc, DateTime EndUtc);
    private sealed record DailyStageContext(ProductModelStage Entity, DailyProductionStageDto Dto, IReadOnlyCollection<DailyWorkerContext> Workers);
    private sealed record DailyContext(
        Factory Factory,
        ProductionLine Line,
        ProductModel Product,
        DateOnly ProductionDate,
        string ContextVersion,
        IReadOnlyCollection<DailyStageContext> Stages,
        IReadOnlyCollection<DailyProductionWorkerDto> ActiveWorkers,
        DailyProductionDraftDto? ExistingDraft)
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
            ActiveWorkers,
            ExistingDraft);
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
            Stages.SelectMany(stage => stage.Allocations)
                .GroupBy(allocation => new { allocation.WorkerId, allocation.SnapshotWorkerCode, allocation.SnapshotWorkerName })
                .OrderBy(group => group.Key.SnapshotWorkerCode, StringComparer.Ordinal)
                .ThenBy(group => group.Key.WorkerId)
                .Select(group => new DailyProductionWorkerTotalDto(
                    group.Key.WorkerId,
                    group.Key.SnapshotWorkerCode,
                    group.Key.SnapshotWorkerName,
                    RoundMoney(group.Sum(allocation => allocation.CalculatedEarning))))
                .ToArray(),
            Stages.SelectMany(stage => stage.Warnings).Distinct().ToArray());
    }

    private async Task<List<StageProductionWorkerAllocation>> BuildAllocationsAsync(
        ProductModelStage stage,
        Guid productionLineId,
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

        var assignments = await assignmentEngine.ResolveEffectiveAssignmentsAsync(ids, eventAtUtc, ct);
        if (assignments.IsFailure) throw new ProductionConflictException("تعذر التحقق من تعيينات العمال الفعلية.");
        var attendance = await attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(ids, eventAtUtc, ct);
        if (attendance.IsFailure) throw new ProductionConflictException("تعذر التحقق من حضور العمال من مصدر الحضور.");

        var workersRequiringOverride = workers.Where(worker =>
        {
            var assignedToStage = assignments.Value!.TryGetValue(worker.WorkerId, out var workerAssignments) && workerAssignments.Any(assignment => assignment.EffectiveSubStageId == stage.SubStageId && assignment.ProductionLineId == productionLineId);
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
            .ThenInclude(x => x!.Department)
            .SingleOrDefaultAsync(x => x.Id == stageId && x.ProductModelId == order.ProductModelId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Active model stage was not found for this production order.");

        var line = order.ProductionLine;
        if (stage.SubStage is null || !stage.SubStage.IsActive || stage.SubStage.MainStage is null || !stage.SubStage.MainStage.IsActive || line is null || !line.IsActive || line.Factory is null || !line.Factory.IsActive)
            throw new ProductionConflictException("The selected production stage is no longer active in the factory structure.");
        if (!order.ProductionLineId.HasValue)
            throw new ProductionConflictException("لا يمكن تسجيل دفعة لأمر إنتاج لا يحتوي على خط إنتاج صالح.");
        if (stage.ProductionLineId != order.ProductionLineId.Value)
            throw new ProductionConflictException("The selected production stage is not assigned to this order's production line.");
        if (line.DepartmentId is null || stage.SubStage.DepartmentId != line.DepartmentId.Value)
            throw new ProductionConflictException("المرحلة المحددة لا تتبع قسم خط أمر الإنتاج.");

        return (order, stage);
    }

    private static StageProductionRecord CreateSnapshotRecord(ProductionOrder order, ProductModelStage stage, DateOnly productionDate, decimal producedQuantity, decimal acceptedQuantity, decimal rejectedQuantity, Guid clientRequestId, string? notes, Guid actorId, DateTime atUtc)
    {
        var subStage = stage.SubStage!;
        var mainStage = subStage.MainStage!;
        var line = order.ProductionLine!;
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
    private static void EnsureDailyApprovalBusinessConsistency(StageProductionRecord record)
    {
        if (record.ProducedQuantity <= 0 || record.AcceptedQuantity != record.ProducedQuantity || record.RejectedQuantity != 0)
            throw new ProductionConflictException("لا يمكن اعتماد مرحلة بكمية تشغيل غير مكتملة أو غير متسقة.");
        if (record.WorkerAllocations.Count == 0)
            throw new ProductionConflictException("لا يمكن اعتماد مرحلة بلا عامل مشارك محفوظ. استكمل المسودة أولًا.");

        var workers = record.WorkerAllocations;
        switch (record.SnapshotCompensationMode)
        {
            case CompensationMode.SharedPercentage:
                if (workers.Any(worker => !worker.Percentage.HasValue || worker.Percentage <= 0 || worker.FixedAmount.HasValue) ||
                    workers.Sum(worker => worker.Percentage ?? 0m) != 100m)
                {
                    throw new ProductionConflictException("لا يمكن اعتماد مرحلة قبل اكتمال توزيع نسب العمال إلى 100٪.");
                }

                var hasInputQuantities = workers.Any(worker => worker.InputQuantity.HasValue);
                if (hasInputQuantities &&
                    (workers.Any(worker => !worker.InputQuantity.HasValue || worker.InputQuantity <= 0) ||
                     RoundQuantity(workers.Sum(worker => worker.InputQuantity ?? 0m)) != RoundQuantity(record.ProducedQuantity)))
                {
                    throw new ProductionConflictException("لا يمكن اعتماد مرحلة قبل اكتمال كميات العمال ومطابقتها لكمية المرحلة.");
                }

                if (RoundQuantity(workers.Sum(worker => worker.EquivalentQuantity)) != RoundQuantity(record.ProducedQuantity))
                    throw new ProductionConflictException("لا يمكن اعتماد المرحلة قبل موازنة كميات العمال مع كمية المرحلة.");

                foreach (var worker in workers)
                {
                    var expectedQuantity = hasInputQuantities
                        ? RoundQuantity(worker.InputQuantity!.Value)
                        : RoundQuantity(record.ProducedQuantity * worker.Percentage!.Value / 100m);
                    if (Math.Abs(worker.EquivalentQuantity - expectedQuantity) > 0.001m ||
                        worker.CalculatedEarning != RoundMoney(worker.EquivalentQuantity * record.SnapshotPiecePrice))
                    {
                        throw new ProductionConflictException("لا يمكن اعتماد المرحلة لأن توزيع الكميات أو المستحقات يحتاج إعادة حساب.");
                    }
                }
                break;

            case CompensationMode.FixedAmount:
                if (workers.Any(worker => worker.Percentage.HasValue || !worker.FixedAmount.HasValue || worker.FixedAmount < 0 ||
                                          worker.EquivalentQuantity != 0m || worker.CalculatedEarning != RoundMoney(worker.FixedAmount ?? 0m)))
                {
                    throw new ProductionConflictException("لا يمكن اعتماد المرحلة قبل استكمال القيم الثابتة الصحيحة لكل عامل.");
                }
                break;

            default:
                if (workers.Any(worker => worker.Percentage.HasValue || worker.FixedAmount.HasValue ||
                                          worker.EquivalentQuantity != 0m ||
                                          worker.CalculatedEarning != RoundMoney(record.ProducedQuantity * record.SnapshotPiecePrice)))
                {
                    throw new ProductionConflictException("لا يمكن اعتماد المرحلة لأن مستحقات المعدل الكامل تحتاج إعادة حساب.");
                }
                break;
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
    private async Task<ProductionOrder> OrderAsync(Guid id, CancellationToken ct, bool includeRecords = false) { var query = db.Set<ProductionOrder>().Include(x => x.ProductModel).Include(x => x.ProductionLine).ThenInclude(x => x!.Factory).AsQueryable(); if (includeRecords) query = query.Include(x => x.StageProductionRecords); return await query.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Production order was not found."); }
    private async Task<ProductionOrderDto> OrderDtoAsync(Guid id, CancellationToken ct) { var x = await OrderAsync(id, ct); return new(x.Id, x.OrderNumber, x.ProductModelId, x.ProductModel!.Code, x.ProductionLineId, x.ProductionDate, x.PlannedQuantity, x.Status.ToString(), x.Notes, x.SourceImportBatchId.HasValue, x.RecordedAtUtc, x.ApprovedAtUtc); }
    private async Task<StageProductionRecord> RecordAsync(Guid id, CancellationToken ct) => await db.Set<StageProductionRecord>().Include(x => x.ProductionOrder).ThenInclude(x => x!.ProductModel).Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Production record was not found.");
    private async Task<StageProductionRecordDto> GetRecordByClientRequestAsync(Guid orderId, Guid clientRequestId, CancellationToken ct) => ToRecordDto(await db.Set<StageProductionRecord>().Include(x => x.ProductionOrder).ThenInclude(x => x!.ProductModel).Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).SingleAsync(x => x.ProductionOrderId == orderId && x.ClientRequestId == clientRequestId, ct));
    private static StageProductionRecordDto ToRecordDto(StageProductionRecord x) => new(x.Id, x.ProductionOrderId, x.ProductModelStageId, x.ProductionDate, x.ProducedQuantity, x.AcceptedQuantity, x.RejectedQuantity, x.Status.ToString(), x.SnapshotStageCode, x.SnapshotStageName, x.SnapshotProductModelCode, x.SnapshotProductModelName, x.SnapshotFactoryCode, x.SnapshotFactoryName, x.SnapshotProductionLineCode, x.SnapshotProductionLineName, x.SnapshotMainStageName, x.SnapshotPiecePrice, x.SnapshotStandardSeconds, x.SnapshotCompensationMode.ToString(), x.TotalWorkerEarnings, x.ConcurrencyToken, x.WorkerAllocations.Select(ToAllocationDto).ToList(), x.Notes, x.ApprovedBy, x.ApprovedAtUtc, x.CancelledBy, x.CancelledAtUtc, x.ApprovalCancellationReason);
    private static ProductionWorkerAllocationDto ToAllocationDto(StageProductionWorkerAllocation x) => new(x.WorkerId, x.SnapshotWorkerCode, x.SnapshotWorkerName, x.Percentage, x.FixedAmount, x.InputQuantity, x.EquivalentQuantity, x.CalculatedEarning, x.Notes, x.ManualOverrideReason);
    private async Task AuditAsync(Guid actorId, AuditActionType action, string entityType, Guid entityId, object? before, object? after, CancellationToken ct) { var result = await audit.RecordAsync(actorId, action, entityType, entityId.ToString(), before, after, "ProductionCostRecording", ct); if (result.IsFailure) throw new InvalidOperationException(result.Error?.Message ?? "Production audit failed."); }
    private static object OrderAudit(ProductionOrder x) => new { x.Id, x.OrderNumber, x.Status, x.ProductionDate, x.PlannedQuantity, x.ConcurrencyToken, Result = "Success" };
    private static object DailyDraftAudit(ProductionOrder x) => new { x.Id, x.Status, x.ProductionDate, x.ProductionLineId, x.ProductModelId, x.PlannedQuantity, x.SourceReference, x.RecordedAtUtc, x.ConcurrencyToken, Result = "Success" };
    private static object DailyApprovalOrderAudit(ProductionOrder x, int stageCount) => new { x.Id, x.Status, x.ProductionDate, x.ProductionLineId, x.ProductModelId, StageCount = stageCount, x.ApprovedBy, x.ApprovedAtUtc, x.ConcurrencyToken, Result = "Success" };
    private static object DailyApprovalRecordAudit(StageProductionRecord x, int stageCount) => new { x.Id, x.ProductionOrderId, x.ProductModelStageId, x.Status, x.ProductionDate, StageCount = stageCount, x.ApprovedBy, x.ApprovedAtUtc, x.ConcurrencyToken, Result = "Success" };
    private static object DailyApprovalCancellationOrderAudit(ProductionOrder x, int stageCount, string reason) => new { x.Id, x.Status, x.ProductionDate, x.ProductionLineId, x.ProductModelId, StageCount = stageCount, ApprovalCancellationReason = reason.Trim(), x.ConcurrencyToken, Result = "Success" };
    private static object DailyApprovalCancellationRecordAudit(StageProductionRecord x, int stageCount) => new { x.Id, x.ProductionOrderId, x.ProductModelStageId, x.Status, x.ProductionDate, StageCount = stageCount, x.CancelledBy, x.CancelledAtUtc, x.ApprovalCancellationReason, x.ConcurrencyToken, Result = "Success" };
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
    private DateTime ProductionDateEvidenceAtUtc(DateOnly productionDate)
    {
        return ProductionDayBoundsUtc(productionDate).EndUtc.AddTicks(-1);
    }

    private (DateTime StartUtc, DateTime EndUtc) ProductionDayBoundsUtc(DateOnly productionDate)
    {
        var localStart = productionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, cairoTimeZoneProvider.TimeZone),
            TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), cairoTimeZoneProvider.TimeZone));
    }

    private sealed record DailyDraftStageConfiguration(
        Guid ProductModelStageId,
        string StageCode,
        string StageName,
        string MainStageName,
        decimal PiecePrice,
        decimal? StandardSeconds,
        CompensationMode CompensationMode);
    private sealed record CalculatedAllocation(Guid WorkerId, decimal EquivalentQuantity, decimal CalculatedEarning);
}
