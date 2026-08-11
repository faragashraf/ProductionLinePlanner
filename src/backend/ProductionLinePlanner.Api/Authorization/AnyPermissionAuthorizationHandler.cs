using Microsoft.AspNetCore.Authorization;
using ProductionLinePlanner.Application.Abstractions;

namespace ProductionLinePlanner.Api.Authorization;

public sealed class AnyPermissionAuthorizationHandler(
    ICurrentUserService currentUserService,
    IPermissionService permissionService)
    : AuthorizationHandler<AnyPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyPermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || !currentUserService.IsAuthenticated || currentUserService.UserId is not { } userId)
        {
            return;
        }

        var permissions = await permissionService.GetEffectivePermissionsAsync(userId);
        if (requirement.PermissionNames.Any(permission => permissions.Contains(permission, StringComparer.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }
    }
}
