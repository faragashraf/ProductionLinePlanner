using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Realtime;

public interface INotificationPublisher
{
    Task<Result<NotificationPublishResultDto>> PublishToUserAsync(
        PublishUserNotificationCommand command,
        CancellationToken cancellationToken = default);

    Task<Result> PublishEphemeralToCapabilityAsync(
        PublishCapabilityNotificationCommand command,
        CancellationToken cancellationToken = default);
}
