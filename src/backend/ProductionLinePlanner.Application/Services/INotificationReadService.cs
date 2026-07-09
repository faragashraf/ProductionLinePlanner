using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Services;

public interface INotificationReadService
{
    Task<Result<PagedResult<NotificationDto>>> GetNotificationsAsync(
        Guid recipientUserId,
        bool? isRead = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<int>> GetUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationDto>> MarkNotificationReadAsync(
        Guid notificationId,
        MarkNotificationReadRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> MarkAllAsReadAsync(
        Guid recipientUserId,
        DateTime? beforeDateUtc = null,
        CancellationToken cancellationToken = default);
}
