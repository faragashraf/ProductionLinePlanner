using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class AppRole
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;

    private AppRole() { }

    public AppRole(
        Guid id,
        UserRole role,
        string name,
        string? description = null,
        bool isSystemRole = false,
        bool isActive = true,
        DateTime? createdAtUtc = null)
        : this(id, name, description, isSystemRole, isActive, createdAtUtc, role) { }

    public AppRole(
        Guid id,
        string name,
        string? description = null,
        bool isActive = true,
        DateTime? createdAtUtc = null)
        : this(id, name, description, false, isActive, createdAtUtc, null) { }

    private AppRole(
        Guid id,
        string name,
        string? description,
        bool isSystemRole,
        bool isActive,
        DateTime? createdAtUtc,
        UserRole? role)
    {
        Id = id;
        Role = role;
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        IsSystemRole = isSystemRole;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public UserRole? Role { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystemRole { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public List<RolePermission> Permissions { get; } = [];

    public bool CanModifyDefinition => !IsSystemRole;

    public void UpdateDetails(string? name, bool updateDescription, string? description, bool? isActive)
    {
        if (!CanModifyDefinition && (name is not null || updateDescription || isActive.HasValue))
        {
            throw new InvalidOperationException("System role definitions are product-controlled.");
        }

        if (name is not null)
        {
            Name = NormalizeName(name);
        }

        if (updateDescription)
        {
            // A null or whitespace description is the explicit clear semantic.
            Description = NormalizeDescription(description);
        }
        if (isActive.HasValue)
        {
            IsActive = isActive.Value;
        }

        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkAsSystemRole()
    {
        if (Role is null)
        {
            throw new InvalidOperationException("Only catalog roles can be system roles.");
        }

        IsSystemRole = true;
    }

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Role name is required.", nameof(name));
        }

        if (normalized.Length > MaxNameLength)
        {
            throw new ArgumentException($"Role name cannot exceed {MaxNameLength} characters.", nameof(name));
        }

        return normalized;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : NormalizeNonEmptyDescription(description);

    public static bool IsDescriptionWithinLimit(string? description) =>
        (description?.Trim().Length ?? 0) <= MaxDescriptionLength;

    private static string NormalizeNonEmptyDescription(string description)
    {
        var normalized = description.Trim();
        if (normalized.Length > MaxDescriptionLength)
        {
            throw new ArgumentException($"Role description cannot exceed {MaxDescriptionLength} characters.", nameof(description));
        }

        return normalized;
    }
}
