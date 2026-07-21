namespace ProductionLinePlanner.Application.Realtime;

public enum ManufacturingEntityType
{
    Factory,
    Department,
    ProductionLine,
    MainStage,
    SubStage,
    ProductModel,
    ProductModelStage,
    ProductionOrder,
    Worker
}

public enum ManufacturingChangeType
{
    Created,
    Updated,
    Deleted,
    Activated,
    Deactivated,
    Reordered,
    RelationshipChanged
}

/// <summary>
/// A compact invalidation hint for manufacturing master data. Consumers refetch
/// from the API; no entity payload is transported through SignalR.
/// </summary>
public sealed record ManufacturingDataChanged(
    Guid EventId,
    ManufacturingEntityType EntityType,
    ManufacturingChangeType ChangeType,
    Guid EntityId,
    DateTime OccurredAtUtc,
    Guid? ActorUserId,
    string? CorrelationId,
    Guid? FactoryId = null,
    Guid? DepartmentId = null,
    Guid? ProductionLineId = null,
    Guid? MainStageId = null,
    Guid? ProductModelId = null,
    Guid? SubStageId = null,
    DateOnly? ProductionDate = null);

public interface IManufacturingDataChangePublisher
{
    Task PublishAsync(ManufacturingDataChanged change, CancellationToken cancellationToken = default);
}

public interface IManufacturingRealtimeCorrelationContext
{
    string? CorrelationId { get; }
}
