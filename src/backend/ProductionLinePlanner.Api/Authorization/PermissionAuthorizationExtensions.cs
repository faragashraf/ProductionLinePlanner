using Microsoft.AspNetCore.Authorization;
using ProductionLinePlanner.Domain.Authorization;

namespace ProductionLinePlanner.Api.Authorization;

public static class PermissionAuthorizationExtensions
{
    private const string PolicyPrefix = "Permission:";

    public static string PolicyName(string permission) => PolicyPrefix + permission;

    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)
    {
        ValidateKnownPermission(permission);
        return builder.RequireAuthorization(PolicyName(permission));
    }

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, string permission)
    {
        ValidateKnownPermission(permission);
        return builder.RequireAuthorization(PolicyName(permission));
    }

    public static void AddPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var permission in PermissionCatalog.All)
        {
            options.AddPolicy(PolicyName(permission.Name), policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new PermissionRequirement(permission.Name));
            });
        }
    }

    private static void ValidateKnownPermission(string permission)
    {
        if (!PermissionCatalog.IsKnown(permission))
        {
            throw new ArgumentException($"Unknown product permission '{permission}'.", nameof(permission));
        }
    }
}
