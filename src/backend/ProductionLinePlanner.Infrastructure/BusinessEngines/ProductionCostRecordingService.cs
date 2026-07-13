using System.Data;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class ProductionCostRecordingService(AppDbContext db, IAuditEngine audit) : IProductionCostRecordingService
{
    public async Task<ProductionOrderDto> CreateOrderAsync(CreateProductionOrderRequest request, Guid actorId, CancellationToken ct)
    {
        if (!await db.Set<ProductModel>().AnyAsync(x => x.Id == request.ProductModelId && x.IsActive, ct)) throw new InvalidOperationException("An active product model is required.");
        if (await db.Set<ProductionOrder>().AnyAsync(x => x.OrderNumber == request.OrderNumber.Trim(), ct)) throw new ProductionConflictException("Order number already exists.");
        var order = new ProductionOrder(Guid.NewGuid(), request.OrderNumber, request.ProductModelId, request.ProductionLineId, request.ProductionDate, request.PlannedQuantity, request.Notes, actorId, DateTime.UtcNow);
        db.Add(order); await AuditAsync(actorId, AuditActionType.Create, "ProductionOrder", order.Id, null, OrderAudit(order), ct); await db.SaveChangesAsync(ct); return await OrderDtoAsync(order.Id, ct);
    }

    public async Task<IReadOnlyCollection<ProductionOrderDto>> ListOrdersAsync(ProductionOrderStatus? status, CancellationToken ct) =>
        await db.Set<ProductionOrder>().AsNoTracking().Include(x => x.ProductModel).Where(x => !status.HasValue || x.Status == status).OrderByDescending(x => x.ProductionDate).Select(x => new ProductionOrderDto(x.Id, x.OrderNumber, x.ProductModelId, x.ProductModel!.Code, x.ProductionLineId, x.ProductionDate, x.PlannedQuantity, x.Status.ToString(), x.Notes)).ToListAsync(ct);

    public async Task<ProductionOrderDto> UpdateOrderAsync(Guid id, UpdateProductionOrderRequest request, Guid actorId, CancellationToken ct)
    { var order = await OrderAsync(id, ct); var before = OrderAudit(order); order.UpdateDraft(request.ProductionDate, request.PlannedQuantity, request.Notes, actorId, DateTime.UtcNow); await AuditAsync(actorId, AuditActionType.Update, "ProductionOrder", id, before, OrderAudit(order), ct); await db.SaveChangesAsync(ct); return await OrderDtoAsync(id, ct); }

    public async Task<ProductionOrderDto> TransitionOrderAsync(Guid id, ProductionOrderStatus status, Guid actorId, CancellationToken ct)
    {
        var order = await OrderAsync(id, ct, includeRecords: true); var before = OrderAudit(order); var now = DateTime.UtcNow;
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
        EnsureRecordableOrder(order); var subStage = await db.Set<SubStage>().AsNoTracking().SingleAsync(x => x.Id == stage.SubStageId, ct); var now = DateTime.UtcNow;
        var record = new StageProductionRecord(Guid.NewGuid(), order.Id, stage.Id, request.ProductionDate, request.ProducedQuantity, request.AcceptedQuantity, request.RejectedQuantity, subStage.Code, subStage.Name, stage.PiecePrice, stage.StandardSeconds, stage.CompensationMode, order.ProductModel!.Code, order.ProductModel.Name, request.ClientRequestId, request.Notes, actorId, now);
        record.ReplaceAllocations(await BuildAllocationsAsync(stage.CompensationMode, request.AcceptedQuantity, stage.PiecePrice, request.Workers, ct)); db.Add(record);
        await AuditAsync(actorId, AuditActionType.Create, "StageProductionRecord", record.Id, null, RecordAudit(record), ct); await AuditAsync(actorId, AuditActionType.Update, "StageProductionWorkerAllocation", record.Id, null, AllocationAudit(record), ct); await db.SaveChangesAsync(ct); return await GetRecordAsync(record.Id, ct);
    }

    public async Task<StageProductionRecordDto> CalculatePreviewAsync(CreateStageProductionRecordRequest request, Guid actorId, CancellationToken ct)
    {
        var (order, stage) = await LoadOrderAndStageAsync(request.ProductionOrderId, request.ProductModelStageId, ct); EnsureRecordableOrder(order);
        var subStage = await db.Set<SubStage>().AsNoTracking().SingleAsync(x => x.Id == stage.SubStageId, ct);
        var record = new StageProductionRecord(Guid.NewGuid(), order.Id, stage.Id, request.ProductionDate, request.ProducedQuantity, request.AcceptedQuantity, request.RejectedQuantity, subStage.Code, subStage.Name, stage.PiecePrice, stage.StandardSeconds, stage.CompensationMode, order.ProductModel!.Code, order.ProductModel.Name, request.ClientRequestId == Guid.Empty ? Guid.NewGuid() : request.ClientRequestId, request.Notes, actorId, DateTime.UtcNow);
        record.ReplaceAllocations(await BuildAllocationsAsync(stage.CompensationMode, request.AcceptedQuantity, stage.PiecePrice, request.Workers, ct)); record.SetCalculationPreview(record.WorkerAllocations.Sum(x => x.CalculatedEarning));
        return ToRecordDto(record);
    }

    public async Task<StageProductionRecordDto> UpdateDraftAsync(Guid id, UpdateStageProductionRecordRequest request, Guid actorId, CancellationToken ct)
    {
        var record = await RecordAsync(id, ct); EnsureCurrentVersion(record, request.ConcurrencyToken); db.Entry(record).Property(x => x.ConcurrencyToken).OriginalValue = request.ConcurrencyToken; EnsureRecordableOrder(record.ProductionOrder!); var before = RecordAudit(record); var allocationsBefore = AllocationAudit(record);
        record.UpdateDraft(request.ProductionDate, request.ProducedQuantity, request.AcceptedQuantity, request.RejectedQuantity, request.Notes); var allocations = await BuildAllocationsAsync(record.SnapshotCompensationMode, request.AcceptedQuantity, record.SnapshotPiecePrice, request.Workers, ct); var removedAllocations = record.ReplaceAllocations(allocations); db.RemoveRange(removedAllocations);
        await AuditAsync(actorId, AuditActionType.Update, "StageProductionRecord", id, before, RecordAudit(record), ct); await AuditAsync(actorId, AuditActionType.Update, "StageProductionWorkerAllocation", id, allocationsBefore, AllocationAudit(record), ct); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new ProductionConflictException("The production record changed while it was being saved. Refresh and try again."); } return await GetRecordAsync(id, ct);
    }

    public async Task<StageProductionRecordDto> GetRecordAsync(Guid id, CancellationToken ct) => ToRecordDto(await RecordAsync(id, ct));

    public async Task<IReadOnlyCollection<StageProductionRecordDto>> ListRecordsAsync(DateOnly? from, DateOnly? to, StageProductionRecordStatus? status, CancellationToken ct)
    { var q = db.Set<StageProductionRecord>().AsNoTracking().Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).Where(x => (!from.HasValue || x.ProductionDate >= from) && (!to.HasValue || x.ProductionDate <= to) && (!status.HasValue || x.Status == status)); return (await q.OrderByDescending(x => x.ProductionDate).ToListAsync(ct)).Select(ToRecordDto).ToList(); }

    public async Task<StageProductionRecordDto> ApproveAsync(Guid id, Guid concurrencyToken, Guid actorId, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var record = await RecordAsync(id, ct);
        EnsureCurrentVersion(record, concurrencyToken);
        if (record.Status == StageProductionRecordStatus.Approved) return ToRecordDto(record);
        db.Entry(record).Property(x => x.ConcurrencyToken).OriginalValue = concurrencyToken; EnsureRecordableOrder(record.ProductionOrder!);
        var currentAccepted = await db.Set<StageProductionRecord>().Where(x => x.ProductionOrderId == record.ProductionOrderId && x.Id != record.Id && x.Status == StageProductionRecordStatus.Approved).SumAsync(x => (decimal?)x.AcceptedQuantity, ct) ?? 0m;
        if (currentAccepted + record.AcceptedQuantity > record.ProductionOrder!.PlannedQuantity) throw new ProductionConflictException("Approved accepted quantity exceeds the production order planned quantity.");
        var before = FinancialAudit(record); var recalculated = await BuildAllocationsAsync(record.SnapshotCompensationMode, record.AcceptedQuantity, record.SnapshotPiecePrice, record.WorkerAllocations.Select(x => new WorkerAllocationRequest(x.WorkerId, x.Percentage, x.FixedAmount, x.Notes)).ToList(), ct);
        foreach (var allocation in record.WorkerAllocations) { var calculated = recalculated.Single(x => x.WorkerId == allocation.WorkerId); allocation.SetCalculatedAmounts(calculated.EquivalentQuantity, calculated.CalculatedEarning); }
        var now = DateTime.UtcNow; record.Approve(record.WorkerAllocations.Sum(x => x.CalculatedEarning), actorId, now);
        await AuditAsync(actorId, AuditActionType.Update, "StageProductionRecord", id, before, FinancialAudit(record), ct); await AuditAsync(actorId, AuditActionType.Update, "StageProductionWorkerAllocation", id, null, AllocationAudit(record), ct); await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); return ToRecordDto(record);
    }

    public async Task<StageProductionRecordDto> CancelAsync(Guid id, Guid concurrencyToken, Guid actorId, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var record = await RecordAsync(id, ct); EnsureCurrentVersion(record, concurrencyToken); db.Entry(record).Property(x => x.ConcurrencyToken).OriginalValue = concurrencyToken; var before = FinancialAudit(record); var now = DateTime.UtcNow; record.Cancel(actorId, now);
        await AuditAsync(actorId, AuditActionType.Cancel, "StageProductionRecord", id, before, FinancialAudit(record), ct); await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); return ToRecordDto(record);
    }

    public async Task<IReadOnlyCollection<DailyProductionCostReportRowDto>> DailyReportAsync(DateOnly from, DateOnly to, Guid? orderId, Guid? modelId, Guid? workerId, CancellationToken ct)
    { var q = db.Set<StageProductionRecord>().AsNoTracking().Include(x => x.ProductionOrder).Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).Where(x => x.Status == StageProductionRecordStatus.Approved && x.ProductionDate >= from && x.ProductionDate <= to && (!orderId.HasValue || x.ProductionOrderId == orderId) && (!modelId.HasValue || x.ProductionOrder!.ProductModelId == modelId) && (!workerId.HasValue || x.WorkerAllocations.Any(a => a.WorkerId == workerId))); return (await q.ToListAsync(ct)).Select(x => new DailyProductionCostReportRowDto(x.Id, x.ProductionDate, x.ProductionOrder!.OrderNumber, x.SnapshotProductModelCode, x.SnapshotStageCode, x.SnapshotStageName, x.ProducedQuantity, x.AcceptedQuantity, x.RejectedQuantity, x.TotalWorkerEarnings, x.SnapshotCompensationMode.ToString(), x.Status.ToString(), x.WorkerAllocations.Select(ToAllocationDto).ToList())).ToList(); }

    private async Task<List<StageProductionWorkerAllocation>> BuildAllocationsAsync(CompensationMode mode, decimal accepted, decimal price, IReadOnlyCollection<WorkerAllocationRequest> workers, CancellationToken ct)
    {
        if (workers.Count == 0) throw new InvalidOperationException("At least one worker allocation is required."); if (workers.Select(x => x.WorkerId).Distinct().Count() != workers.Count) throw new ProductionConflictException("A worker may only appear once in a record.");
        var ids = workers.Select(x => x.WorkerId).ToList(); var valid = await db.Set<Worker>().Where(x => ids.Contains(x.Id) && x.EmploymentStatus != EmploymentStatus.LeftEmployment).Select(x => x.Id).ToListAsync(ct); if (valid.Count != ids.Count) throw new InvalidOperationException("All workers must exist and must not have left employment.");
        var name = mode.ToString(); if (name == "SharedPercentage" && workers.Any(x => !x.Percentage.HasValue || x.Percentage <= 0 || x.FixedAmount.HasValue) || name != "SharedPercentage" && workers.Any(x => x.Percentage.HasValue)) throw new InvalidOperationException("Allocation fields do not match the compensation mode.");
        if (name == "SharedPercentage" && workers.Sum(x => x.Percentage!.Value) != 100m) throw new InvalidOperationException("Shared worker percentages must sum to exactly 100 percent."); if (name == "FixedAmount" && workers.Any(x => !x.FixedAmount.HasValue || x.FixedAmount < 0)) throw new InvalidOperationException("Fixed amount allocations require a non-negative amount."); if (name != "FixedAmount" && workers.Any(x => x.FixedAmount.HasValue)) throw new InvalidOperationException("Fixed amount is only valid for FixedAmount mode.");
        return workers.Select(x => { var allocation = new StageProductionWorkerAllocation(Guid.NewGuid(), x.WorkerId, x.Percentage, x.FixedAmount, x.Notes); var equivalent = name == "SharedPercentage" ? accepted * x.Percentage!.Value / 100m : 0m; var earning = name == "SharedPercentage" ? equivalent * price : name == "FixedAmount" ? x.FixedAmount!.Value : accepted * price; allocation.SetCalculatedAmounts(equivalent, earning); return allocation; }).ToList();
    }

    private async Task<(ProductionOrder Order, ProductModelStage Stage)> LoadOrderAndStageAsync(Guid orderId, Guid stageId, CancellationToken ct) { var order = await OrderAsync(orderId, ct); var stage = await db.Set<ProductModelStage>().SingleOrDefaultAsync(x => x.Id == stageId && x.ProductModelId == order.ProductModelId && x.IsActive, ct) ?? throw new KeyNotFoundException("Active model stage was not found for this production order."); return (order, stage); }
    private static void EnsureRecordableOrder(ProductionOrder order) { if (order.Status != ProductionOrderStatus.Active) throw new ProductionConflictException("Production can only be recorded against an active production order."); }
    private static void EnsureCurrentVersion(StageProductionRecord record, Guid supplied) { if (supplied == Guid.Empty || record.ConcurrencyToken != supplied) throw new ProductionConflictException("The production record has changed. Refresh and try again."); }
    private async Task<ProductionOrder> OrderAsync(Guid id, CancellationToken ct, bool includeRecords = false) { var query = db.Set<ProductionOrder>().Include(x => x.ProductModel).AsQueryable(); if (includeRecords) query = query.Include(x => x.StageProductionRecords); return await query.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Production order was not found."); }
    private async Task<ProductionOrderDto> OrderDtoAsync(Guid id, CancellationToken ct) { var x = await OrderAsync(id, ct); return new(x.Id, x.OrderNumber, x.ProductModelId, x.ProductModel!.Code, x.ProductionLineId, x.ProductionDate, x.PlannedQuantity, x.Status.ToString(), x.Notes); }
    private async Task<StageProductionRecord> RecordAsync(Guid id, CancellationToken ct) => await db.Set<StageProductionRecord>().Include(x => x.ProductionOrder).ThenInclude(x => x!.ProductModel).Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Production record was not found.");
    private async Task<StageProductionRecordDto> GetRecordByClientRequestAsync(Guid orderId, Guid clientRequestId, CancellationToken ct) => ToRecordDto(await db.Set<StageProductionRecord>().Include(x => x.ProductionOrder).ThenInclude(x => x!.ProductModel).Include(x => x.WorkerAllocations).ThenInclude(x => x.Worker).SingleAsync(x => x.ProductionOrderId == orderId && x.ClientRequestId == clientRequestId, ct));
    private static StageProductionRecordDto ToRecordDto(StageProductionRecord x) => new(x.Id, x.ProductionOrderId, x.ProductModelStageId, x.ProductionDate, x.ProducedQuantity, x.AcceptedQuantity, x.RejectedQuantity, x.Status.ToString(), x.SnapshotStageCode, x.SnapshotStageName, x.SnapshotProductModelCode, x.SnapshotProductModelName, x.SnapshotPiecePrice, x.SnapshotStandardSeconds, x.SnapshotCompensationMode.ToString(), x.TotalWorkerEarnings, x.ConcurrencyToken, x.WorkerAllocations.Select(ToAllocationDto).ToList(), x.Notes);
    private static ProductionWorkerAllocationDto ToAllocationDto(StageProductionWorkerAllocation x) => new(x.WorkerId, x.Worker?.FullName ?? string.Empty, x.Percentage, x.FixedAmount, x.EquivalentQuantity, x.CalculatedEarning, x.Notes);
    private async Task AuditAsync(Guid actorId, AuditActionType action, string entityType, Guid entityId, object? before, object? after, CancellationToken ct) { var result = await audit.RecordAsync(actorId, action, entityType, entityId.ToString(), before, after, "ProductionCostRecording", ct); if (result.IsFailure) throw new InvalidOperationException(result.Error?.Message ?? "Production audit failed."); }
    private static object OrderAudit(ProductionOrder x) => new { x.Id, x.OrderNumber, x.Status, x.ProductionDate, x.PlannedQuantity, x.ConcurrencyToken, Result = "Success" };
    private static object RecordAudit(StageProductionRecord x) => new { x.Id, x.ProductionOrderId, x.ProductModelStageId, x.Status, x.ProductionDate, x.ProducedQuantity, x.AcceptedQuantity, x.RejectedQuantity, x.SnapshotProductModelCode, x.SnapshotProductModelName, x.SnapshotStageCode, x.SnapshotStageName, x.SnapshotPiecePrice, x.SnapshotStandardSeconds, x.SnapshotCompensationMode, x.TotalWorkerEarnings, x.ConcurrencyToken, Result = "Success" };
    private static object AllocationAudit(StageProductionRecord x) => new { x.Id, x.Status, WorkerCount = x.WorkerAllocations.Count, TotalEarnings = x.WorkerAllocations.Sum(a => a.CalculatedEarning), Result = "Success" };
    private static object FinancialAudit(StageProductionRecord x) => new { RecordId = x.Id, OrderId = x.ProductionOrderId, OrderNumber = x.ProductionOrder?.OrderNumber, x.SnapshotProductModelCode, x.SnapshotProductModelName, x.SnapshotStageCode, x.SnapshotStageName, x.SnapshotPiecePrice, x.SnapshotStandardSeconds, CompensationMode = x.SnapshotCompensationMode, x.Status, x.ProducedQuantity, x.AcceptedQuantity, x.RejectedQuantity, TotalEarnings = x.TotalWorkerEarnings, Allocations = x.WorkerAllocations.Take(100).Select(a => new { a.WorkerId, EmployeeCode = a.Worker?.EmployeeCode, a.Percentage, a.FixedAmount, a.EquivalentQuantity, a.CalculatedEarning }).ToArray(), Result = "Success" };
}
