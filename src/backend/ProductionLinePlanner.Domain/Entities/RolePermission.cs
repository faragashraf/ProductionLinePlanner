using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class RolePermission
{
    private RolePermission() { }

    public RolePermission(Guid appRoleId, Guid permissionId, DateTime? createdAtUtc = null)
    {
        if (appRoleId == Guid.Empty)
        {
            throw new ArgumentException("AppRoleId is required.", nameof(appRoleId));
        }

        if (permissionId == Guid.Empty)
        {
            throw new ArgumentException("PermissionId is required.", nameof(permissionId));
        }

        AppRoleId = appRoleId;
        PermissionId = permissionId;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid AppRoleId { get; init; }
    public Guid PermissionId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; private set; }

    public AppRole? AppRole { get; set; }
    public Permission? Permission { get; set; }
}
