namespace ProductionLinePlanner.Application.Requests;

public sealed record CreateProductionOrderRequest(string OrderNumber, Guid ProductModelId, Guid? ProductionLineId, DateOnly ProductionDate, decimal PlannedQuantity, string? Notes);
public sealed record UpdateProductionOrderRequest(DateOnly ProductionDate, decimal PlannedQuantity, string? Notes);
public sealed record WorkerAllocationRequest(
    Guid WorkerId,
    decimal? Percentage,
    decimal? FixedAmount,
    string? Notes,
    string? ManualOverrideReason = null,
    decimal? InputQuantity = null);
public sealed record CreateStageProductionRecordRequest(Guid ProductionOrderId, Guid ProductModelStageId, DateOnly ProductionDate, decimal ProducedQuantity, decimal AcceptedQuantity, decimal RejectedQuantity, Guid ClientRequestId, string? Notes, IReadOnlyCollection<WorkerAllocationRequest> Workers);
public sealed record UpdateStageProductionRecordRequest(DateOnly ProductionDate, decimal ProducedQuantity, decimal AcceptedQuantity, decimal RejectedQuantity, Guid ConcurrencyToken, string? Notes, IReadOnlyCollection<WorkerAllocationRequest> Workers);
public sealed record RecordActionRequest(Guid ConcurrencyToken);
public sealed record CancelProductionApprovalRequest(Guid ConcurrencyToken, string Reason);
public sealed record DailyStageApprovalRequest(Guid StageProductionRecordId, Guid ConcurrencyToken);
public sealed record DailyProductionApprovalRequest(IReadOnlyCollection<DailyStageApprovalRequest> StageApprovals);

/// <summary>
/// A single physical line quantity is deliberately supplied once and expanded
/// to one output per mapped model stage by the daily-operations capability.
/// Worker rows are allocations only and never become production outputs.
/// </summary>
public sealed record DailyProductionStageRequest(
    Guid ProductModelStageId,
    IReadOnlyCollection<WorkerAllocationRequest> Workers);

public sealed record DailyProductionOperationRequest(
    Guid FactoryId,
    Guid ProductionLineId,
    Guid ProductModelId,
    DateOnly ProductionDate,
    decimal LineQuantity,
    Guid ClientRequestId,
    string? Notes,
    string? PreviewToken,
    IReadOnlyCollection<DailyProductionStageRequest> Stages);
