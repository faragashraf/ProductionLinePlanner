using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Notifications;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Notifications;

public sealed class NotificationRecipientResolver(
    AppDbContext dbContext,
    IPermissionService permissionService) : INotificationRecipientResolver
{
    public async Task<Result<IReadOnlyCollection<Guid>>> ResolveAsync(
        IReadOnlyCollection<NotificationRecipientRule> rules,
        NotificationRecipientContext context,
        CancellationToken cancellationToken = default)
    {
        if (rules is null)
        {
            return Result<IReadOnlyCollection<Guid>>.Failure(new Error(
                "RecipientRulesRequired",
                "Recipient rules are required."));
        }

        var directUserIds = new HashSet<Guid>();
        var roleIds = new HashSet<Guid>();
        var requestedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestedCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var includeCreator = false;
        var excludeActor = false;

        foreach (var rule in rules)
        {
            switch (rule.Kind)
            {
                case NotificationRecipientKind.User:
                    if (rule.SubjectId is not Guid userId || userId == Guid.Empty)
                    {
                        return InvalidRule("User recipient rules require a user identifier.");
                    }
                    directUserIds.Add(userId);
                    break;

                case NotificationRecipientKind.Role:
                    if (rule.SubjectId is not Guid roleId || roleId == Guid.Empty)
                    {
                        return InvalidRule("Role recipient rules require a role identifier.");
                    }
                    roleIds.Add(roleId);
                    break;

                case NotificationRecipientKind.Permission:
                    var permission = rule.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(permission) || !PermissionCatalog.IsKnown(permission))
                    {
                        return Result<IReadOnlyCollection<Guid>>.Failure(new Error(
                            "UnknownPermission",
                            "Permission recipient rules require a known product permission."));
                    }
                    requestedPermissions.Add(permission!);
                    break;

                case NotificationRecipientKind.CapabilityGroup:
                    var capability = rule.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(capability) || PermissionCatalog.ByCapability(capability).Count == 0)
                    {
                        return Result<IReadOnlyCollection<Guid>>.Failure(new Error(
                            "UnknownCapability",
                            "Capability recipient rules require a known product capability."));
                    }
                    requestedCapabilities.Add(capability);
                    break;

                case NotificationRecipientKind.Creator:
                    includeCreator = true;
                    break;

                case NotificationRecipientKind.ExcludeActor:
                    excludeActor = true;
                    break;

                default:
                    return InvalidRule("The recipient rule kind is not supported.");
            }
        }

        var recipients = new HashSet<Guid>();
        if (directUserIds.Count > 0)
        {
            recipients.UnionWith(await dbContext.AppUsers
                .AsNoTracking()
                .Where(user => user.IsActive && directUserIds.Contains(user.Id))
                .Select(user => user.Id)
                .ToArrayAsync(cancellationToken));
        }

        if (roleIds.Count > 0)
        {
            recipients.UnionWith(await dbContext.AppUsers
                .AsNoTracking()
                .Where(user => user.IsActive && user.Roles.Any(role => role.IsActive && roleIds.Contains(role.Id)))
                .Select(user => user.Id)
                .ToArrayAsync(cancellationToken));
        }

        if (includeCreator && context.CreatorUserId is Guid creatorUserId && creatorUserId != Guid.Empty)
        {
            var creatorIsActive = await dbContext.AppUsers
                .AsNoTracking()
                .AnyAsync(user => user.Id == creatorUserId && user.IsActive, cancellationToken);
            if (creatorIsActive)
            {
                recipients.Add(creatorUserId);
            }
        }

        if (requestedPermissions.Count > 0 || requestedCapabilities.Count > 0)
        {
            var activeUserIds = await dbContext.AppUsers
                .AsNoTracking()
                .Where(user => user.IsActive)
                .Select(user => user.Id)
                .ToArrayAsync(cancellationToken);
            var capabilityByPermission = PermissionCatalog.All.ToDictionary(
                entry => entry.Name,
                entry => entry.Capability,
                StringComparer.OrdinalIgnoreCase);

            foreach (var userId in activeUserIds)
            {
                var effectivePermissions = await permissionService.GetEffectivePermissionsAsync(userId, cancellationToken);
                if (effectivePermissions.Any(requestedPermissions.Contains) ||
                    effectivePermissions.Any(permission =>
                        capabilityByPermission.TryGetValue(permission, out var capability) &&
                        requestedCapabilities.Contains(capability)))
                {
                    recipients.Add(userId);
                }
            }
        }

        if (excludeActor && context.ActorUserId is Guid actorUserId)
        {
            recipients.Remove(actorUserId);
        }

        return Result<IReadOnlyCollection<Guid>>.Success(
            recipients.OrderBy(userId => userId).ToArray());
    }

    private static Result<IReadOnlyCollection<Guid>> InvalidRule(string message) =>
        Result<IReadOnlyCollection<Guid>>.Failure(new Error("InvalidRecipientRule", message));
}
