using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProductionLinePlanner.Application.Abstractions;

namespace ProductionLinePlanner.Api.Security;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var rawUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub");
            if (Guid.TryParse(rawUserId, out var userId))
            {
                return userId;
            }

            return null;
        }
    }

    public string? UserName => User?.Identity?.Name;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles
    {
        get
        {
            if (User is null)
            {
                return [];
            }

            return User.FindAll(ClaimTypes.Role)
                .Select(role => role.Value)
                .Concat(User.FindAll("role").Select(role => role.Value))
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
