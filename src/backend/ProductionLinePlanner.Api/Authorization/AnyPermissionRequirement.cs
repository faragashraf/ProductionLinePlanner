using Microsoft.AspNetCore.Authorization;

namespace ProductionLinePlanner.Api.Authorization;

public sealed class AnyPermissionRequirement(IEnumerable<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> PermissionNames { get; } = permissions
        .Where(permission => !string.IsNullOrWhiteSpace(permission))
        .Select(permission => permission.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
