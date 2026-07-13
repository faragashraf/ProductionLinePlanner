using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Authorization;

public sealed class PermissionService(AppDbContext dbContext) : IPermissionService
{
    public async Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var userIsActive = await dbContext.AppUsers
            .AsNoTracking()
            .AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken);

        if (!userIsActive)
        {
            return [];
        }

        var activeRoleIds = await dbContext.AppUsers
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .SelectMany(x => x.Roles)
            .Where(role => role.IsActive)
            .Select(role => role.Id)
            .ToArrayAsync(cancellationToken);

        var rolePermissionNames = await (
                from rolePermission in dbContext.RolePermissions.AsNoTracking()
                join permission in dbContext.Permissions.AsNoTracking()
                    on rolePermission.PermissionId equals permission.Id
                where activeRoleIds.Contains(rolePermission.AppRoleId) && permission.IsActive
                select permission.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

        var directOverrides = await (
                from permissionOverride in dbContext.UserPermissionOverrides.AsNoTracking()
                join permission in dbContext.Permissions.AsNoTracking()
                    on permissionOverride.PermissionId equals permission.Id
                where permissionOverride.AppUserId == userId && permission.IsActive
                select new { Name = permission.Name, permissionOverride.Effect })
            .ToListAsync(cancellationToken);

        var permissions = new HashSet<string>(rolePermissionNames, StringComparer.OrdinalIgnoreCase);
        foreach (var overrideEntry in directOverrides)
        {
            if (string.IsNullOrWhiteSpace(overrideEntry.Name))
            {
                continue;
            }

            if (overrideEntry.Effect == PermissionEffect.Grant)
            {
                permissions.Add(overrideEntry.Name);
                continue;
            }

            permissions.Remove(overrideEntry.Name);
        }

        return permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission) && PermissionCatalog.IsKnown(permission))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var operationalState = await dbContext.Permissions
            .AsNoTracking()
            .Where(permission => PermissionCatalog.All.Select(entry => entry.Name).Contains(permission.Name))
            .ToDictionaryAsync(permission => permission.Name, permission => permission.IsActive, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return PermissionCatalog.All
            .Select(entry => new PermissionCatalogItemDto
            {
                Name = entry.Name,
                Capability = entry.Capability,
                DescriptionAr = entry.DescriptionAr,
                DescriptionEn = entry.DescriptionEn,
                IsCritical = entry.IsCritical,
                IsActive = operationalState.GetValueOrDefault(entry.Name, false)
            })
            .OrderBy(permission => permission.Capability, StringComparer.OrdinalIgnoreCase)
            .ThenBy(permission => permission.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
