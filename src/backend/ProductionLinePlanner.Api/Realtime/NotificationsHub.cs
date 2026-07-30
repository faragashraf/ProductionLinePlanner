using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Authorization;

namespace ProductionLinePlanner.Api.Realtime;

public static class RealtimeEndpointPaths
{
    public const string NotificationsHub = "/hubs/notifications";
}

public interface IRealtimeClient
{
    Task NotificationReceived(NotificationSummaryDto notification);
    Task NotificationReadStateChanged(NotificationReadStateChangedDto change);
    Task ManufacturingDataChanged(ManufacturingDataChangedMessage change);
}

[Authorize]
public sealed class NotificationsHub(
    ICapabilityGroupResolver capabilityGroupResolver,
    IPermissionService permissionService,
    ILogger<NotificationsHub> logger) : Hub<IRealtimeClient>
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

    public async Task JoinManufacturingScreen(string screen)
    {
        var group = await ResolveManufacturingGroupAsync(screen);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }

    public async Task LeaveManufacturingScreen(string screen)
    {
        var group = await ResolveManufacturingGroupAsync(screen);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }

    private async Task<string> ResolveManufacturingGroupAsync(string screen)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId))
        {
            throw new HubException("A valid authenticated user is required.");
        }

        var requiredPermissions = ManufacturingRealtimeGroups.RequiredPermissions(screen);
        if (requiredPermissions.Count == 0)
        {
            throw new HubException("Unknown manufacturing realtime screen.");
        }

        var permissions = await permissionService.GetEffectivePermissionsAsync(userId, Context.ConnectionAborted);
        if (requiredPermissions.Any(permission => !permissions.Contains(permission, StringComparer.OrdinalIgnoreCase)))
        {
            throw new HubException("You are not authorized for this manufacturing realtime screen.");
        }

        return ManufacturingRealtimeGroups.ForScreen(screen);
    }
}
