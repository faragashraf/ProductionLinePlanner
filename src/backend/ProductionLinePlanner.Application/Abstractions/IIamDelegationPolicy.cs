using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IIamDelegationPolicy
{
    Task<DelegationDecision> CanAssignRoleAsync(Guid actorUserId, Guid targetUserId, AppRole role, CancellationToken cancellationToken = default);
    Task<DelegationDecision> CanChangeDirectPermissionAsync(Guid actorUserId, Guid targetUserId, string permissionName, PermissionEffect effect, bool isRemoval, CancellationToken cancellationToken = default);
    Task<DelegationDecision> CanManageRolePermissionsAsync(Guid actorUserId, IEnumerable<string> permissionNames, CancellationToken cancellationToken = default);
}

public sealed record DelegationDecision(bool Allowed, string Code, string Message)
{
    public static DelegationDecision Permit() => new(true, string.Empty, string.Empty);
    public static DelegationDecision Deny(string code, string message) => new(false, code, message);
}
