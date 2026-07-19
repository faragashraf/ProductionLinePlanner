using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Realtime;

public sealed record NotificationSummaryDto(
    Guid Id,
    string Title,
    string Message,
    NotificationStatus Status,
    bool IsRead,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

public sealed record PublishUserNotificationCommand(
    Guid NotificationId,
    Guid RecipientUserId,
    string Title,
    string Message,
    Guid? SenderUserId = null,
    Guid? RelatedWorkerId = null,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    DateTime? CreatedAtUtc = null);

public sealed record NotificationPublishResultDto(
    Guid NotificationId,
    bool Created,
    bool LiveDispatched);

public sealed record PublishCapabilityNotificationCommand(
    string Permission,
    NotificationSummaryDto Notification);
