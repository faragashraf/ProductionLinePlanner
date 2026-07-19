using Microsoft.AspNetCore.SignalR;
using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Api.Realtime;

public sealed class SignalRNotificationLiveDispatcher(
    IHubContext<NotificationsHub, INotificationsClient> hubContext,
    ICapabilityGroupResolver capabilityGroupResolver) : INotificationLiveDispatcher
{
    public Task SendToUserAsync(
        Guid recipientUserId,
        NotificationSummaryDto notification,
        CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .User(recipientUserId.ToString("D"))
            .NotificationReceived(notification);

    public Task SendToCapabilityAsync(
        string permission,
        NotificationSummaryDto notification,
        CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(capabilityGroupResolver.GetGroupName(permission))
            .NotificationReceived(notification);
}
