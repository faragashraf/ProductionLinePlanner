using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Notifications;

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
    DateTime? ReadAtUtc,
    string? EventKey = null,
    NotificationSeverity Severity = NotificationSeverity.Information);

public sealed record PublishUserNotificationCommand(
    Guid NotificationId,
    Guid RecipientUserId,
    string Title,
    string Message,
    Guid? SenderUserId = null,
    Guid? RelatedWorkerId = null,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    DateTime? CreatedAtUtc = null,
    string? EventKey = null,
    NotificationSeverity? Severity = null);

public sealed record NotificationPublishResultDto(
    Guid NotificationId,
    bool Created,
    bool LiveDispatched);

public sealed record PublishCapabilityNotificationCommand(
    string Permission,
    NotificationSummaryDto Notification);
