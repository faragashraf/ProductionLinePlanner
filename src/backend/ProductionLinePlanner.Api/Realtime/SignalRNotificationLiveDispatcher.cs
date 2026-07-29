using Microsoft.AspNetCore.SignalR;
using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Api.Realtime;

public sealed class SignalRNotificationLiveDispatcher(
    IHubContext<NotificationsHub, IRealtimeClient> hubContext,
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

    public Task SendReadStateToUserAsync(
        Guid recipientUserId,
        NotificationReadStateChangedDto change,
        CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .User(recipientUserId.ToString("D"))
            .NotificationReadStateChanged(change);
}
