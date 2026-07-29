using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Notifications;

namespace ProductionLinePlanner.Application.DTOs;

public sealed class NotificationDto
{
    public Guid Id { get; init; }
    public Guid RecipientUserId { get; init; }
    public Guid? SenderUserId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public NotificationStatus Status { get; init; }
    public bool IsRead { get; init; }
    public Guid? RelatedWorkerId { get; init; }
    public string? RelatedEntityType { get; init; }
    public Guid? RelatedEntityId { get; init; }
    public string? EventKey { get; init; }
    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Information;
    public bool IsToastEnabled { get; init; } = true;
    public bool IsSoundEnabled { get; init; }
    public bool IsBrowserEnabled { get; init; }
    public string? NavigationUrl { get; init; }
    public string? MetadataJson { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ReadAtUtc { get; init; }
}
