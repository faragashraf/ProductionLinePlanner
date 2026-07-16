using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

public static class AssignmentHelpers
{
    private const string TempStatusActive = "Active";
    private const string TempStatusScheduled = "Scheduled";

    public sealed record CurrentWorkerAssignmentState(
        Guid WorkerId,
        AssignmentType? AssignmentType,
        DateTime? StartsAtUtc,
        DateTime? EndsAtUtc,
        Guid? EffectiveSubStageId,
        Guid? FromSubStageId,
        Guid? ToSubStageId,
        Guid? ReplacementForWorkerId);

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

}
