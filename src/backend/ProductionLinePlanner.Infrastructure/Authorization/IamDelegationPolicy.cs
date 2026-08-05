using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Authorization;

public sealed class IamDelegationPolicy(AppDbContext dbContext, IPermissionService permissionService) : IIamDelegationPolicy
{
    public async Task<DelegationDecision> CanAssignRoleAsync(Guid actorUserId, Guid targetUserId, AppRole role, CancellationToken cancellationToken = default)
    {
        if (actorUserId == targetUserId)
            return DelegationDecision.Deny("SelfPromotionForbidden", "Users cannot change their own role assignments.");

        var actorIsSuperAdmin = await IsSuperAdminAsync(actorUserId, cancellationToken);
        if (actorIsSuperAdmin)
            return DelegationDecision.Permit();

        if (role.Role == UserRole.SuperAdmin)
            return DelegationDecision.Deny("SuperAdminDelegationForbidden", "Only a SuperAdmin can assign SuperAdmin.");

        var permissions = await permissionService.GetEffectivePermissionsAsync(actorUserId, cancellationToken);
        if (!permissions.Contains("users.manage", StringComparer.OrdinalIgnoreCase))
            return DelegationDecision.Deny("DelegationForbidden", "users.manage is required to assign roles.");

        var rolePermissions = await (from assignment in dbContext.RolePermissions.AsNoTracking()
                                     join permission in dbContext.Permissions.AsNoTracking() on assignment.PermissionId equals permission.Id
                                     where assignment.AppRoleId == role.Id && permission.IsActive
                                     select permission.Name).ToArrayAsync(cancellationToken);
        return rolePermissions.All(permission => permissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
            ? DelegationDecision.Permit()
            : DelegationDecision.Deny("DelegationAuthorityExceeded", "The actor cannot delegate permissions they do not hold.");
    }

    public async Task<DelegationDecision> CanChangeDirectPermissionAsync(Guid actorUserId, Guid targetUserId, string permissionName, PermissionEffect effect, bool isRemoval, CancellationToken cancellationToken = default)
    {
        if (actorUserId == targetUserId)
            return DelegationDecision.Deny("SelfPromotionForbidden", "Users cannot change their own direct permissions.");

        if (await IsSuperAdminAsync(actorUserId, cancellationToken))
            return DelegationDecision.Permit();

        var permissions = await permissionService.GetEffectivePermissionsAsync(actorUserId, cancellationToken);
        if (!permissions.Contains("permissions.assign", StringComparer.OrdinalIgnoreCase))
            return DelegationDecision.Deny("DelegationForbidden", "permissions.assign is required to manage direct permissions.");

        var isSensitive = PermissionCatalog.All.Any(entry => entry.IsCritical && string.Equals(entry.Name, permissionName, StringComparison.OrdinalIgnoreCase));
        if (isSensitive)
            return DelegationDecision.Deny("SensitivePermissionDelegationForbidden", "Only a SuperAdmin can delegate sensitive permissions.");

        if (!isRemoval && !permissions.Contains(permissionName, StringComparer.OrdinalIgnoreCase))
            return DelegationDecision.Deny("DelegationAuthorityExceeded", "The actor cannot delegate a permission they do not hold.");

        return DelegationDecision.Permit();
    }

    public async Task<DelegationDecision> CanManageRolePermissionsAsync(Guid actorUserId, IEnumerable<string> permissionNames, CancellationToken cancellationToken = default)
    {
        if (await IsSuperAdminAsync(actorUserId, cancellationToken))
            return DelegationDecision.Permit();

        var permissions = await permissionService.GetEffectivePermissionsAsync(actorUserId, cancellationToken);
        if (!permissions.Contains("roles.manage", StringComparer.OrdinalIgnoreCase))
            return DelegationDecision.Deny("DelegationForbidden", "roles.manage is required to manage custom role permissions.");

        foreach (var permission in permissionNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!permissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
                return DelegationDecision.Deny("DelegationAuthorityExceeded", "The actor cannot add a permission they do not hold.");
            if (PermissionCatalog.All.Any(entry => entry.IsCritical && string.Equals(entry.Name, permission, StringComparison.OrdinalIgnoreCase)))
                return DelegationDecision.Deny("SensitivePermissionDelegationForbidden", "Only a SuperAdmin can add sensitive permissions to a custom role.");
        }

        return DelegationDecision.Permit();
    }

    private Task<bool> IsSuperAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.AppUsers.AsNoTracking().AnyAsync(user => user.Id == userId && user.IsActive && user.Roles.Any(role => role.Role == UserRole.SuperAdmin), cancellationToken);
}
