using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Api.Realtime;

/// <summary>
/// Stable browser-facing SignalR contract. Keep enum values textual so JavaScript
/// clients never depend on the server serializer's enum representation.
/// </summary>
public sealed record ManufacturingDataChangedMessage(
    Guid EventId,
    string EntityType,
    string ChangeType,
    Guid EntityId,
    DateTime OccurredAtUtc,
    Guid? ActorUserId,
    string? CorrelationId,
    Guid? FactoryId,
    Guid? DepartmentId,
    Guid? ProductionLineId,
    Guid? MainStageId,
    Guid? ProductModelId,
    Guid? SubStageId,
    DateOnly? ProductionDate,
    Guid? WorkerId)
{
    public static ManufacturingDataChangedMessage From(ManufacturingDataChanged change) => new(
        change.EventId,
        change.EntityType.ToString(),
        ChangeTypeValue(change.ChangeType),
        change.EntityId,
        change.OccurredAtUtc,
        change.ActorUserId,
        change.CorrelationId,
        change.FactoryId,
        change.DepartmentId,
        change.ProductionLineId,
        change.MainStageId,
        change.ProductModelId,
        change.SubStageId,
        change.ProductionDate,
        change.WorkerId);

    private static string ChangeTypeValue(ManufacturingChangeType changeType) => changeType switch
    {
        ManufacturingChangeType.PermanentAssignmentCreated => "permanent-assignment-created",
        ManufacturingChangeType.PermanentAssignmentUpdated => "permanent-assignment-updated",
        ManufacturingChangeType.PermanentAssignmentCancelled => "permanent-assignment-cancelled",
        _ => changeType.ToString()
    };
}
