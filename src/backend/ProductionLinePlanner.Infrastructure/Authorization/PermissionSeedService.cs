using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Authorization;

public sealed class PermissionSeedService(
    AppDbContext dbContext,
    ILogger<PermissionSeedService> logger) : IRolePermissionSeedService
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

            var addedAssignments = 0;
            var removedAssignments = 0;

            foreach (var role in Enum.GetValues<UserRole>())
            {
                var roleEntity = await dbContext.AppRoles
                    .Include(x => x.Permissions)
                    .FirstOrDefaultAsync(x => x.Role == role, cancellationToken);
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
                        UserRole.HumanResources => "Human resources role.",
                        UserRole.Accounting => "Accounting role.",
                        _ => "System role."
                    };

                    roleEntity = new AppRole(
                        id: Guid.NewGuid(),
                        role: role,
                        name: role.ToString(),
                        description: roleDescription,
                    isSystemRole: true,
                        isActive: true,
                        createdAtUtc: now);
                    dbContext.AppRoles.Add(roleEntity);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                else if (!roleEntity.IsSystemRole)
                {
                    roleEntity.MarkAsSystemRole();
                }

                var targetPermissions = GetPermissionsForRole(role).ToList();
                var authoritativePermissionIds = targetPermissions
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(allPermissions.ContainsKey)
                    .Select(permissionName => allPermissions[permissionName].Id)
                    .ToHashSet();

                var existingPermissionIds = roleEntity.Permissions
                    .Select(assignment => assignment.PermissionId)
                    .ToHashSet();

                foreach (var assignment in roleEntity.Permissions
                             .Where(assignment => !authoritativePermissionIds.Contains(assignment.PermissionId))
                             .ToArray())
                {
                    dbContext.RolePermissions.Remove(assignment);
                    removedAssignments++;
                }

                foreach (var permissionId in authoritativePermissionIds.Where(permissionId => !existingPermissionIds.Contains(permissionId)))
                {
                    dbContext.RolePermissions.Add(new RolePermission(roleEntity.Id, permissionId));
                    addedAssignments++;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Reconciled system role permissions: {AddedAssignments} grants added and {RemovedAssignments} grants removed.",
                addedAssignments,
                removedAssignments);
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
            "workers.view", "workers.manage", "attendance.view", "attendance.sync",
            "factory-structure.view", "factory-structure.manage", "assignments.view", "assignments.manage",
            "departments.view", "departments.manage", "production.view", "production.record", "production.approve",
            "stages.view", "stages.manage", "stages.delete", "models.view", "models.manage",
            "roles.view", "roles.manage", "users.view", "permissions.assign", "audit.view"
        },
        UserRole.Planner => new[]
        {
            "workers.view", "attendance.view", "factory-structure.view", "assignments.view", "assignments.manage",
            "departments.view", "production.view", "stages.view", "models.view",
        },
        UserRole.Supervisor => new[]
        {
            "workers.view", "attendance.view", "factory-structure.view", "assignments.view", "production.view", "stages.view",
        },
        UserRole.Operator => new[]
        {
            "attendance.view", "factory-structure.view", "production.view", "stages.view",
        },
        UserRole.HumanResources => new[]
        {
            "workers.view", "attendance.view", "attendance.sync", "assignments.view"
        },
        UserRole.Accounting => new[]
        {
            // The daily operations screen presents the saved draft together with
            // its operational source data. These are read-only grants only;
            // production.record remains deliberately absent so Accounting cannot
            // recalculate, save, or change any production data.
            "workers.view", "attendance.view", "assignments.view", "stages.view",
            "factory-structure.view", "departments.view", "models.view", "production.view",
            "production.daily-drafts.approve"
        },
        _ => []
    };
}
