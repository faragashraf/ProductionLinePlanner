using Microsoft.AspNetCore.Authorization;

namespace ProductionLinePlanner.Api.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string PermissionName { get; } = permission;
}
