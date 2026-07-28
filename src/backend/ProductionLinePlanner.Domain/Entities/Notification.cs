using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Notifications;

namespace ProductionLinePlanner.Domain.Entities;

public class Notification
{
    private Notification() { }

    public Notification(
        Guid id,
        Guid recipientUserId,
        string title,
        string message,
        Guid? senderUserId = null,
        Guid? relatedWorkerId = null,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        NotificationStatus status = NotificationStatus.Unread,
        DateTime? createdAtUtc = null,
        string? eventKey = null,
        NotificationSeverity? severity = null,
        bool isToastEnabled = true,
        bool isSoundEnabled = false,
        bool isBrowserEnabled = false,
        string? navigationUrl = null,
        string? metadataJson = null,
        string? correlationKey = null)
    {
        if (recipientUserId == Guid.Empty)
            throw new ArgumentException("RecipientUserId is required.", nameof(recipientUserId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message is required.", nameof(message));

        Id = id;
        RecipientUserId = recipientUserId;
        SenderUserId = senderUserId;
        Title = title.Trim();
        Message = message.Trim();
        Status = status;
        RelatedWorkerId = relatedWorkerId;
        RelatedEntityType = string.IsNullOrWhiteSpace(relatedEntityType) ? null : relatedEntityType.Trim();
        RelatedEntityId = relatedEntityId;
        EventKey = string.IsNullOrWhiteSpace(eventKey) ? null : eventKey.Trim();
        Severity = severity;
        IsToastEnabled = isToastEnabled;
        IsSoundEnabled = isSoundEnabled;
        IsBrowserEnabled = isBrowserEnabled;
        NavigationUrl = NormalizeOptional(navigationUrl, 300, nameof(navigationUrl));
        MetadataJson = NormalizeOptional(metadataJson, 4000, nameof(metadataJson));
        CorrelationKey = NormalizeOptional(correlationKey, 200, nameof(correlationKey));
        IsRead = status is NotificationStatus.Read;
        ReadAtUtc = IsRead ? createdAtUtc ?? DateTime.UtcNow : null;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid RecipientUserId { get; private set; }
    public AppUser? RecipientUser { get; set; }
    public Guid? SenderUserId { get; private set; }
    public AppUser? SenderUser { get; set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public NotificationStatus Status { get; private set; }
    public Guid? RelatedWorkerId { get; private set; }
    public string? RelatedEntityType { get; private set; }
    public Guid? RelatedEntityId { get; private set; }
    public string? EventKey { get; private set; }
    public NotificationSeverity? Severity { get; private set; }
    public bool IsToastEnabled { get; private set; }
    public bool IsSoundEnabled { get; private set; }
    public bool IsBrowserEnabled { get; private set; }
    public string? NavigationUrl { get; private set; }
    public string? MetadataJson { get; private set; }
    public string? CorrelationKey { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void MarkAsRead(DateTime? readAtUtc = null)
    {
        IsRead = true;
        Status = NotificationStatus.Read;
        ReadAtUtc = readAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = readAtUtc ?? DateTime.UtcNow;
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"{parameterName} cannot exceed {maximumLength} characters.", parameterName);
        return normalized;
    }
}
