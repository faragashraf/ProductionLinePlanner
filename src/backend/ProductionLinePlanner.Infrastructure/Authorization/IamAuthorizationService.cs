using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Authorization;

public sealed class IamAuthorizationService(AppDbContext dbContext, IIamDelegationPolicy delegationPolicy) : IIamAuthorizationService
{
    public async Task<IamAuthorizationUpdateResult> ReplaceAsync(Guid actorUserId, Guid targetUserId, UserAuthorizationUpdateRequest request, string? requestMeta, CancellationToken cancellationToken = default)
    {
        var grants = Normalize(request.DirectGrants); var denies = Normalize(request.DirectDenies);
        if (grants.Intersect(denies, StringComparer.OrdinalIgnoreCase).Any()) return Fail(400, "ValidationError", "A permission cannot be both directly granted and denied.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var user = await dbContext.AppUsers.Include(x => x.Roles).SingleOrDefaultAsync(x => x.Id == targetUserId, cancellationToken);
        if (user is null) return Fail(404, "NotFound", "User not found.");
        var roleIds = request.RoleIds.Distinct().ToArray();
        if (roleIds.Length == 0) return Fail(400, "ValidationError", "At least one role is required.");
        var roles = await dbContext.AppRoles.Where(role => roleIds.Contains(role.Id) && role.IsActive).ToListAsync(cancellationToken);
        if (roles.Count != roleIds.Length) return Fail(400, "ValidationError", "One or more active roles were not found.");
        foreach (var role in roles.Where(role => user.Roles.All(current => current.Id != role.Id)).Concat(user.Roles.Where(role => role.Role == UserRole.SuperAdmin && roles.All(next => next.Id != role.Id))))
        {
            var decision = await delegationPolicy.CanAssignRoleAsync(actorUserId, targetUserId, role, cancellationToken);
            if (!decision.Allowed) return Fail(403, decision.Code, decision.Message);
        }
        if (user.Roles.Any(role => role.Role == UserRole.SuperAdmin) && roles.All(role => role.Role != UserRole.SuperAdmin))
        {
            var others = await dbContext.AppUsers.Where(candidate => candidate.Id != targetUserId && candidate.IsActive).CountAsync(candidate => candidate.Roles.Any(role => role.Role == UserRole.SuperAdmin), cancellationToken);
            if (others == 0) return Fail(403, "LastSuperAdminProtected", "Cannot remove the last active SuperAdmin.");
        }
        var desired = grants.ToDictionary(name => name, _ => PermissionEffect.Grant, StringComparer.OrdinalIgnoreCase); foreach (var deny in denies) desired[deny] = PermissionEffect.Deny;
        var permissions = await dbContext.Permissions.Where(permission => permission.IsActive && desired.Keys.Contains(permission.Name)).ToListAsync(cancellationToken);
        if (permissions.Count != desired.Count) return Fail(400, "ValidationError", "One or more permissions are unknown or inactive.");
        var overrides = await (from entry in dbContext.UserPermissionOverrides where entry.AppUserId == targetUserId join permission in dbContext.Permissions on entry.PermissionId equals permission.Id select new { Entry = entry, permission.Name }).ToListAsync(cancellationToken);
        foreach (var entry in overrides.Where(entry => !desired.TryGetValue(entry.Name, out var effect) || effect != entry.Entry.Effect)) { var d = await delegationPolicy.CanChangeDirectPermissionAsync(actorUserId, targetUserId, entry.Name, entry.Entry.Effect, true, cancellationToken); if (!d.Allowed) return Fail(403, d.Code, d.Message); }
        foreach (var permission in permissions.Where(permission => !overrides.Any(entry => entry.Entry.PermissionId == permission.Id && desired[permission.Name] == entry.Entry.Effect))) { var d = await delegationPolicy.CanChangeDirectPermissionAsync(actorUserId, targetUserId, permission.Name, desired[permission.Name], false, cancellationToken); if (!d.Allowed) return Fail(403, d.Code, d.Message); }
        var before = new { RoleIds = user.Roles.Select(role => role.Id).ToArray(), Permissions = overrides.Select(entry => $"{entry.Name}:{entry.Entry.Effect}").ToArray() };
        user.Roles.Clear(); foreach (var role in roles) user.Roles.Add(role);
        dbContext.UserPermissionOverrides.RemoveRange(overrides.Select(entry => entry.Entry));
        foreach (var permission in permissions) dbContext.UserPermissionOverrides.Add(new UserPermissionOverride(targetUserId, permission.Id, desired[permission.Name], actorUserId));
        dbContext.Entry(user).Property(nameof(AppUser.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
        dbContext.AuditLogs.Add(new AuditLog(Guid.NewGuid(), actorUserId, AuditActionType.Update, nameof(AppUser), targetUserId.ToString(), JsonSerializer.Serialize(before), JsonSerializer.Serialize(new { RoleIds = roleIds, Permissions = desired.Select(x => $"{x.Key}:{x.Value}").ToArray(), Result = "AuthorizationUpdated" }), requestMeta));
        await dbContext.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(true, 200, string.Empty, string.Empty, roleIds, grants, denies);
    }
    private static string[] Normalize(string[]? values) => (values ?? []).Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static IamAuthorizationUpdateResult Fail(int status, string code, string message) => new(false, status, code, message, [], [], []);
}
