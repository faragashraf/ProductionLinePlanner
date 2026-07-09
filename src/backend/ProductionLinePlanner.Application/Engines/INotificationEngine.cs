using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface INotificationEngine
{
    Task<Result<PagedResult<NotificationDto>>> GetNotificationsAsync(
        Guid recipientUserId,
        bool? isRead,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<int>> GetUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationDto>> MarkNotificationReadAsync(
        Guid recipientUserId,
        Guid notificationId,
        DateTime? readAtUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<int>> MarkAllAsReadAsync(
        Guid recipientUserId,
        DateTime? beforeDateUtc = null,
        CancellationToken cancellationToken = default);
}

