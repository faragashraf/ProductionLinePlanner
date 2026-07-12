namespace ProductionLinePlanner.Application.Requests;

public sealed class UserRoleAssignmentsRequest
{
    public string[] Roles { get; init; } = [];
}

public sealed class UserStatusRequest
{
    public bool IsActive { get; init; }
}

public sealed class UserPermissionOverrideRequest
{
    public string Permission { get; init; } = string.Empty;
    public string Effect { get; init; } = string.Empty;
}

public sealed class RoleCreateRequest
{
    public string Role { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed class RoleUpdateRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class RolePermissionSetRequest
{
    public string[] PermissionNames { get; init; } = [];
}
