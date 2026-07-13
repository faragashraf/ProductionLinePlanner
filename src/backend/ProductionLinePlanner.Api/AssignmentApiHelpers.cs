using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

public static class AssignmentHelpers
{
    private const string TimelineActionAutoReturn = "AutoReturn";
    private const string TempStatusActive = "Active";
    private const string TempStatusScheduled = "Scheduled";
    private const string TempStatusCompleted = "Completed";
    private static readonly HashSet<string> AuditSafeProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id",
        "WorkerId",
        "SubStageId",
        "FromSubStageId",
        "ToSubStageId",
        "ReplacementForWorkerId",
        "AssignedByUserId",
        "ActorUserId",
        "AppUserId",
        "RefreshTokenId",
        "Reason",
        "ActionType",
        "Status",
        "AssignedAtUtc",
        "StartAtUtc",
        "EndAtUtc",
        "CreatedAtUtc",
        "UpdatedAtUtc",
        "ExpiresAtUtc",
        "RevokedAtUtc",
        "IsRead",
        "ReadAtUtc",
        "IsActive",
        "Name",
        "Description",
        "Roles",
        "RoleIds",
        "Permissions",
        "PermissionNames",
        "Permission",
        "Effect",
        "Result"
    };

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public sealed record CurrentWorkerAssignmentState(
        Guid WorkerId,
        AssignmentType? AssignmentType,
        DateTime? StartsAtUtc,
        DateTime? EndsAtUtc,
        Guid? EffectiveSubStageId,
        Guid? FromSubStageId,
        Guid? ToSubStageId,
        Guid? ReplacementForWorkerId);

    public static async Task FinalizeCompletedTemporaryAssignmentsAsync(
        AppDbContext dbContext,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var endedAssignments = await dbContext.WorkerTemporaryAssignments
            .Where(x =>
                (x.Status == TempStatusScheduled || x.Status == TempStatusActive) &&
                x.EndAtUtc <= asOfUtc)
            .ToListAsync(cancellationToken);

        if (endedAssignments.Count == 0)
        {
            return;
        }

        foreach (var assignment in endedAssignments)
        {
            dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.Status)).CurrentValue = TempStatusCompleted;
            dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.UpdatedAtUtc)).CurrentValue = asOfUtc;
            dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
                id: Guid.NewGuid(),
                workerId: assignment.WorkerId,
                fromSubStageId: assignment.FromSubStageId,
                toSubStageId: assignment.ToSubStageId,
                assignmentType: assignment.AssignmentType.ToString(),
                actionType: TimelineActionAutoReturn,
                reason: $"Temporary assignment ended at {assignment.EndAtUtc:O}",
                startAtUtc: assignment.StartAtUtc,
                endAtUtc: assignment.EndAtUtc,
                performedByUserId: assignment.AssignedByUserId,
                isAutomatic: true,
                relatedTemporaryAssignmentId: assignment.Id,
                replacementForWorkerId: assignment.ReplacementForWorkerId,
                createdAtUtc: asOfUtc));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static async Task<Dictionary<Guid, CurrentWorkerAssignmentState>> ResolveCurrentAssignmentsAsync(
        AppDbContext dbContext,
        IEnumerable<Guid> workerIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var uniqueWorkerIds = workerIds.Distinct().ToArray();
        if (uniqueWorkerIds.Length == 0)
        {
            return [];
        }

        await FinalizeCompletedTemporaryAssignmentsAsync(dbContext, asOfUtc, cancellationToken);

        var defaultAssignments = await dbContext.WorkerDefaultAssignments
            .AsNoTracking()
            .Where(x => uniqueWorkerIds.Contains(x.WorkerId) && x.IsActive)
            .Select(x => new { x.WorkerId, x.AssignedAt, x.Id, x.SubStageId })
            .ToListAsync(cancellationToken);

        var currentDefaultsByWorker = defaultAssignments
            .GroupBy(x => x.WorkerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.AssignedAt)
                    .ThenByDescending(x => x.Id)
                    .First());

        var activeTemporaryAssignments = await dbContext.WorkerTemporaryAssignments
            .AsNoTracking()
            .Where(x => uniqueWorkerIds.Contains(x.WorkerId)
                        && x.StartAtUtc <= asOfUtc
                        && x.EndAtUtc > asOfUtc
                        && (x.Status == TempStatusActive || x.Status == TempStatusScheduled))
            .Select(x => new
            {
                x.WorkerId,
                x.Id,
                x.StartAtUtc,
                x.EndAtUtc,
                x.FromSubStageId,
                x.ToSubStageId,
                x.ReplacementForWorkerId
            })
            .ToListAsync(cancellationToken);

        var temporaryByWorker = activeTemporaryAssignments
            .GroupBy(x => x.WorkerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.StartAtUtc)
                    .ThenByDescending(x => x.Id)
                    .First());

        var results = new Dictionary<Guid, CurrentWorkerAssignmentState>(uniqueWorkerIds.Length);

        foreach (var workerId in uniqueWorkerIds)
        {
            if (temporaryByWorker.TryGetValue(workerId, out var tempAssignment))
                {
            results[workerId] = new CurrentWorkerAssignmentState(
                WorkerId: workerId,
                AssignmentType: tempAssignment.ReplacementForWorkerId is null
                    ? AssignmentType.Temporary
                    : AssignmentType.Replacement,
                    StartsAtUtc: tempAssignment.StartAtUtc,
                    EndsAtUtc: tempAssignment.EndAtUtc,
                    EffectiveSubStageId: tempAssignment.ToSubStageId,
                    FromSubStageId: tempAssignment.FromSubStageId,
                    ToSubStageId: tempAssignment.ToSubStageId,
                    ReplacementForWorkerId: tempAssignment.ReplacementForWorkerId);
                continue;
            }

            if (currentDefaultsByWorker.TryGetValue(workerId, out var defaultAssignment))
            {
                results[workerId] = new CurrentWorkerAssignmentState(
                    WorkerId: workerId,
                    AssignmentType: AssignmentType.Default,
                    StartsAtUtc: defaultAssignment.AssignedAt,
                    EndsAtUtc: null,
                    EffectiveSubStageId: defaultAssignment.SubStageId,
                    FromSubStageId: null,
                    ToSubStageId: null,
                    ReplacementForWorkerId: null);
                continue;
            }

            results[workerId] = new CurrentWorkerAssignmentState(
                WorkerId: workerId,
                AssignmentType: null,
                StartsAtUtc: null,
                EndsAtUtc: null,
                EffectiveSubStageId: null,
                FromSubStageId: null,
                ToSubStageId: null,
                ReplacementForWorkerId: null);
        }

        return results;
    }

    public static async Task<Dictionary<Guid, AttendanceStatus>> GetLatestAttendanceStatusByWorkerAsync(
        AppDbContext dbContext,
        Guid[] workerIds,
        CancellationToken cancellationToken,
        DateTime? asOfUtc = null)
    {
        if (workerIds.Length == 0)
        {
            return [];
        }

        var asOf = asOfUtc ?? DateTime.UtcNow;
        var startOfDate = new DateTime(asOf.Year, asOf.Month, asOf.Day, 0, 0, 0, DateTimeKind.Utc);

        var query = dbContext.AttendanceRecords
            .AsNoTracking()
            .Where(x => workerIds.Contains(x.WorkerId) && x.AttendanceTimeUtc >= startOfDate && x.AttendanceTimeUtc <= asOf)
            .GroupBy(x => x.WorkerId)
            .Select(g => new
            {
                WorkerId = g.Key,
                Status = g.OrderByDescending(x => x.AttendanceTimeUtc).First().AttendanceStatus
            });

        var result = await query
            .ToDictionaryAsync(x => x.WorkerId, x => x.Status, cancellationToken);

        return result;
    }

    public static void AddAuditLog(
        AppDbContext dbContext,
        Guid actorUserId,
        AuditActionType actionType,
        string entityType,
        string entityId,
        object? before = null,
        object? after = null,
        HttpContext? httpContext = null,
        string? requestMeta = null)
    {
        if (actorUserId == Guid.Empty || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        var metadata = requestMeta;
        if (metadata is null && httpContext is not null)
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString();
            metadata = $"{httpContext.Request.Method} {httpContext.Request.Path}";
            if (!string.IsNullOrWhiteSpace(ip))
            {
                metadata = $"{metadata} from {ip}";
            }
        }

        dbContext.AuditLogs.Add(new AuditLog(
            id: Guid.NewGuid(),
            actorUserId: actorUserId,
            actionType: actionType,
            entityType: entityType,
            entityId: entityId,
            entityBeforeJson: SerializeAuditPayload(before),
            entityAfterJson: SerializeAuditPayload(after),
            requestMeta: metadata,
            createdAtUtc: DateTime.UtcNow));
    }

    private static string? SerializeAuditPayload(object? payload)
    {
        if (payload is null)
        {
            return null;
        }

        if (IsSimpleType(payload))
        {
            return JsonSerializer.Serialize(payload, AuditJsonOptions);
        }

        var safePayload = BuildSafePayload(payload);
        if (safePayload is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(safePayload, AuditJsonOptions);
    }

    private static object? BuildSafePayload(object payload)
    {
        var payloadType = payload.GetType();
        if (IsSimpleType(payload))
        {
            return payload;
        }

        try
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var properties = payloadType
                .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .Where(x => x.CanRead);

            foreach (var property in properties)
            {
                if (!AuditSafeProperties.Contains(property.Name))
                {
                    continue;
                }

                var value = property.GetValue(payload);
                if (value is null)
                {
                    continue;
                }

                if (IsSimpleType(value))
                {
                    result[property.Name] = value;
                }
                else if (value is System.Collections.IEnumerable values && value is not string)
                {
                    var safeValues = values.Cast<object?>().Where(item => item is not null && IsSimpleType(item)).ToArray();
                    if (safeValues.Length > 0)
                    {
                        result[property.Name] = safeValues;
                    }
                }
            }

            if (result.Count == 0)
            {
                return null;
            }

            result["type"] = payloadType.Name;
            return result;
        }
        catch
        {
            return new Dictionary<string, object?>
            {
                ["type"] = payloadType.Name
            };
        }
    }

    private static bool IsSimpleType(object value)
    {
        var valueType = value.GetType();
        return valueType == typeof(string)
               || valueType == typeof(char)
               || valueType == typeof(Guid)
               || valueType == typeof(DateTime)
               || valueType == typeof(DateTimeOffset)
               || valueType == typeof(bool)
               || valueType == typeof(byte)
               || valueType == typeof(short)
               || valueType == typeof(int)
               || valueType == typeof(long)
               || valueType == typeof(float)
               || valueType == typeof(double)
               || valueType == typeof(decimal)
               || valueType == typeof(ushort)
               || valueType == typeof(uint)
               || valueType == typeof(ulong)
               || valueType.IsEnum;
    }

}
