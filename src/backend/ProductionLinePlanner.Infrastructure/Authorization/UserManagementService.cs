using System.Data;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Authorization;

public sealed class UserManagementService(
    AppDbContext dbContext,
    IUserPasswordHasher passwordHasher,
    IIamDelegationPolicy delegationPolicy,
    IAuditEngine auditEngine) : IUserManagementService
{
    public async Task<UserManagementResult> CreateAsync(
        Guid actorUserId,
        AdminUserCreateRequest request,
        string? requestMeta,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.FullName, request.Email, request.RoleIds, request.Password, passwordRequired: true);
        if (validation is not null) return validation;

        var loginIdentifier = AppUser.NormalizeLoginIdentifier(request.Email);
        if (await LoginIdentifierExistsAsync(loginIdentifier, null, cancellationToken))
            return Fail(409, "DuplicateLoginIdentifier", "Login identifier is already in use.");

        var rolesResult = await ResolveRolesAsync(request.RoleIds, cancellationToken);
        if (rolesResult.Error is not null) return rolesResult.Error;

        var userId = Guid.NewGuid();
        foreach (var role in rolesResult.Roles)
        {
            var decision = await delegationPolicy.CanAssignRoleAsync(actorUserId, userId, role, cancellationToken);
            if (!decision.Allowed) return Fail(403, decision.Code, decision.Message);
        }

        var user = new AppUser(userId, request.FullName, loginIdentifier, "temporary-password-hash", request.IsActive);
        user.ChangePasswordHash(passwordHasher.HashPassword(user, request.Password));
        foreach (var role in rolesResult.Roles) user.AssignRole(role);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        dbContext.AppUsers.Add(user);
        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(AppUser),
            user.Id.ToString(),
            before: null,
            after: new
            {
                user.FullName,
                user.Email,
                user.IsActive,
                RoleIds = rolesResult.Roles.Select(role => role.Id).ToArray(),
                Result = "UserCreated"
            },
            requestMeta,
            cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Fail(409, "DuplicateLoginIdentifier", "Login identifier is already in use.");
        }

        return UserManagementResult.Success(ToDetails(user), 201);
    }

    public async Task<UserManagementResult> UpdateAsync(
        Guid actorUserId,
        Guid userId,
        AdminUserUpdateRequest request,
        string? requestMeta,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.FullName, request.Email, request.RoleIds, password: null, passwordRequired: false);
        if (validation is not null) return validation;

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var user = await dbContext.AppUsers
            .Include(candidate => candidate.Roles)
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null) return Fail(404, "NotFound", "User not found.");

        var loginIdentifier = AppUser.NormalizeLoginIdentifier(request.Email);
        if (await LoginIdentifierExistsAsync(loginIdentifier, userId, cancellationToken))
            return Fail(409, "DuplicateLoginIdentifier", "Login identifier is already in use.");

        var rolesResult = await ResolveRolesAsync(request.RoleIds, cancellationToken);
        if (rolesResult.Error is not null) return rolesResult.Error;

        var rolesChanged = !user.Roles.Select(role => role.Id).Order().SequenceEqual(rolesResult.Roles.Select(role => role.Id).Order());
        if (rolesChanged)
        {
            foreach (var changedRole in user.Roles.Where(current => rolesResult.Roles.All(next => next.Id != current.Id))
                         .Concat(rolesResult.Roles.Where(next => user.Roles.All(current => current.Id != next.Id))))
            {
                var decision = await delegationPolicy.CanAssignRoleAsync(actorUserId, userId, changedRole, cancellationToken);
                if (!decision.Allowed) return Fail(403, decision.Code, decision.Message);
            }
        }

        var removesSuperAdmin = user.Roles.Any(role => role.Role == UserRole.SuperAdmin)
            && rolesResult.Roles.All(role => role.Role != UserRole.SuperAdmin);
        var disablesSuperAdmin = user.IsActive && !request.IsActive
            && user.Roles.Any(role => role.Role == UserRole.SuperAdmin);
        if (removesSuperAdmin || disablesSuperAdmin)
        {
            var otherActiveSuperAdmins = await dbContext.AppUsers
                .AsNoTracking()
                .Where(candidate => candidate.Id != userId && candidate.IsActive)
                .CountAsync(candidate => candidate.Roles.Any(role => role.Role == UserRole.SuperAdmin), cancellationToken);
            if (otherActiveSuperAdmins == 0)
                return Fail(403, "LastSuperAdminProtected", "Cannot remove or disable the last active SuperAdmin.");
        }

        var before = new
        {
            user.FullName,
            user.Email,
            user.IsActive,
            RoleIds = user.Roles.Select(role => role.Id).ToArray()
        };
        user.UpdateProfile(request.FullName, loginIdentifier, request.IsActive);
        user.Roles.Clear();
        foreach (var role in rolesResult.Roles) user.Roles.Add(role);

        await auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Update,
            nameof(AppUser),
            user.Id.ToString(),
            before,
            new
            {
                user.FullName,
                user.Email,
                user.IsActive,
                RoleIds = rolesResult.Roles.Select(role => role.Id).ToArray(),
                Result = "UserUpdated"
            },
            requestMeta,
            cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Fail(409, "DuplicateLoginIdentifier", "Login identifier is already in use.");
        }

        return UserManagementResult.Success(ToDetails(user));
    }

    private static UserManagementResult? Validate(
        string? fullName,
        string? loginIdentifier,
        Guid[]? roleIds,
        string? password,
        bool passwordRequired)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return Fail(400, "ValidationError", "Full name is required.");
        if (fullName.Trim().Length > AppUser.MaxFullNameLength) return Fail(400, "ValidationError", $"Full name cannot exceed {AppUser.MaxFullNameLength} characters.");
        if (string.IsNullOrWhiteSpace(loginIdentifier)) return Fail(400, "ValidationError", "Login identifier is required.");
        if (loginIdentifier.Trim().Length > AppUser.MaxLoginIdentifierLength) return Fail(400, "ValidationError", $"Login identifier cannot exceed {AppUser.MaxLoginIdentifierLength} characters.");
        if (passwordRequired && string.IsNullOrWhiteSpace(password)) return Fail(400, "ValidationError", "Password is required.");
        if (roleIds is null || roleIds.Length == 0) return Fail(400, "ValidationError", "At least one role is required.");
        return null;
    }

    private async Task<(List<AppRole> Roles, UserManagementResult? Error)> ResolveRolesAsync(Guid[] roleIds, CancellationToken cancellationToken)
    {
        var distinctIds = roleIds.Distinct().ToArray();
        if (distinctIds.Length != roleIds.Length) return ([], Fail(400, "ValidationError", "Duplicate roles are not allowed."));
        var roles = await dbContext.AppRoles.Where(role => distinctIds.Contains(role.Id) && role.IsActive).ToListAsync(cancellationToken);
        return roles.Count == distinctIds.Length
            ? (roles, null)
            : ([], Fail(400, "ValidationError", "One or more active roles were not found."));
    }

    private Task<bool> LoginIdentifierExistsAsync(string loginIdentifier, Guid? excludedUserId, CancellationToken cancellationToken) =>
        dbContext.AppUsers.AsNoTracking().AnyAsync(
            user => (!excludedUserId.HasValue || user.Id != excludedUserId.Value) && user.Email.ToLower() == loginIdentifier,
            cancellationToken);

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static AdminUserDetailsDto ToDetails(AppUser user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        IsActive = user.IsActive,
        PreferredLanguage = user.PreferredLanguage,
        CreatedAtUtc = user.CreatedAtUtc,
        UpdatedAtUtc = user.UpdatedAtUtc,
        RoleIds = user.Roles.OrderBy(role => role.Name).Select(role => role.Id).ToArray(),
        Roles = user.Roles.OrderBy(role => role.Name).Select(role => role.Name).ToArray()
    };

    private static UserManagementResult Fail(int statusCode, string code, string message) =>
        UserManagementResult.Failure(statusCode, code, message);
}
