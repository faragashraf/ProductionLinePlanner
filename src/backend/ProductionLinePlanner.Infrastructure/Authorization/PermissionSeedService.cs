using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Authorization;

public sealed class PermissionSeedService(AppDbContext dbContext) : IRolePermissionSeedService
{
    private static readonly SemaphoreSlim SeedSemaphore = new(1, 1);
    private static bool _seededOnce;

    public async Task EnsureSeedAsync(CancellationToken cancellationToken = default)
    {
        if (_seededOnce)
        {
            return;
        }

        await SeedSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_seededOnce)
            {
                return;
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var existingPermissions = await dbContext.Permissions
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var permissionByName = existingPermissions
                .ToDictionary(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var permission in PermissionCatalog.All)
            {
                if (permissionByName.ContainsKey(permission.Name))
                {
                    continue;
                }

                var newPermissionId = Guid.NewGuid();
                dbContext.Permissions.Add(new Permission(
                    id: newPermissionId,
                    name: permission.Name,
                    capability: permission.Capability,
                    descriptionAr: permission.DescriptionAr,
                    descriptionEn: permission.DescriptionEn));

                permissionByName[permission.Name] = newPermissionId;
            }

            if (permissionByName.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var allPermissions = await dbContext.Permissions
                .Where(x => permissionByName.Values.Contains(x.Id))
                .ToDictionaryAsync(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase, cancellationToken);

            foreach (var role in Enum.GetValues<UserRole>())
            {
                var roleEntity = await dbContext.AppRoles.FirstOrDefaultAsync(x => x.Role == role, cancellationToken);
                if (roleEntity is null)
                {
                    var now = DateTime.UtcNow;
                    var roleDescription = role switch
                    {
                        UserRole.SuperAdmin => "System role with full access.",
                        UserRole.Admin => "Operations role with broad operational access.",
                        UserRole.Planner => "Planner role.",
                        UserRole.Supervisor => "Supervisor role.",
                        UserRole.Operator => "Operator role.",
                        _ => "System role."
                    };

                    roleEntity = new AppRole(
                        id: Guid.NewGuid(),
                        role: role,
                        name: role.ToString(),
                        description: roleDescription,
                        isSystemRole: role is UserRole.SuperAdmin,
                        isActive: true,
                        createdAtUtc: now);
                    dbContext.AppRoles.Add(roleEntity);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                var targetPermissions = GetPermissionsForRole(role).ToList();
                var rolePermissionNames = targetPermissions
                    .Select(permission => permission.ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var existingAssignments = await (
                        from assignment in dbContext.RolePermissions.AsNoTracking()
                        join permission in dbContext.Permissions.AsNoTracking()
                            on assignment.PermissionId equals permission.Id
                        where assignment.AppRoleId == roleEntity.Id
                        select permission.Name)
                    .ToListAsync(cancellationToken);

                foreach (var permissionName in rolePermissionNames)
                {
                    if (!allPermissions.TryGetValue(permissionName, out var permission))
                    {
                        continue;
                    }

                    if (existingAssignments.Contains(permission.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    dbContext.RolePermissions.Add(new RolePermission(roleEntity.Id, permission.Id));
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _seededOnce = true;
        }
        finally
        {
            SeedSemaphore.Release();
        }
    }

    public static IEnumerable<string> GetPermissionsForRole(UserRole role) => role switch
    {
        UserRole.SuperAdmin => PermissionCatalog.All.Select(x => x.Name),
        UserRole.Admin => new[]
        {
            "workers.view",
            "workers.manage",
            "attendance.view",
            "attendance.sync",
            "factory-structure.view",
            "factory-structure.manage",
            "assignments.view",
            "assignments.manage",
            "departments.view",
            "departments.manage",
            "production.view",
            "production.record",
            "production.approve",
            "stages.view",
            "stages.manage",
            "models.view",
            "models.manage",
            "roles.view",
            "roles.manage",
            "users.view",
            "permissions.assign",
            "audit.view"
        },
        UserRole.Planner => new[]
        {
            "workers.view",
            "attendance.view",
            "factory-structure.view",
            "assignments.view",
            "assignments.manage",
            "departments.view",
            "production.view",
            "stages.view",
            "models.view"
        },
        UserRole.Supervisor => new[]
        {
            "workers.view",
            "attendance.view",
            "factory-structure.view",
            "assignments.view",
            "production.view",
            "stages.view"
        },
        _ => new[]
        {
            "attendance.view",
            "factory-structure.view",
            "production.view",
            "stages.view"
        }
    };
}
