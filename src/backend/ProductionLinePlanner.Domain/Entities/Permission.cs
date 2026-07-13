namespace ProductionLinePlanner.Domain.Entities;

public class Permission
{
    private Permission() { }

    public Permission(
        Guid id,
        string name,
        string capability,
        string? descriptionAr = null,
        string? descriptionEn = null,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Permission name is required.", nameof(name));

        if (string.IsNullOrWhiteSpace(capability))
            throw new ArgumentException("Permission capability is required.", nameof(capability));

        Id = id;
        Name = name.Trim();
        Capability = capability.Trim();
        DescriptionAr = string.IsNullOrWhiteSpace(descriptionAr) ? null : descriptionAr.Trim();
        DescriptionEn = string.IsNullOrWhiteSpace(descriptionEn) ? null : descriptionEn.Trim();
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public string Name { get; private set; } = string.Empty;
    public string Capability { get; private set; } = string.Empty;
    public string? DescriptionAr { get; private set; }
    public string? DescriptionEn { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public List<RolePermission> RolePermissions { get; } = [];
    public List<UserPermissionOverride> UserPermissionOverrides { get; } = [];

    public void UpdateActivity(bool isActive, DateTime? updatedAtUtc = null)
    {
        IsActive = isActive;
        UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow;
    }
}
