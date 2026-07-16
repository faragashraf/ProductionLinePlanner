using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Data;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Diagnostics;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Infrastructure.Data;

public static class IamAdminEndpoints
{
    public static void MapIamAdminEndpoints(this WebApplication app)
    {
var adminApi = app.MapGroup("/api/admin").RequireAuthorization();

adminApi.MapGet("/permissions/catalog", async (
    IPermissionService permissionService,
    CancellationToken cancellationToken) =>
{
    var catalog = await permissionService.GetCatalogAsync(cancellationToken);
    var grouped = catalog
        .Where(permission => permission.IsActive)
        .OrderBy(permission => permission.Capability)
        .ThenBy(permission => permission.Name)
        .GroupBy(permission => permission.Capability, StringComparer.OrdinalIgnoreCase)
        .Select(group => new PermissionCatalogGroupDto
        {
            Capability = group.Key,
            Permissions = group.Select(permission => new PermissionCatalogItemDto
            {
                Name = permission.Name,
                Capability = permission.Capability,
                DescriptionAr = permission.DescriptionAr,
                DescriptionEn = permission.DescriptionEn,
                IsCritical = permission.IsCritical,
                IsActive = permission.IsActive
            })
                .ToArray()
        })
        .ToArray();

    return Results.Ok(ApiResponse.Success(grouped));
})
    .RequirePermission("permissions.assign")
    .WithTags("IAM")
    .WithName("GetPermissionCatalog");

adminApi.MapGet("/users", async (
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var users = await dbContext.AppUsers
        .AsNoTracking()
        .Include(x => x.Roles)
        .OrderBy(x => x.FullName)
        .ThenBy(x => x.Email)
        .Select(user => new AdminUserListItemDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            IsActive = user.IsActive,
            Roles = user.Roles
                .OrderBy(role => role.Name)
                .Select(role => role.Name)
                .ToArray()
        })
        .ToArrayAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(users));
})
    .RequirePermission("users.view")
    .WithTags("IAM")
    .WithName("ListUsers");

adminApi.MapGet("/users/{userId:guid}/authorization", async (
    Guid userId,
    AppDbContext dbContext,
    IPermissionService permissionService,
    CancellationToken cancellationToken) =>
{
    var user = await dbContext.AppUsers
        .AsNoTracking()
        .Include(user => user.Roles)
        .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

    if (user is null)
    {
        return ApiResponse.Failure("NotFound", "User not found.", 404);
    }

    var catalog = (await permissionService.GetCatalogAsync(cancellationToken))
        .Where(permission => permission.IsActive)
        .ToDictionary(permission => permission.Name, permission => permission, StringComparer.OrdinalIgnoreCase);

    var userPermissionGrantOverrides = await (
            from permissionOverride in dbContext.UserPermissionOverrides.AsNoTracking()
            join permission in dbContext.Permissions.AsNoTracking()
                on permissionOverride.PermissionId equals permission.Id
            where permissionOverride.AppUserId == userId && permission.IsActive
            select new { Name = permission.Name, permissionOverride.Effect })
        .ToArrayAsync(cancellationToken);

    var rolePermissionNames = await (
            from rolePermission in dbContext.RolePermissions.AsNoTracking()
            join permission in dbContext.Permissions.AsNoTracking()
                on rolePermission.PermissionId equals permission.Id
            where rolePermission.AppRoleId != Guid.Empty &&
                  permission.IsActive &&
                  dbContext.AppUsers.Any(user => user.Id == userId && user.Roles.Any(role => role.Id == rolePermission.AppRoleId))
            select permission.Name)
        .Distinct()
        .ToArrayAsync(cancellationToken);

    var roleGrants = new HashSet<string>(rolePermissionNames, StringComparer.OrdinalIgnoreCase);
    var directGrants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var directDenies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var overrideEntry in userPermissionGrantOverrides)
    {
        if (overrideEntry.Effect == PermissionEffect.Grant)
        {
            directGrants.Add(overrideEntry.Name);
        }
        else
        {
            directDenies.Add(overrideEntry.Name);
        }
    }

    var allPermissionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    allPermissionNames.UnionWith(roleGrants);
    allPermissionNames.UnionWith(directGrants);
    allPermissionNames.UnionWith(directDenies);

    var effectivePermissions = await permissionService.GetEffectivePermissionsAsync(userId, cancellationToken);
    var effectivePermissionSet = effectivePermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

    var effective = allPermissionNames
        .Where(allPermissionNames.Contains)
        .Select(permissionName =>
        {
            var sources = new List<string>();
            if (roleGrants.Contains(permissionName))
            {
                sources.Add("Role Grant");
            }

            if (directGrants.Contains(permissionName))
            {
                sources.Add("User Grant");
            }

            if (directDenies.Contains(permissionName))
            {
                sources.Add("User Deny");
            }

            var description = catalog.GetValueOrDefault(permissionName);
            return new EffectivePermissionItemDto
            {
                Permission = permissionName,
                Granted = effectivePermissionSet.Contains(permissionName),
                Sources = sources.ToArray(),
                IsCritical = description?.IsCritical ?? false,
                DescriptionAr = description?.DescriptionAr,
                DescriptionEn = description?.DescriptionEn
            };
        })
        .OrderBy(x => x.Permission)
        .ToArray();

    return Results.Ok(ApiResponse.Success(new AdminUserAuthorizationDto
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        IsActive = user.IsActive,
        PermissionsVersion = user.UpdatedAtUtc,
        Roles = user.Roles
            .OrderBy(role => role.Name)
            .Select(role => role.Name)
            .ToArray(),
        DirectGrants = directGrants
            .OrderBy(permission => permission)
            .ToArray(),
        DirectDenies = directDenies
            .OrderBy(permission => permission)
            .ToArray(),
        EffectivePermissions = effective
    }));
})
    .RequirePermission("users.view")
    .WithTags("IAM")
    .WithName("GetUserAuthorization");

adminApi.MapPatch("/users/{userId:guid}/roles", async (
    Guid userId,
    UserRoleAssignmentsRequest request,
    AppDbContext dbContext,
    IIamDelegationPolicy delegationPolicy,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    await using var roleAssignmentTransaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

    var user = await dbContext.AppUsers
        .Include(x => x.Roles)
        .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

    if (user is null)
    {
        return ApiResponse.Failure("NotFound", "User not found.", 404);
    }

    var requestedRoleNames = NormalizeRoleInputs(request.Roles);
    if (requestedRoleNames.Length == 0)
    {
        return ApiResponse.Failure("ValidationError", "At least one role is required.");
    }

    var normalizedRequestedRoleNames = requestedRoleNames
        .Select(role => role.ToUpperInvariant())
        .ToArray();
    var requestedRoleEntities = await dbContext.AppRoles
        .Where(role => normalizedRequestedRoleNames.Contains(role.Name.ToUpper()))
        .ToListAsync(cancellationToken);

    if (requestedRoleEntities.Count != requestedRoleNames.Length)
    {
        return ApiResponse.Failure("ValidationError", "One or more roles were not found in database.");
    }

    foreach (var role in requestedRoleEntities.Where(role => user.Roles.All(current => current.Id != role.Id)))
    {
        var decision = await delegationPolicy.CanAssignRoleAsync(actorUserId.Value, userId, role, cancellationToken);
        if (!decision.Allowed)
        {
            return ApiResponse.Failure(decision.Code, decision.Message, 403);
        }
    }

    foreach (var role in user.Roles.Where(role => role.Role == UserRole.SuperAdmin && requestedRoleEntities.All(requested => requested.Id != role.Id)))
    {
        var decision = await delegationPolicy.CanAssignRoleAsync(actorUserId.Value, userId, role, cancellationToken);
        if (!decision.Allowed)
        {
            return ApiResponse.Failure(decision.Code, "Only another SuperAdmin can remove SuperAdmin.", 403);
        }
    }

    if (user.Roles.Any(role => role.Role == UserRole.SuperAdmin) && requestedRoleEntities.All(role => role.Role != UserRole.SuperAdmin))
    {
        var otherActiveSuperAdmins = await dbContext.AppUsers
            .AsNoTracking()
            .Where(x => x.Id != userId && x.IsActive)
            .CountAsync(x => x.Roles.Any(role => role.Role == UserRole.SuperAdmin), cancellationToken);

        if (SuperAdminProtection.WouldRemoveLastActiveSuperAdmin(true, otherActiveSuperAdmins))
        {
            return ApiResponse.Failure("Forbidden", "Cannot remove SuperAdmin role from the last active SuperAdmin user.", 403);
        }
    }

    var beforeRoles = user.Roles
        .Select(role => role.Name)
        .OrderBy(role => role)
        .ToArray();

    user.Roles.Clear();
    foreach (var role in requestedRoleEntities)
    {
        user.Roles.Add(role);
    }

    dbContext.Entry(user).Property(nameof(AppUser.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    var afterRoles = user.Roles
        .Select(role => role.Name)
        .OrderBy(role => role)
        .ToArray();

    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Update,
        nameof(AppUser),
        user.Id.ToString(),
        before: new { roles = beforeRoles },
        after: new { roles = afterRoles },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    await roleAssignmentTransaction.CommitAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new { userId = user.Id, roles = afterRoles }));
})
    .RequirePermission("users.manage")
    .WithTags("IAM")
    .WithName("UpdateUserRoles");

adminApi.MapPut("/users/{userId:guid}/authorization", async (
    Guid userId,
    UserAuthorizationUpdateRequest request,
    IIamAuthorizationService authorizationService,
    ICurrentUserService currentUserService,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    var result = await authorizationService.ReplaceAsync(actorUserId.Value, userId, request, $"{httpContext.Request.Method} {httpContext.Request.Path}", cancellationToken);
    return result.Succeeded
        ? Results.Ok(ApiResponse.Success(new { userId, roleIds = result.RoleIds, directGrants = result.DirectGrants, directDenies = result.DirectDenies }))
        : ApiResponse.Failure(result.Code, result.Message, result.StatusCode);
})
    .RequirePermission("users.manage")
    .WithTags("IAM")
    .WithName("ReplaceUserAuthorization");

adminApi.MapPatch("/users/{userId:guid}/status", async (
    Guid userId,
    UserStatusRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    await using var userStatusTransaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

    var user = await dbContext.AppUsers
        .Include(x => x.Roles)
        .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

    if (user is null)
    {
        return ApiResponse.Failure("NotFound", "User not found.", 404);
    }

    if (!request.IsActive && user.IsActive && user.Roles.Any(role => role.Role == UserRole.SuperAdmin))
    {
        var otherActiveSuperAdmins = await dbContext.AppUsers
            .AsNoTracking()
            .Where(x => x.Id != userId && x.IsActive)
            .CountAsync(x => x.Roles.Any(role => role.Role == UserRole.SuperAdmin), cancellationToken);

        if (SuperAdminProtection.WouldRemoveLastActiveSuperAdmin(true, otherActiveSuperAdmins))
        {
            return ApiResponse.Failure("Forbidden", "Cannot disable the last active SuperAdmin user.", 403);
        }
    }

    if (user.IsActive == request.IsActive)
    {
        return Results.Ok(ApiResponse.Success(new { userId = user.Id, isActive = user.IsActive }));
    }

    var before = new { user.IsActive };
    dbContext.Entry(user).Property(nameof(AppUser.IsActive)).CurrentValue = request.IsActive;
    dbContext.Entry(user).Property(nameof(AppUser.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Update,
        nameof(AppUser),
        user.Id.ToString(),
        before,
        new { isActive = request.IsActive },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    await userStatusTransaction.CommitAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new { userId = user.Id, isActive = user.IsActive }));
})
    .RequirePermission("users.manage")
    .WithTags("IAM")
    .WithName("SetUserStatus");

adminApi.MapPost("/users/{userId:guid}/permission-overrides", async (
    Guid userId,
    UserPermissionOverrideRequest request,
    AppDbContext dbContext,
    IIamDelegationPolicy delegationPolicy,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var permissionName = request.Permission?.Trim();
    if (string.IsNullOrWhiteSpace(permissionName))
    {
        return ApiResponse.Failure("ValidationError", "Permission is required.");
    }

    if (!TryParsePermissionEffect(request.Effect, out var effect))
    {
        return ApiResponse.Failure("ValidationError", "Effect must be either Grant or Deny.");
    }

    var user = await dbContext.AppUsers
        .AsNoTracking()
        .AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken);
    if (!user)
    {
        return ApiResponse.Failure("NotFound", "Active user not found.", 404);
    }

    var permission = await dbContext.Permissions.FirstOrDefaultAsync(x => x.Name == permissionName && x.IsActive, cancellationToken);
    if (permission is null)
    {
        return ApiResponse.Failure("ValidationError", "Unknown or inactive permission.");
    }

    var delegationDecision = await delegationPolicy.CanChangeDirectPermissionAsync(actorUserId.Value, userId, permission.Name, effect, false, cancellationToken);
    if (!delegationDecision.Allowed)
    {
        return ApiResponse.Failure(delegationDecision.Code, delegationDecision.Message, 403);
    }

    var existingOverride = await dbContext.UserPermissionOverrides
        .FirstOrDefaultAsync(x => x.AppUserId == userId && x.PermissionId == permission.Id, cancellationToken);

    if (existingOverride is not null)
    {
        return ApiResponse.Failure("Conflict", "A direct override already exists for this user and permission.", 409);
    }

    dbContext.UserPermissionOverrides.Add(new UserPermissionOverride(
        appUserId: userId,
        permissionId: permission.Id,
        effect: effect,
        createdByUserId: actorUserId));

    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Update,
        nameof(AppUser),
        userId.ToString(),
        before: null,
        after: new { permission = permission.Name, effect = effect.ToString() },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new
    {
        userId,
        permission = permission.Name,
        effect = effect.ToString()
    }));
})
    .RequirePermission("permissions.assign")
    .WithTags("IAM")
    .WithName("SetUserPermissionOverride");

adminApi.MapDelete("/users/{userId:guid}/permission-overrides/{permissionName}", async (
    Guid userId,
    string permissionName,
    AppDbContext dbContext,
    IIamDelegationPolicy delegationPolicy,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserService.UserId;
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var normalizedPermissionName = permissionName.Trim();
    if (string.IsNullOrWhiteSpace(normalizedPermissionName))
    {
        return ApiResponse.Failure("ValidationError", "Permission is required.");
    }

    var userExists = await dbContext.AppUsers
        .AsNoTracking()
        .AnyAsync(x => x.Id == userId, cancellationToken);
    if (!userExists)
    {
        return ApiResponse.Failure("NotFound", "User not found.", 404);
    }

    var permission = await dbContext.Permissions
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Name == normalizedPermissionName, cancellationToken);
    if (permission is null)
    {
        return ApiResponse.Failure("ValidationError", "Permission is unknown.");
    }

    var existingOverride = await dbContext.UserPermissionOverrides
        .FirstOrDefaultAsync(x => x.AppUserId == userId && x.PermissionId == permission.Id, cancellationToken);
    if (existingOverride is null)
    {
        return Results.Ok(ApiResponse.Success(new { removed = false }));
    }

    var delegationDecision = await delegationPolicy.CanChangeDirectPermissionAsync(actorUserId.Value, userId, permission.Name, existingOverride.Effect, true, cancellationToken);
    if (!delegationDecision.Allowed)
    {
        return ApiResponse.Failure(delegationDecision.Code, delegationDecision.Message, 403);
    }

    dbContext.UserPermissionOverrides.Remove(existingOverride);
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Update,
        nameof(AppUser),
        userId.ToString(),
        before: new { permission = permission.Name, effect = existingOverride.Effect.ToString() },
        after: null,
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new { removed = true }));
})
    .RequirePermission("permissions.assign")
    .WithTags("IAM")
    .WithName("RemoveUserPermissionOverride");

adminApi.MapGet("/roles", async (
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var roles = await dbContext.AppRoles
        .AsNoTracking()
        .Select(role => new
        {
            Id = role.Id,
            Name = role.Name,
            role.Description,
            role.IsSystemRole,
            role.IsActive,
            AssignedUsers = dbContext.AppUsers.Count(user => user.Roles.Any(r => r.Id == role.Id))
        })
        .ToArrayAsync(cancellationToken);

    var rolePermissions = await dbContext.RolePermissions
        .AsNoTracking()
        .Join(
            dbContext.Permissions.AsNoTracking(),
            rolePermission => rolePermission.PermissionId,
            permission => permission.Id,
            (rolePermission, permission) => new
            {
                rolePermission.AppRoleId,
                PermissionName = permission.Name,
                permission.IsActive
            })
        .Where(item => item.IsActive)
        .ToArrayAsync(cancellationToken);

    var permissionsByRole = rolePermissions
        .GroupBy(item => item.AppRoleId)
        .ToDictionary(
            group => group.Key,
            group => group
                .Select(item => item.PermissionName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

    var mappedRoles = roles
        .Select(role => new AdminRoleDto
        {
            Id = role.Id,
            Role = role.Name,
            Name = role.Name,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            IsActive = role.IsActive,
            Permissions = permissionsByRole.TryGetValue(role.Id, out var permissions)
                ? permissions
                : Array.Empty<string>(),
            AssignedUsers = role.AssignedUsers
        })
        .ToArray();

    return Results.Ok(ApiResponse.Success(mappedRoles));
})
    .RequirePermission("roles.view")
    .WithTags("IAM")
    .WithName("ListRoles");

adminApi.MapPost("/roles", async (
    RoleCreateRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserId(currentUserService);
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var roleName = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(roleName) || roleName.Length > AppRole.MaxNameLength)
    {
        return ApiResponse.Failure("ValidationError", $"Role name is required and cannot exceed {AppRole.MaxNameLength} characters.");
    }

    if (SystemRoleCatalog.IsSystemRoleName(roleName))
    {
        return ApiResponse.Failure("Conflict", "System role names are reserved.", 409);
    }

    if (!AppRole.IsDescriptionWithinLimit(request.Description))
    {
        return ApiResponse.Failure("ValidationError", $"Role description cannot exceed {AppRole.MaxDescriptionLength} characters.");
    }

    var normalizedRoleName = roleName.ToUpperInvariant();
    var roleExists = await dbContext.AppRoles.AnyAsync(x => x.Name.ToUpper() == normalizedRoleName, cancellationToken);
    if (roleExists)
    {
        return ApiResponse.Failure("Conflict", "Role already exists.");
    }

    var roleEntity = new AppRole(
        id: Guid.NewGuid(),
        name: roleName,
        description: request.Description,
        isActive: true,
        createdAtUtc: DateTime.UtcNow);

    dbContext.AppRoles.Add(roleEntity);

    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Create,
        nameof(AppRole),
        roleEntity.Id.ToString(),
        before: null,
        after: new { roleEntity.Role, roleEntity.Name, roleEntity.Description, roleEntity.IsSystemRole },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/admin/roles/{roleEntity.Id}", ApiResponse.Success(new AdminRoleDto
    {
        Id = roleEntity.Id,
        Role = roleEntity.Name,
        Name = roleEntity.Name,
        Description = roleEntity.Description,
        IsSystemRole = roleEntity.IsSystemRole,
        IsActive = roleEntity.IsActive,
        Permissions = [],
        AssignedUsers = 0
    }));
})
    .RequirePermission("roles.manage")
    .WithTags("IAM")
    .WithName("CreateRole");

adminApi.MapPatch("/roles/{roleId:guid}", async (
    Guid roleId,
    RoleUpdateRequest request,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserId(currentUserService);
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var role = await dbContext.AppRoles
        .FirstOrDefaultAsync(x => x.Id == roleId, cancellationToken);
    if (role is null)
    {
        return ApiResponse.Failure("NotFound", "Role not found.", 404);
    }

    if (!role.CanModifyDefinition && (request.Name is not null || request.HasDescription || request.IsActive.HasValue))
    {
        return ApiResponse.Failure("Forbidden", "System role definitions are product-controlled.", 403);
    }

    var beforeRole = new { role.Name, role.Description, role.IsActive };

    if (request.HasDescription && !AppRole.IsDescriptionWithinLimit(request.Description))
    {
        return ApiResponse.Failure("ValidationError", $"Role description cannot exceed {AppRole.MaxDescriptionLength} characters.");
    }

    if (request.Name is not null)
    {
        var newName = request.Name.Trim();
        if (newName.Length == 0 || newName.Length > AppRole.MaxNameLength)
        {
            return ApiResponse.Failure("ValidationError", $"Role name is required and cannot exceed {AppRole.MaxNameLength} characters.");
        }

        if (role.IsSystemRole && !string.Equals(role.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse.Failure("Forbidden", "Cannot rename a system role.", 403);
        }

        var duplicateName = await dbContext.AppRoles.AnyAsync(
            existing => existing.Id != roleId && existing.Name.ToUpper() == newName.ToUpper(),
            cancellationToken);
        if (duplicateName)
        {
            return ApiResponse.Failure("Conflict", "Role name already exists.", 409);
        }
    }

    role.UpdateDetails(request.Name, request.HasDescription, request.Description, request.IsActive);
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Update,
        nameof(AppRole),
        role.Id.ToString(),
        beforeRole,
        after: new { role.Name, role.Description, role.IsActive },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ApiResponse.Success(new AdminRoleDto
    {
        Id = role.Id,
        Role = role.Name,
        Name = role.Name,
        Description = role.Description,
        IsSystemRole = role.IsSystemRole,
        IsActive = role.IsActive,
        AssignedUsers = await dbContext.AppUsers.CountAsync(user => user.Roles.Any(userRole => userRole.Id == role.Id), cancellationToken),
        Permissions = await (
                from rolePermission in dbContext.RolePermissions
                join permission in dbContext.Permissions
                    on rolePermission.PermissionId equals permission.Id
                where rolePermission.AppRoleId == roleId && !string.IsNullOrWhiteSpace(permission.Name)
                select permission.Name)
            .ToArrayAsync(cancellationToken)
    }));
})
    .RequirePermission("roles.manage")
    .WithTags("IAM")
    .WithName("UpdateRole");

adminApi.MapDelete("/roles/{roleId:guid}", async (
    Guid roleId,
    AppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserId(currentUserService);
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var role = await dbContext.AppRoles.FirstOrDefaultAsync(x => x.Id == roleId, cancellationToken);
    if (role is null)
    {
        return ApiResponse.Failure("NotFound", "Role not found.", 404);
    }

    if (role.IsSystemRole)
    {
        return ApiResponse.Failure("Forbidden", "Cannot delete system role.");
    }

    var assignedUsers = await dbContext.AppUsers.CountAsync(x => x.Roles.Any(r => r.Id == roleId), cancellationToken);
    if (assignedUsers > 0)
    {
        return ApiResponse.Failure("Conflict", "Cannot delete role while users are assigned.", 409);
    }

    var beforeRole = new { role.Role, role.Name, role.Description, role.IsActive };
    dbContext.AppRoles.Remove(role);

    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Delete,
        nameof(AppRole),
        role.Id.ToString(),
        beforeRole,
        after: null,
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
})
    .RequirePermission("roles.manage")
    .WithTags("IAM")
    .WithName("DeleteRole");

adminApi.MapPut("/roles/{roleId:guid}/permissions", async (
    Guid roleId,
    RolePermissionSetRequest request,
    AppDbContext dbContext,
    IPermissionService permissionService,
    IIamDelegationPolicy delegationPolicy,
    ICurrentUserService currentUserService,
    IAuditEngine auditEngine,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var actorUserId = currentUserId(currentUserService);
    if (actorUserId is null)
    {
        return ApiResponse.Failure("Unauthorized", "User context is required.");
    }

    var role = await dbContext.AppRoles
        .Include(role => role.Permissions)
        .FirstOrDefaultAsync(x => x.Id == roleId, cancellationToken);
    if (role is null)
    {
        return ApiResponse.Failure("NotFound", "Role not found.", 404);
    }

    if (!role.CanModifyDefinition)
    {
        return ApiResponse.Failure("Forbidden", "System role permissions are product-controlled.", 403);
    }

    var catalogPermissions = (await permissionService.GetCatalogAsync(cancellationToken))
        .Where(permission => permission.IsActive)
        .Select(permission => permission.Name)
        .ToArray();

    var requestedPermissionNames = NormalizePermissionNames(request.PermissionNames);
    var unknownPermissions = requestedPermissionNames
        .Where(permission => !catalogPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
        .ToArray();

    if (unknownPermissions.Length > 0)
    {
        return ApiResponse.Failure("ValidationError", "Unknown permissions: " + string.Join(", ", unknownPermissions));
    }

    var delegationDecision = await delegationPolicy.CanManageRolePermissionsAsync(actorUserId.Value, requestedPermissionNames, cancellationToken);
    if (!delegationDecision.Allowed)
    {
        return ApiResponse.Failure(delegationDecision.Code, delegationDecision.Message, 403);
    }

    var requestedSet = new HashSet<string>(requestedPermissionNames, StringComparer.OrdinalIgnoreCase);
    var permissionEntities = await dbContext.Permissions
        .AsNoTracking()
        .Where(permission => permission.IsActive && requestedSet.Contains(permission.Name))
        .ToListAsync(cancellationToken);

    var currentPermissionNames = await (
            from rolePermission in dbContext.RolePermissions.AsNoTracking()
            join permission in dbContext.Permissions.AsNoTracking()
                on rolePermission.PermissionId equals permission.Id
            where rolePermission.AppRoleId == roleId
            select permission.Name)
        .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);

    var requestedPermissionIds = permissionEntities
        .Select(permission => permission.Id)
        .ToHashSet();

    foreach (var entry in role.Permissions.ToArray())
    {
        if (!requestedPermissionIds.Contains(entry.PermissionId))
        {
            role.Permissions.Remove(entry);
        }
    }

    foreach (var permission in permissionEntities)
    {
        if (role.Permissions.Any(existing => existing.PermissionId == permission.Id))
        {
            continue;
        }

        role.Permissions.Add(new RolePermission(roleId, permission.Id));
    }

    dbContext.Entry(role).Property(nameof(AppRole.UpdatedAtUtc)).CurrentValue = DateTime.UtcNow;
    await auditEngine.RecordAsync(
        actorUserId.Value,
        AuditActionType.Update,
        nameof(AppRole),
        role.Id.ToString(),
        before: new { Permissions = currentPermissionNames.OrderBy(permission => permission).ToArray() },
        after: new { Permissions = requestedSet.OrderBy(permission => permission).ToArray() },
        requestMeta: AuditRequestMetadata.From(httpContext),
        cancellationToken: cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    var assignedUsers = await dbContext.AppUsers.CountAsync(user => user.Roles.Any(userRole => userRole.Id == role.Id), cancellationToken);
    return Results.Ok(ApiResponse.Success(new AdminRoleDto
    {
        Id = role.Id,
        Role = role.Name,
        Name = role.Name,
        Description = role.Description,
        IsSystemRole = role.IsSystemRole,
        IsActive = role.IsActive,
        AssignedUsers = assignedUsers,
        Permissions = requestedSet.OrderBy(permission => permission).ToArray()
    }));
})
    .RequirePermission("roles.manage")
    .WithTags("IAM")
    .WithName("SetRolePermissions");

    }

    private static Guid? currentUserId(ICurrentUserService currentUserService) => currentUserService.UserId;

    private static string[] NormalizeRoleInputs(string[]? roles) =>
        (roles ?? Array.Empty<string>())
            .Select(role => role?.Trim() ?? string.Empty)
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] NormalizePermissionNames(string[]? permissions) =>
        (permissions ?? Array.Empty<string>())
            .Select(permission => permission?.Trim() ?? string.Empty)
            .Where(permission => permission.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool TryParsePermissionEffect(string effect, out PermissionEffect permissionEffect) =>
        Enum.TryParse(effect?.Trim(), true, out permissionEffect);
}
