namespace ProductionLinePlanner.Application.Realtime;

public interface INotificationLiveDispatcher
{
    Task SendToUserAsync(
        Guid recipientUserId,
        NotificationSummaryDto notification,
        CancellationToken cancellationToken = default);

    Task SendToCapabilityAsync(
        string permission,
        NotificationSummaryDto notification,
        CancellationToken cancellationToken = default);

    Task SendReadStateToUserAsync(
        Guid recipientUserId,
        NotificationReadStateChangedDto change,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
