namespace ProductionLinePlanner.Application.Requests;

public sealed record CreateProductionOrderRequest(string OrderNumber, Guid ProductModelId, Guid? ProductionLineId, DateOnly ProductionDate, decimal PlannedQuantity, string? Notes);
public sealed record UpdateProductionOrderRequest(DateOnly ProductionDate, decimal PlannedQuantity, string? Notes);
public sealed record WorkerAllocationRequest(Guid WorkerId, decimal? Percentage, decimal? FixedAmount, string? Notes);
public sealed record CreateStageProductionRecordRequest(Guid ProductionOrderId, Guid ProductModelStageId, DateOnly ProductionDate, decimal ProducedQuantity, decimal AcceptedQuantity, decimal RejectedQuantity, Guid ClientRequestId, string? Notes, IReadOnlyCollection<WorkerAllocationRequest> Workers);
public sealed record UpdateStageProductionRecordRequest(DateOnly ProductionDate, decimal ProducedQuantity, decimal AcceptedQuantity, decimal RejectedQuantity, Guid ConcurrencyToken, string? Notes, IReadOnlyCollection<WorkerAllocationRequest> Workers);
public sealed record RecordActionRequest(Guid ConcurrencyToken);
