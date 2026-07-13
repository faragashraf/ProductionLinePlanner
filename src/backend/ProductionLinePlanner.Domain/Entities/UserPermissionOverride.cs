using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class UserPermissionOverride
{
    private UserPermissionOverride() { }

    public UserPermissionOverride(
        Guid appUserId,
        Guid permissionId,
        PermissionEffect effect,
        Guid? createdByUserId = null,
        DateTime? createdAtUtc = null)
    {
        if (appUserId == Guid.Empty)
        {
            throw new ArgumentException("AppUserId is required.", nameof(appUserId));
        }

        if (permissionId == Guid.Empty)
        {
            throw new ArgumentException("PermissionId is required.", nameof(permissionId));
        }

        AppUserId = appUserId;
        PermissionId = permissionId;
        Effect = effect;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid AppUserId { get; init; }
    public Guid PermissionId { get; init; }
    public PermissionEffect Effect { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; private set; }

    public AppUser? AppUser { get; set; }
    public Permission? Permission { get; set; }

    public void UpdateEffect(PermissionEffect effect, Guid? updatedByUserId = null, DateTime? updatedAtUtc = null)
    {
        Effect = effect;
        CreatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow;
    }
}
