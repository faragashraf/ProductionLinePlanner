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
    StageProductionRecord,
    AttendanceRecord,
    AttendanceSyncState,
    Worker,
    WorkerDefaultAssignment
}

public enum ManufacturingChangeType
{
    Created,
    Updated,
    Deleted,
    Activated,
    Deactivated,
    Reordered,
    RelationshipChanged,
    PermanentAssignmentCreated,
    PermanentAssignmentUpdated,
    PermanentAssignmentCancelled
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
    DateOnly? ProductionDate = null,
    Guid? WorkerId = null,
    string Source = "Application",
    IReadOnlyList<DateOnly>? AffectedAttendanceDates = null,
    IReadOnlyList<Guid>? WorkerIds = null,
    IReadOnlyList<Guid>? DepartmentIds = null,
    int AddedAttendanceCount = 0,
    int UpdatedAttendanceCount = 0,
    IReadOnlyList<string>? WorkerChangeKinds = null,
    IReadOnlyList<string>? AttendanceChangeKinds = null);

public interface IManufacturingDataChangePublisher
{
    Task PublishAsync(ManufacturingDataChanged change, CancellationToken cancellationToken = default);
}

public interface IManufacturingRealtimeCorrelationContext
{
    string? CorrelationId { get; }
}

public interface IManufacturingRealtimeChangeContext
{
    string Source { get; }
    string? CorrelationId { get; }
    DateOnly? ProductionDate { get; }

    IDisposable Begin(string source, string? correlationId = null, DateOnly? productionDate = null);
}
