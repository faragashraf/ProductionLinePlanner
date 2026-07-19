using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class NotificationEngine : INotificationEngine
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditEngine _auditEngine;

    public NotificationEngine(AppDbContext dbContext, IAuditEngine auditEngine)
    {
        _dbContext = dbContext;
        _auditEngine = auditEngine;
    }

    public async Task<Result<PagedResult<NotificationDto>>> GetNotificationsAsync(
        Guid recipientUserId,
        bool? isRead,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (recipientUserId == Guid.Empty)
        {
            return Result<PagedResult<NotificationDto>>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (page < 1 || pageSize < 1 || pageSize > 200)
        {
            return Result<PagedResult<NotificationDto>>.Failure(new Error("ValidationError", "page and pageSize must be positive, pageSize max 200."));
        }

        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == recipientUserId);

        if (isRead.HasValue)
        {
            query = query.Where(x => x.IsRead == isRead.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var notifications = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new NotificationDto
            {
                Id = x.Id,
                RecipientUserId = x.RecipientUserId,
                SenderUserId = x.SenderUserId,
                Title = x.Title,
                Message = x.Message,
                Status = x.Status,
                IsRead = x.IsRead,
                RelatedWorkerId = x.RelatedWorkerId,
                RelatedEntityType = x.RelatedEntityType,
                RelatedEntityId = x.RelatedEntityId,
                CreatedAtUtc = x.CreatedAtUtc,
                ReadAtUtc = x.ReadAtUtc
            })
            .ToArrayAsync(cancellationToken);

        return Result<PagedResult<NotificationDto>>.Success(PagedResult<NotificationDto>.Success(notifications, page, pageSize, totalCount));
    }

    public async Task<Result<int>> GetUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        if (recipientUserId == Guid.Empty)
        {
            return Result<int>.Failure(new Error("Unauthorized", "User context is required."));
        }

        var unreadCount = await _dbContext.Notifications
            .AsNoTracking()
            .CountAsync(x => x.RecipientUserId == recipientUserId && !x.IsRead, cancellationToken);

        return Result<int>.Success(unreadCount);
    }

    public async Task<Result<NotificationDto>> MarkNotificationReadAsync(
        Guid recipientUserId,
        Guid notificationId,
        DateTime? readAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (recipientUserId == Guid.Empty)
        {
            return Result<NotificationDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(x => x.Id == notificationId && x.RecipientUserId == recipientUserId, cancellationToken);

        if (notification is null)
        {
            return Result<NotificationDto>.Failure(new Error("NotFound", "Notification not found."));
        }

        var before = new
        {
            notification.Id,
            notification.IsRead,
            notification.ReadAtUtc
        };

        if (!notification.IsRead)
        {
            notification.MarkAsRead(readAtUtc ?? DateTime.UtcNow);
            await _auditEngine.RecordAsync(
                recipientUserId,
                AuditActionType.Update,
                nameof(Notification),
                notification.Id.ToString(),
                before,
                new
                {
                    notification.Id,
                    notification.IsRead,
                    notification.ReadAtUtc
                },
                cancellationToken: cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result<NotificationDto>.Success(new NotificationDto
        {
            Id = notification.Id,
            RecipientUserId = notification.RecipientUserId,
            SenderUserId = notification.SenderUserId,
            Title = notification.Title,
            Message = notification.Message,
            Status = notification.Status,
            IsRead = notification.IsRead,
            RelatedWorkerId = notification.RelatedWorkerId,
            RelatedEntityType = notification.RelatedEntityType,
            RelatedEntityId = notification.RelatedEntityId,
            CreatedAtUtc = notification.CreatedAtUtc,
            ReadAtUtc = notification.ReadAtUtc
        });
    }

    public async Task<Result<int>> MarkAllAsReadAsync(
        Guid recipientUserId,
        DateTime? beforeDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (recipientUserId == Guid.Empty)
        {
            return Result<int>.Failure(new Error("Unauthorized", "User context is required."));
        }

        var query = _dbContext.Notifications
            .Where(x => x.RecipientUserId == recipientUserId && !x.IsRead);

        if (beforeDateUtc.HasValue)
        {
            query = query.Where(x => x.CreatedAtUtc <= beforeDateUtc.Value);
        }

        var now = DateTime.UtcNow;
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var updatedCount = await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.Status, NotificationStatus.Read)
                .SetProperty(x => x.ReadAtUtc, now), cancellationToken);

            await _auditEngine.RecordAsync(
                recipientUserId,
                AuditActionType.Update,
                nameof(Notification),
                "read-all",
                new { recipientUserId, beforeDateUtc },
                new { recipientUserId, updatedCount },
                cancellationToken: cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Result<int>.Success(updatedCount);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }
}
