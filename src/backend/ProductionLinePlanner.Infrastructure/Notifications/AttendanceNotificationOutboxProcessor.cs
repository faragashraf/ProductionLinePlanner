using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Notifications;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Notifications;

public sealed class AttendanceNotificationOutboxProcessor(
    AppDbContext dbContext,
    INotificationPolicyEngine policyEngine,
    INotificationPublisher notificationPublisher,
    ICairoTimeZoneProvider cairoTimeZoneProvider,
    ILogger<AttendanceNotificationOutboxProcessor> logger) : IAttendanceNotificationOutboxProcessor
{
    private static readonly CultureInfo ArabicCulture = CultureInfo.GetCultureInfo("ar-EG");

    public async Task<Result<int>> ProcessPendingAsync(
        int batchSize = 50,
        CancellationToken cancellationToken = default)
    {
        var events = await dbContext.AttendanceNotificationEvents
            .Where(item => item.ProcessedAtUtc == null)
            .OrderBy(item => item.CreatedAtUtc)
            .Take(Math.Clamp(batchSize, 1, 500))
            .ToArrayAsync(cancellationToken);
        var processed = 0;

        foreach (var attendanceEvent in events)
        {
            var result = await ProcessOneAsync(attendanceEvent, cancellationToken);
            if (result.IsSuccess)
            {
                attendanceEvent.MarkProcessed(DateTime.UtcNow);
                processed++;
            }
            else
            {
                attendanceEvent.MarkFailed(result.Error?.Code ?? "AttendanceNotificationFailed", DateTime.UtcNow);
                logger.LogWarning(
                    "Attendance notification outbox processing failed. eventId={EventId}, idempotencyKey={IdempotencyKey}, errorCode={ErrorCode}, errorMessage={ErrorMessage}",
                    attendanceEvent.Id,
                    attendanceEvent.IdempotencyKey,
                    result.Error?.Code,
                    result.Error?.Message);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result<int>.Success(processed);
    }

    private async Task<Result> ProcessOneAsync(
        AttendanceNotificationEvent attendanceEvent,
        CancellationToken cancellationToken)
    {
        if (attendanceEvent.AttendanceTimeUtc.Kind == DateTimeKind.Local)
            return Result.Failure(new Error("ValidationError", "AttendanceTimeUtc cannot be local."));
        if (attendanceEvent.CreatedAtUtc.Kind == DateTimeKind.Local)
            return Result.Failure(new Error("ValidationError", "CreatedAtUtc cannot be local."));

        // SQL Server datetime2 preserves the UTC clock value but not DateTime.Kind.
        var attendanceTimeUtc = NormalizeStoredUtc(attendanceEvent.AttendanceTimeUtc);
        var createdAtUtc = NormalizeStoredUtc(attendanceEvent.CreatedAtUtc);
        var eventKey = attendanceEvent.AttendanceType == WorkerAttendanceNotificationType.CheckIn
            ? NotificationEventKeys.WorkerCheckedIn
            : NotificationEventKeys.WorkerCheckedOut;
        var policy = await dbContext.NotificationPolicies
            .AsNoTracking()
            .Include(item => item.RecipientRules)
            .SingleOrDefaultAsync(item => item.EventKey == eventKey, cancellationToken);
        if (policy is null)
            return Result.Failure(new Error("NotificationPolicyNotFound", "Attendance notification policy was not found."));

        if (!policy.IsEnabled)
            return Result.Success();

        var assignment = await ResolvePermanentAssignmentAsync(attendanceEvent, attendanceTimeUtc, cancellationToken);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(attendanceTimeUtc, cairoTimeZoneProvider.TimeZone);
        var assignmentText = assignment is null
            ? "العامل غير مسكن حاليًا."
            : $"التسكين الحالي: {StageLabel(assignment.StageName)}، {assignment.LineName}.";
        var definition = new NotificationPolicyDefinition(
            policy.EventKey,
            policy.IsEnabled,
            policy.Severity,
            new NotificationSoundPolicy(policy.IsSoundEnabled),
            new NotificationToastPolicy(policy.IsToastEnabled),
            new NotificationInboxPolicy(policy.IsInboxEnabled),
            new NotificationBrowserPolicy(policy.IsBrowserEnabled),
            policy.TitleTemplateAr,
            policy.MessageTemplateAr,
            policy.RecipientRules
                .Where(rule => rule.IsActive)
                .OrderBy(rule => rule.SortOrder)
                .Select(ToRecipientRule)
                .ToArray());
        var decision = await policyEngine.EvaluateAsync(
            definition,
            new NotificationEventContext(
                eventKey,
                new Dictionary<string, string>
                {
                    ["WorkerName"] = attendanceEvent.WorkerName,
                    ["EmployeeCode"] = attendanceEvent.EmployeeCode,
                    ["AttendanceTime"] = localTime.ToString("hh:mm tt", ArabicCulture),
                    ["AssignmentText"] = assignmentText
                }),
            cancellationToken);
        if (decision.IsFailure)
            return Result.Failure(decision.Error!);

        var evaluated = decision.Value!;
        if (!evaluated.ShouldDispatch)
            return Result.Success();

        var metadataJson = JsonSerializer.Serialize(new
        {
            navigationAction = NotificationNavigationActions.OpenDailyAttendance,
            navigationPayload = new
            {
                workerId = attendanceEvent.WorkerId,
                productionDate = DateOnly.FromDateTime(localTime)
            },
            workerId = attendanceEvent.WorkerId,
            workerName = attendanceEvent.WorkerName,
            employeeCode = attendanceEvent.EmployeeCode,
            attendanceType = attendanceEvent.AttendanceType.ToString(),
            attendanceTimeUtc,
            assignmentStatus = assignment is null ? "Unassigned" : "Assigned",
            stageId = assignment?.StageId,
            stageName = assignment?.StageName,
            productionLineId = assignment?.LineId,
            productionLineName = assignment?.LineName
        });

        foreach (var recipientUserId in evaluated.RecipientUserIds)
        {
            var notificationId = DeterministicGuid($"{attendanceEvent.IdempotencyKey}:{recipientUserId:D}");
            var published = await notificationPublisher.PublishToUserAsync(
                new PublishUserNotificationCommand(
                    notificationId,
                    recipientUserId,
                    evaluated.Title!,
                    evaluated.Message!,
                    RelatedWorkerId: attendanceEvent.WorkerId,
                    RelatedEntityType: nameof(AttendanceRecord),
                    RelatedEntityId: attendanceEvent.AttendanceRecordId,
                    CreatedAtUtc: createdAtUtc,
                    EventKey: evaluated.EventKey,
                    Severity: evaluated.Severity,
                    IsToastEnabled: evaluated.Toast.Enabled,
                    IsSoundEnabled: evaluated.Sound.Enabled,
                    IsBrowserEnabled: evaluated.Browser.Enabled,
                    // Legacy clients can still open this trusted route. New clients
                    // use the action/payload stored in MetadataJson above.
                    NavigationUrl: "/attendance/workforce",
                    MetadataJson: metadataJson,
                    CorrelationKey: attendanceEvent.IdempotencyKey),
                cancellationToken);
            if (published.IsFailure)
                return Result.Failure(published.Error!);
        }

        return Result.Success();
    }

    private async Task<AssignmentSnapshot?> ResolvePermanentAssignmentAsync(
        AttendanceNotificationEvent attendanceEvent,
        DateTime attendanceTimeUtc,
        CancellationToken cancellationToken)
    {
        var candidates = await (from assignment in dbContext.WorkerDefaultAssignments.AsNoTracking()
                                join stage in dbContext.SubStages.AsNoTracking() on assignment.SubStageId equals stage.Id
                                join line in dbContext.ProductionLines.AsNoTracking() on assignment.ProductionLineId equals line.Id
                                where assignment.WorkerId == attendanceEvent.WorkerId
                                      && assignment.IsActive
                                      && assignment.AssignedAt <= attendanceTimeUtc
                                      && stage.IsActive
                                      && line.IsActive
                                orderby assignment.AssignedAt descending, stage.DefaultOrder, assignment.Id
                                select new AssignmentSnapshot(stage.Id, stage.Name, line.Id, line.Name, assignment.AssignedAt))
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length > 1)
        {
            logger.LogWarning(
                "Worker has multiple active permanent assignments during attendance notification composition. workerId={WorkerId}, selectedStageId={StageId}",
                attendanceEvent.WorkerId,
                candidates[0].StageId);
        }

        return candidates.FirstOrDefault();
    }

    private static NotificationRecipientRule ToRecipientRule(NotificationPolicyRecipientRule rule) => rule.RecipientKind switch
    {
        NotificationRecipientKind.User => new(NotificationRecipientKind.User, rule.UserId),
        NotificationRecipientKind.Role => new(NotificationRecipientKind.Role, rule.RoleId),
        NotificationRecipientKind.Permission => new(NotificationRecipientKind.Permission, Value: rule.PermissionKey),
        NotificationRecipientKind.CapabilityGroup => new(NotificationRecipientKind.CapabilityGroup, Value: rule.CapabilityKey),
        NotificationRecipientKind.Creator => new(NotificationRecipientKind.Creator),
        NotificationRecipientKind.ExcludeActor => new(NotificationRecipientKind.ExcludeActor),
        NotificationRecipientKind.AllActiveUsers => new(NotificationRecipientKind.AllActiveUsers),
        _ => throw new InvalidOperationException("Unsupported notification recipient rule.")
    };

    private static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guid = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guid);
        guid[7] = (byte)((guid[7] & 0x0f) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3f) | 0x80);
        return new Guid(guid);
    }

    private static DateTime NormalizeStoredUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string StageLabel(string stageName) =>
        stageName.StartsWith("مرحلة", StringComparison.Ordinal)
            ? stageName
            : $"مرحلة {stageName}";

    private sealed record AssignmentSnapshot(Guid StageId, string StageName, Guid LineId, string LineName, DateTime AssignedAtUtc);
}
