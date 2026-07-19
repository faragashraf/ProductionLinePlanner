using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Authorization;

namespace ProductionLinePlanner.Infrastructure.Realtime;

public sealed class CapabilityGroupResolver(IPermissionService permissionService) : ICapabilityGroupResolver
{
    private const string GroupPrefix = "capability:";

    public async Task<IReadOnlyCollection<string>> ResolveGroupsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return [];
        }

        var permissions = await permissionService.GetEffectivePermissionsAsync(userId, cancellationToken);
        return permissions
            .Where(PermissionCatalog.IsKnown)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .Select(GetGroupName)
            .ToArray();
    }

    public string GetGroupName(string permission)
    {
        if (!PermissionCatalog.IsKnown(permission))
        {
            throw new ArgumentException("A known permission is required.", nameof(permission));
        }

        return $"{GroupPrefix}{permission.Trim().ToLowerInvariant()}";
    }
}
