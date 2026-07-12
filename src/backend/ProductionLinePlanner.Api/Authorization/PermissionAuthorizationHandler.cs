using Microsoft.AspNetCore.Authorization;
using ProductionLinePlanner.Application.Abstractions;

namespace ProductionLinePlanner.Api.Authorization;

public sealed class PermissionAuthorizationHandler(
    ICurrentUserService currentUserService,
    IPermissionService permissionService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || !currentUserService.IsAuthenticated)
        {
            return;
        }

        var userId = currentUserService.UserId;
        if (userId is null)
        {
            return;
        }

        var permissions = await permissionService.GetEffectivePermissionsAsync(userId.Value);
        if (permissions.Contains(requirement.PermissionName, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
    }
}
