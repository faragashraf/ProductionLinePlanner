using ProductionLinePlanner.Domain.Notifications;

namespace ProductionLinePlanner.Domain.Entities;

public sealed class NotificationPolicyRecipientRule
{
    public const int MaxKeyLength = 100;

    private NotificationPolicyRecipientRule() { }

    public NotificationPolicyRecipientRule(
        Guid id,
        Guid notificationPolicyId,
        NotificationRecipientKind recipientKind,
        Guid? userId,
        Guid? roleId,
        string? permissionKey,
        string? capabilityKey,
        bool isExcludeActor,
        int sortOrder,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (notificationPolicyId == Guid.Empty) throw new ArgumentException("NotificationPolicyId is required.", nameof(notificationPolicyId));
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));

        Id = id;
        NotificationPolicyId = notificationPolicyId;
        RecipientKind = recipientKind;
        UserId = userId;
        RoleId = roleId;
        PermissionKey = NormalizeOptionalKey(permissionKey, nameof(permissionKey));
        CapabilityKey = NormalizeOptionalKey(capabilityKey, nameof(capabilityKey));
        IsExcludeActor = isExcludeActor;
        SortOrder = sortOrder;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid NotificationPolicyId { get; private set; }
    public NotificationPolicy? NotificationPolicy { get; set; }
    public NotificationRecipientKind RecipientKind { get; private set; }
    public Guid? UserId { get; private set; }
    public AppUser? User { get; set; }
    public Guid? RoleId { get; private set; }
    public AppRole? Role { get; set; }
    public string? PermissionKey { get; private set; }
    public string? CapabilityKey { get; private set; }
    public bool IsExcludeActor { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private static string? NormalizeOptionalKey(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > MaxKeyLength)
        {
            throw new ArgumentException($"The value cannot exceed {MaxKeyLength} characters.", parameterName);
        }

        return normalized;
    }
}
