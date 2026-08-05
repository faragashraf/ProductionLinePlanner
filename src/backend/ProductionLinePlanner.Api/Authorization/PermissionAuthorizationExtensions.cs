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

    public static RouteHandlerBuilder RequireAnyPermission(this RouteHandlerBuilder builder, params string[] permissions)
    {
        var normalizedPermissions = permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPermissions.Length == 0)
        {
            throw new ArgumentException("At least one product permission is required.", nameof(permissions));
        }

        foreach (var permission in normalizedPermissions)
        {
            ValidateKnownPermission(permission);
        }

        return builder.RequireAuthorization(policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.Requirements.Add(new AnyPermissionRequirement(normalizedPermissions));
        });
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
