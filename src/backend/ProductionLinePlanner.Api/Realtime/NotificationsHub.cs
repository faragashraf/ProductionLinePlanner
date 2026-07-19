using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Api.Realtime;

public static class RealtimeEndpointPaths
{
    public const string NotificationsHub = "/hubs/notifications";
}

public interface INotificationsClient
{
    Task NotificationReceived(NotificationSummaryDto notification);
}

[Authorize]
public sealed class NotificationsHub(
    ICapabilityGroupResolver capabilityGroupResolver,
    ILogger<NotificationsHub> logger) : Hub<INotificationsClient>
{
    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId))
        {
            logger.LogWarning("Rejected an authenticated realtime connection without a valid user identifier.");
            Context.Abort();
            return;
        }

        var groups = await capabilityGroupResolver.ResolveGroupsAsync(userId, Context.ConnectionAborted);
        foreach (var group in groups)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        }

        logger.LogInformation("Realtime connection established with {CapabilityGroupCount} capability groups.", groups.Count);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("Realtime connection closed.");
        await base.OnDisconnectedAsync(exception);
    }
}
