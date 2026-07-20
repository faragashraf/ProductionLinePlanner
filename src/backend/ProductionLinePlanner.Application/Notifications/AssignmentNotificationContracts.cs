namespace ProductionLinePlanner.Application.Notifications;

public sealed record AssignmentNotificationDispatchRequest(
    Guid ActorUserId,
    Guid WorkerId,
    Guid? FromSubStageId,
    Guid? ToSubStageId,
    Guid AssignmentId,
    string AssignmentType);

/// <summary>
/// Bridges successful assignment writes to the existing notification-policy platform.
/// Implementations must never roll back an already committed assignment.
/// </summary>
public interface IAssignmentNotificationDispatcher
{
    Task DispatchAsync(
        AssignmentNotificationDispatchRequest request,
        CancellationToken cancellationToken = default);
}
