using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class AssignmentRecommendationEngine : IAssignmentRecommendationEngine
{
    private const int DefaultTopCandidates = 10;
    private const int MaxTopCandidates = 25;
    private const int HistoryLookbackDays = 45;
    private const decimal BaseScore = 0m;
    private const decimal PresentScore = 25m;
    private const decimal LateScore = 20m;
    private const decimal DefaultStageScore = 15m;
    private const decimal SameLineScore = 12m;
    private const decimal SameSubStageScore = 30m;
    private const decimal TimelineHistoryScore = 10m;
    private const decimal UnderstaffedPenalty = -30m;
    private const decimal CriticalPenalty = -25m;

    private readonly AppDbContext _dbContext;
    private readonly IAssignmentEngine _assignmentEngine;
    private readonly IAttendanceEngine _attendanceEngine;
    private readonly IAuditEngine _auditEngine;

    public AssignmentRecommendationEngine(
        AppDbContext dbContext,
        IAssignmentEngine assignmentEngine,
        IAttendanceEngine attendanceEngine,
        IAuditEngine auditEngine)
    {
        _dbContext = dbContext;
        _assignmentEngine = assignmentEngine;
        _attendanceEngine = attendanceEngine;
        _auditEngine = auditEngine;
    }

    public async Task<Result<AssignmentRecommendationResultDto>> GetRecommendationsAsync(
        Guid subStageId,
        Guid actorUserId,
        string? requestMeta = null,
        int topCandidates = DefaultTopCandidates,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<AssignmentRecommendationResultDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (subStageId == Guid.Empty)
        {
            return Result<AssignmentRecommendationResultDto>.Failure(new Error("ValidationError", "SubStageId is required."));
        }

        if (topCandidates <= 0)
        {
            return Result<AssignmentRecommendationResultDto>.Failure(new Error("ValidationError", "topCandidates must be greater than 0."));
        }

        if (topCandidates > MaxTopCandidates)
        {
            topCandidates = MaxTopCandidates;
        }

        var asOfUtc = DateTime.UtcNow;

        var targetSubStage = await (from ss in _dbContext.SubStages.AsNoTracking()
                                    join ms in _dbContext.MainStages.AsNoTracking() on ss.MainStageId equals ms.Id
                                    where ss.Id == subStageId && ss.IsActive && ms.IsActive
                                    select new
                                    {
                                        SubStageId = ss.Id,
                                        ss.Capacity,
                                        MainStageId = ms.Id,
                                        ms.IsCritical,
                                        ms.ProductionLineId
                                    })
            .FirstOrDefaultAsync(cancellationToken);

        if (targetSubStage is null)
        {
            return Result<AssignmentRecommendationResultDto>.Failure(new Error("NotFound", "Sub-stage not found."));
        }

        var activeWorkers = await _dbContext.Workers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.FullName })
            .ToListAsync(cancellationToken);

        if (activeWorkers.Count == 0)
        {
            return Result<AssignmentRecommendationResultDto>.Success(new AssignmentRecommendationResultDto
            {
                SubStageId = subStageId,
                AsOfUtc = asOfUtc,
                TopCandidates = topCandidates,
                Candidates = Array.Empty<AssignmentRecommendationCandidateDto>()
            });
        }

        var workerIds = activeWorkers.Select(x => x.Id).ToArray();
        var currentAssignmentsResult = await _assignmentEngine.ResolveCurrentAssignmentsAsync(workerIds, asOfUtc, cancellationToken);
        if (currentAssignmentsResult.IsFailure)
        {
            return Result<AssignmentRecommendationResultDto>.Failure(currentAssignmentsResult.Error!);
        }

        var defaultAssignments = await (from defaultAssignment in _dbContext.WorkerDefaultAssignments.AsNoTracking()
                                       join ss in _dbContext.SubStages.AsNoTracking() on defaultAssignment.SubStageId equals ss.Id
                                       join ms in _dbContext.MainStages.AsNoTracking() on ss.MainStageId equals ms.Id
                                       where workerIds.Contains(defaultAssignment.WorkerId)
                                           && defaultAssignment.IsActive
                                           && ss.IsActive
                                           && ms.IsActive
                                       select new CandidateDefaultProfile(
                                           defaultAssignment.WorkerId,
                                           defaultAssignment.SubStageId,
                                           defaultAssignment.AssignedAt,
                                           ms.ProductionLineId))
            .ToListAsync(cancellationToken);

        var defaultProfileByWorker = defaultAssignments
            .GroupBy(x => x.WorkerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.AssignedAt)
                    .ThenByDescending(x => x.SubStageId)
                    .First());

        var currentAssignments = currentAssignmentsResult.Value!.ToDictionary(
            x => x.Key,
            x => x.Value);

        var candidatesBase = activeWorkers
            .Where(w => currentAssignments.TryGetValue(w.Id, out var assignment)
                        && assignment.AssignmentType == AssignmentType.Default
                        && defaultProfileByWorker.ContainsKey(w.Id))
            .Select(w => new
            {
                w.Id,
                w.FullName,
                Assignment = currentAssignments[w.Id],
                DefaultProfile = defaultProfileByWorker[w.Id]
            })
            .ToList();

        if (candidatesBase.Count == 0)
        {
            return Result<AssignmentRecommendationResultDto>.Success(new AssignmentRecommendationResultDto
            {
                SubStageId = subStageId,
                AsOfUtc = asOfUtc,
                TopCandidates = topCandidates,
                Candidates = Array.Empty<AssignmentRecommendationCandidateDto>()
            });
        }

        var candidateIds = candidatesBase.Select(x => x.Id).ToArray();

        var attendanceByWorkerResult = await _attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(candidateIds, asOfUtc, cancellationToken);
        if (attendanceByWorkerResult.IsFailure)
        {
            return Result<AssignmentRecommendationResultDto>.Failure(attendanceByWorkerResult.Error!);
        }

        var attendanceByWorker = attendanceByWorkerResult.Value!;

        var historyLookup = await GetHistorySignalsForSubStageAsync(candidateIds, subStageId, asOfUtc, cancellationToken);
        var loadBySubStage = candidatesBase
            .Where(x => x.Assignment.EffectiveSubStageId.HasValue)
            .GroupBy(x => x.Assignment.EffectiveSubStageId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var sourceSubStageStats = await GetSourceSubStageStatsAsync(loadBySubStage, cancellationToken);

        var candidates = new List<AssignmentRecommendationCandidateDto>(candidateIds.Length);
        foreach (var candidate in candidatesBase)
        {
            if (!attendanceByWorker.TryGetValue(candidate.Id, out var attendanceRecord))
            {
                continue;
            }

            if (attendanceRecord.Status == AttendanceStatus.Absent)
            {
                continue;
            }

            var score = BaseScore;
            var reasons = new List<string>();
            var riskWarnings = new List<string>();

            score += DefaultStageScore;
            reasons.Add("Worker has an active default assignment.");

            if (attendanceRecord.Status == AttendanceStatus.Present)
            {
                score += PresentScore;
                reasons.Add("Worker is present today.");
            }
            else
            {
                score += LateScore;
                reasons.Add("Worker has a late attendance record for today.");
                riskWarnings.Add("Worker is late and may impact immediate readiness.");
            }

            if (candidate.DefaultProfile.ProductionLineId == targetSubStage.ProductionLineId)
            {
                score += SameLineScore;
                reasons.Add("Worker is currently assigned on the same production line.");
            }

            if (candidate.DefaultProfile.SubStageId == subStageId)
            {
                score += SameSubStageScore;
                reasons.Add("Worker already defaults to the target sub-stage.");
            }
            else if (historyLookup.TryGetValue(candidate.Id, out var hasHistory) && hasHistory)
            {
                score += TimelineHistoryScore;
                reasons.Add("Worker has recently worked on the target sub-stage.");
            }

            if (candidate.Assignment.EffectiveSubStageId.HasValue &&
                sourceSubStageStats.TryGetValue(candidate.Assignment.EffectiveSubStageId.Value, out var sourceStats))
            {
                if (sourceStats.IsCritical)
                {
                    riskWarnings.Add("Worker is currently in a critical stage.");
                    score += CriticalPenalty;
                }

                if (sourceStats.IsUnderstaffed)
                {
                    riskWarnings.Add("Worker is currently in an understaffed stage.");
                    score += UnderstaffedPenalty;
                }
            }

            candidates.Add(new AssignmentRecommendationCandidateDto
            {
                WorkerId = candidate.Id,
                WorkerName = candidate.FullName,
                Score = score,
                Reasons = reasons.ToArray(),
                RiskWarnings = riskWarnings.ToArray()
            });
        }

        var resultCandidates = candidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.WorkerName)
            .Take(topCandidates)
            .ToArray();

        var result = new AssignmentRecommendationResultDto
        {
            SubStageId = subStageId,
            AsOfUtc = asOfUtc,
            TopCandidates = topCandidates,
            Candidates = resultCandidates
        };

        await _auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Resolve,
            "AssignmentRecommendation",
            subStageId.ToString(),
            before: null,
            after: new { subStageId, topCandidates, asOfUtc, resultCount = resultCandidates.Length },
            requestMeta: requestMeta,
            cancellationToken: cancellationToken);

        return Result<AssignmentRecommendationResultDto>.Success(result);
    }

    private async Task<Dictionary<Guid, bool>> GetHistorySignalsForSubStageAsync(
        Guid[] candidateIds,
        Guid targetSubStageId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        if (candidateIds.Length == 0)
        {
            return [];
        }

        var fromDateUtc = asOfUtc.AddDays(-HistoryLookbackDays);
        var records = await _dbContext.AssignmentTimelineEntries
            .AsNoTracking()
            .Where(x =>
                candidateIds.Contains(x.WorkerId)
                && (x.ToSubStageId == targetSubStageId || x.FromSubStageId == targetSubStageId)
                && (x.EndAtUtc == null || x.EndAtUtc >= fromDateUtc)
                && x.StartAtUtc >= fromDateUtc)
            .Select(x => new { x.WorkerId })
            .ToListAsync(cancellationToken);

        return records
            .DistinctBy(x => x.WorkerId)
            .ToDictionary(x => x.WorkerId, _ => true);
    }

    private async Task<Dictionary<Guid, SourceSubStageReadiness>> GetSourceSubStageStatsAsync(
        IReadOnlyDictionary<Guid, int> sourceSubStageLoad,
        CancellationToken cancellationToken)
    {
        if (sourceSubStageLoad.Count == 0)
        {
            return [];
        }

        var sources = await (from ss in _dbContext.SubStages.AsNoTracking()
                             join ms in _dbContext.MainStages.AsNoTracking() on ss.MainStageId equals ms.Id
                             where sourceSubStageLoad.Keys.Contains(ss.Id) && ss.IsActive && ms.IsActive
                             select new
                             {
                                 SubStageId = ss.Id,
                                 ss.Capacity,
                                 IsCritical = ms.IsCritical
                             })
            .ToListAsync(cancellationToken);

        return sources
            .Select(x => new
            {
                x.SubStageId,
                x.Capacity,
                x.IsCritical
            })
            .ToDictionary(
                x => x.SubStageId,
                x =>
                {
                    sourceSubStageLoad.TryGetValue(x.SubStageId, out var currentWorkerCount);
                    return new SourceSubStageReadiness(
                        x.SubStageId,
                        x.Capacity,
                        x.IsCritical,
                        currentWorkerCount,
                        currentWorkerCount < x.Capacity);
                });
    }

    private sealed record CandidateDefaultProfile(
        Guid WorkerId,
        Guid SubStageId,
        DateTime AssignedAt,
        Guid ProductionLineId);

    private sealed record SourceSubStageReadiness(
        Guid SubStageId,
        int Capacity,
        bool IsCritical,
        int CurrentWorkerCount,
        bool IsUnderstaffed);
}
