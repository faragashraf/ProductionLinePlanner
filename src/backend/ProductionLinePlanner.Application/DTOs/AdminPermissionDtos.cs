namespace ProductionLinePlanner.Application.DTOs;

public sealed class AdminUserListItemDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string[] Roles { get; init; } = [];
}

public sealed class PermissionSourceDto
{
    public string Permission { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}

public sealed class EffectivePermissionItemDto
{
    public string Permission { get; init; } = string.Empty;
    public string[] Sources { get; init; } = [];
    public bool Granted { get; init; }
    public string? DescriptionAr { get; init; }
    public string? DescriptionEn { get; init; }
    public bool IsCritical { get; init; }
}

public sealed class AdminUserAuthorizationDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime PermissionsVersion { get; init; }
    public string[] Roles { get; init; } = [];
    public string[] DirectGrants { get; init; } = [];
    public string[] DirectDenies { get; init; } = [];
    public EffectivePermissionItemDto[] EffectivePermissions { get; init; } = [];
}

public sealed class PermissionCatalogItemDto
{
    public string Name { get; init; } = string.Empty;
    public string Capability { get; init; } = string.Empty;
    public string? DescriptionAr { get; init; }
    public string? DescriptionEn { get; init; }
    public bool IsCritical { get; init; }
    public bool IsActive { get; init; }
}

public sealed class PermissionCatalogGroupDto
{
    public string Capability { get; init; } = string.Empty;
    public PermissionCatalogItemDto[] Permissions { get; init; } = [];
}

public sealed class AdminRoleDto
{
    public Guid Id { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsSystemRole { get; init; }
    public bool IsActive { get; init; }
    public int AssignedUsers { get; init; }
    public string[] Permissions { get; init; } = [];
}

public sealed class AdminRoleWithAssignedUsersDto
{
    public Guid Id { get; init; }
    public string Role { get; init; } = string.Empty;
    public int AssignedUsers { get; init; }
}
