using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Api.Realtime;

/// <summary>
/// Stable browser-facing SignalR contract. Keep enum values textual so JavaScript
/// clients never depend on the server serializer's enum representation.
/// </summary>
public sealed record ManufacturingDataChangedMessage(
    Guid EventId,
    string EventType,
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
    Guid? WorkerId,
    string Source,
    IReadOnlyList<DateOnly> AffectedAttendanceDates,
    IReadOnlyList<Guid> WorkerIds,
    IReadOnlyList<Guid> DepartmentIds,
    int AddedAttendanceCount,
    int UpdatedAttendanceCount,
    IReadOnlyList<string> WorkerChangeKinds,
    IReadOnlyList<string> AttendanceChangeKinds,
    OperationalReadinessDeltaDto? OperationalReadiness)
{
    public static ManufacturingDataChangedMessage From(
        ManufacturingDataChanged change,
        OperationalReadinessDeltaDto? operationalReadiness = null) => new(
        change.EventId,
        EventTypeValue(change),
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
        change.WorkerId,
        change.Source,
        change.AffectedAttendanceDates ?? [],
        change.WorkerIds ?? [],
        change.DepartmentIds ?? [],
        change.AddedAttendanceCount,
        change.UpdatedAttendanceCount,
        change.WorkerChangeKinds ?? [],
        change.AttendanceChangeKinds ?? [],
        operationalReadiness);

    private static string EventTypeValue(ManufacturingDataChanged change) => change.EntityType switch
    {
        ManufacturingEntityType.AttendanceRecord => "manufacturing.attendance.changed",
        ManufacturingEntityType.AttendanceSyncState => "manufacturing.attendance-sync.changed",
        ManufacturingEntityType.Worker when change.WorkerChangeKinds?.Contains(
            "department-assignment",
            StringComparer.OrdinalIgnoreCase) == true => "manufacturing.worker-department.changed",
        ManufacturingEntityType.Worker => "manufacturing.workers.changed",
        _ => "manufacturing.data.changed"
    };

    private static string ChangeTypeValue(ManufacturingChangeType changeType) => changeType switch
    {
        ManufacturingChangeType.PermanentAssignmentCreated => "permanent-assignment-created",
        ManufacturingChangeType.PermanentAssignmentUpdated => "permanent-assignment-updated",
        ManufacturingChangeType.PermanentAssignmentCancelled => "permanent-assignment-cancelled",
        _ => changeType.ToString()
    };
}
