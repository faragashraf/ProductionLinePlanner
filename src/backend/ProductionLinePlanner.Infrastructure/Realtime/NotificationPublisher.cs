using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Notifications;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Realtime;

public sealed class NotificationPublisher(
    AppDbContext dbContext,
    INotificationLiveDispatcher liveDispatcher,
    ILogger<NotificationPublisher> logger) : INotificationPublisher
{
    public async Task<Result<NotificationPublishResultDto>> PublishToUserAsync(
        PublishUserNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(command);
        if (validationError is not null)
        {
            return Result<NotificationPublishResultDto>.Failure(validationError);
        }

        var title = command.Title.Trim();
        var message = command.Message.Trim();
        var relatedEntityType = string.IsNullOrWhiteSpace(command.RelatedEntityType)
            ? null
            : command.RelatedEntityType.Trim();
        var existing = await dbContext.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(notification => notification.Id == command.NotificationId, cancellationToken);

        if (existing is not null)
        {
            return Matches(existing, command, title, message, relatedEntityType)
                ? Result<NotificationPublishResultDto>.Success(new(command.NotificationId, false, false))
                : Result<NotificationPublishResultDto>.Failure(new Error(
                    "NotificationIdConflict",
                    "The notification identifier is already associated with a different notification."));
        }

        var recipientExists = await dbContext.AppUsers
            .AsNoTracking()
            .AnyAsync(user => user.Id == command.RecipientUserId, cancellationToken);
        if (!recipientExists)
        {
            return Result<NotificationPublishResultDto>.Failure(new Error(
                "RecipientNotFound",
                "The notification recipient does not exist."));
        }

        var notification = new Notification(
            command.NotificationId,
            command.RecipientUserId,
            title,
            message,
            command.SenderUserId,
            command.RelatedWorkerId,
            relatedEntityType,
            command.RelatedEntityId,
            NotificationStatus.Unread,
            command.CreatedAtUtc,
            command.EventKey,
            command.Severity);

        dbContext.Notifications.Add(notification);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The primary key is the durable idempotency boundary. Another
            // publisher can win between the read above and this insert, so
            // resolve that race from the persisted row instead of relying on
            // an in-memory lock that would fail across servers.
            dbContext.Entry(notification).State = EntityState.Detached;
            var concurrentlyPersisted = await dbContext.Notifications
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    persisted => persisted.Id == command.NotificationId,
                    cancellationToken);

            if (concurrentlyPersisted is null)
            {
                throw;
            }

            return Matches(concurrentlyPersisted, command, title, message, relatedEntityType)
                ? Result<NotificationPublishResultDto>.Success(new(command.NotificationId, false, false))
                : Result<NotificationPublishResultDto>.Failure(new Error(
                    "NotificationIdConflict",
                    "The notification identifier is already associated with a different notification."));
        }

        var summary = ToSummary(notification);
        var liveDispatched = false;
        try
        {
            await liveDispatcher.SendToUserAsync(command.RecipientUserId, summary, cancellationToken);
            liveDispatched = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Live dispatch failed after notification {NotificationId} was persisted.",
                command.NotificationId);
        }

        return Result<NotificationPublishResultDto>.Success(new(command.NotificationId, true, liveDispatched));
    }

    public async Task<Result> PublishEphemeralToCapabilityAsync(
        PublishCapabilityNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!PermissionCatalog.IsKnown(command.Permission))
        {
            return Result.Failure(new Error("UnknownPermission", "A known permission is required."));
        }

        try
        {
            await liveDispatcher.SendToCapabilityAsync(command.Permission, command.Notification, cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Ephemeral capability notification dispatch failed for notification {NotificationId}.",
                command.Notification.Id);
            return Result.Failure(new Error("RealtimeDispatchFailed", "The live notification could not be dispatched."));
        }
    }

    private static Error? Validate(PublishUserNotificationCommand command)
    {
        if (command.NotificationId == Guid.Empty)
            return new Error("ValidationError", "NotificationId is required and acts as the idempotency key.");
        if (command.RecipientUserId == Guid.Empty)
            return new Error("ValidationError", "RecipientUserId is required.");
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Trim().Length > 200)
            return new Error("ValidationError", "Title is required and cannot exceed 200 characters.");
        if (string.IsNullOrWhiteSpace(command.Message) || command.Message.Trim().Length > 2000)
            return new Error("ValidationError", "Message is required and cannot exceed 2000 characters.");
        if (command.RelatedEntityType?.Trim().Length > 100)
            return new Error("ValidationError", "RelatedEntityType cannot exceed 100 characters.");
        if (command.CreatedAtUtc is { Kind: not DateTimeKind.Utc })
            return new Error("ValidationError", "CreatedAtUtc must be UTC when provided.");

        return null;
    }

    private static bool Matches(
        Notification notification,
        PublishUserNotificationCommand command,
        string title,
        string message,
        string? relatedEntityType) =>
        notification.RecipientUserId == command.RecipientUserId &&
        notification.SenderUserId == command.SenderUserId &&
        notification.Title == title &&
        notification.Message == message &&
        notification.RelatedWorkerId == command.RelatedWorkerId &&
        notification.RelatedEntityType == relatedEntityType &&
        notification.RelatedEntityId == command.RelatedEntityId &&
        notification.EventKey == command.EventKey &&
        notification.Severity == command.Severity &&
        (command.CreatedAtUtc is null || notification.CreatedAtUtc == command.CreatedAtUtc.Value);

    private static NotificationSummaryDto ToSummary(Notification notification) => new(
        notification.Id,
        notification.Title,
        notification.Message,
        notification.Status,
        notification.IsRead,
        notification.RelatedEntityType,
        notification.RelatedEntityId,
        notification.CreatedAtUtc,
        notification.ReadAtUtc,
        notification.EventKey,
        notification.Severity ?? NotificationSeverity.Information);
}
