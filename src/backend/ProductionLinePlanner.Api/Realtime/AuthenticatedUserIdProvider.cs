using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace ProductionLinePlanner.Api.Realtime;

public sealed class AuthenticatedUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => ResolveUserId(connection.User);

    public static string? ResolveUserId(ClaimsPrincipal? principal)
    {
        var rawUserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");
        return Guid.TryParse(rawUserId, out var userId)
            ? userId.ToString("D")
            : null;
    }
}
