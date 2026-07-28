using ProductionLinePlanner.Domain.Notifications;

namespace ProductionLinePlanner.Domain.Entities;

public sealed class NotificationPolicy
{
    public const int MaxEventKeyLength = 100;
    public const int MaxSoundKeyLength = 50;
    public const int MaxTitleTemplateLength = 200;
    public const int MaxMessageTemplateLength = 2000;

    private NotificationPolicy() { }

    public NotificationPolicy(
        Guid id,
        string eventKey,
        bool isEnabled,
        NotificationSeverity severity,
        bool isToastEnabled,
        bool isInboxEnabled,
        bool isSoundEnabled,
        string? soundKey,
        string titleTemplateAr,
        string messageTemplateAr,
        Guid? actorUserId = null,
        DateTime? createdAtUtc = null)
        : this(id, eventKey, isEnabled, severity, isToastEnabled, isInboxEnabled, isSoundEnabled,
            isBrowserEnabled: false, soundKey, titleTemplateAr, messageTemplateAr, actorUserId, createdAtUtc)
    {
    }

    public NotificationPolicy(
        Guid id,
        string eventKey,
        bool isEnabled,
        NotificationSeverity severity,
        bool isToastEnabled,
        bool isInboxEnabled,
        bool isSoundEnabled,
        bool isBrowserEnabled,
        string? soundKey,
        string titleTemplateAr,
        string messageTemplateAr,
        Guid? actorUserId = null,
        DateTime? createdAtUtc = null)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Id is required.", nameof(id)) : id;
        EventKey = NormalizeEventKey(eventKey);
        ApplySettings(isEnabled, severity, isToastEnabled, isInboxEnabled, isSoundEnabled, isBrowserEnabled, soundKey, titleTemplateAr, messageTemplateAr, actorUserId, createdAtUtc ?? DateTime.UtcNow);
        CreatedByUserId = actorUserId;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
    }

    public Guid Id { get; init; }
    public string EventKey { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public NotificationSeverity Severity { get; private set; }
    public bool IsToastEnabled { get; private set; }
    public bool IsInboxEnabled { get; private set; }
    public bool IsSoundEnabled { get; private set; }
    public bool IsBrowserEnabled { get; private set; }
    public string? SoundKey { get; private set; }
    public string TitleTemplateAr { get; private set; } = string.Empty;
    public string MessageTemplateAr { get; private set; } = string.Empty;
    public Guid? CreatedByUserId { get; private set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; private set; }
    public AppUser? UpdatedByUser { get; set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public List<NotificationPolicyRecipientRule> RecipientRules { get; } = [];

    public void Update(
        bool isEnabled,
        NotificationSeverity severity,
        bool isToastEnabled,
        bool isInboxEnabled,
        bool isSoundEnabled,
        bool isBrowserEnabled,
        string? soundKey,
        string titleTemplateAr,
        string messageTemplateAr,
        Guid actorUserId,
        DateTime? updatedAtUtc = null)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        }

        ApplySettings(isEnabled, severity, isToastEnabled, isInboxEnabled, isSoundEnabled, isBrowserEnabled, soundKey, titleTemplateAr, messageTemplateAr, actorUserId, updatedAtUtc ?? DateTime.UtcNow);
    }

    private void ApplySettings(
        bool isEnabled,
        NotificationSeverity severity,
        bool isToastEnabled,
        bool isInboxEnabled,
        bool isSoundEnabled,
        bool isBrowserEnabled,
        string? soundKey,
        string titleTemplateAr,
        string messageTemplateAr,
        Guid? actorUserId,
        DateTime timestamp)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity), "Notification severity is not supported.");
        }

        IsEnabled = isEnabled;
        Severity = severity;
        IsToastEnabled = isToastEnabled;
        IsInboxEnabled = isInboxEnabled;
        IsSoundEnabled = isSoundEnabled;
        IsBrowserEnabled = isBrowserEnabled;
        SoundKey = NormalizeSoundKey(isSoundEnabled, soundKey);
        TitleTemplateAr = NormalizeRequired(titleTemplateAr, MaxTitleTemplateLength, nameof(titleTemplateAr));
        MessageTemplateAr = NormalizeRequired(messageTemplateAr, MaxMessageTemplateLength, nameof(messageTemplateAr));
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = timestamp;
    }

    private static string NormalizeEventKey(string? value) =>
        NormalizeRequired(value, MaxEventKeyLength, nameof(value));

    private static string? NormalizeSoundKey(bool isSoundEnabled, string? value)
    {
        if (!isSoundEnabled)
        {
            return null;
        }

        var normalized = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
        if (!string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only the default sound key is supported.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeRequired(string? value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new ArgumentException($"A value between 1 and {maxLength} characters is required.", parameterName);
        }

        return normalized;
    }
}
