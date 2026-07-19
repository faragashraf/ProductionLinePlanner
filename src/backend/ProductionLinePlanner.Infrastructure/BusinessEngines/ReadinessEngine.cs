using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class ReadinessEngine : IReadinessEngine
{
    private readonly AppDbContext _dbContext;
    private readonly IAssignmentEngine _assignmentEngine;
    private readonly IAttendanceEngine _attendanceEngine;

    public ReadinessEngine(
        AppDbContext dbContext,
        IAssignmentEngine assignmentEngine,
        IAttendanceEngine attendanceEngine)
    {
        _dbContext = dbContext;
        _assignmentEngine = assignmentEngine;
        _attendanceEngine = attendanceEngine;
    }

    public async Task<Result<StageReadinessDto>> GetFactoryReadinessAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = asOfUtc ?? DateTime.UtcNow;

        var activeSubStages = await (from ss in _dbContext.SubStages.AsNoTracking()
                                    join ms in _dbContext.MainStages.AsNoTracking() on ss.MainStageId equals ms.Id
                                    join pl in _dbContext.ProductionLines.AsNoTracking() on ms.ProductionLineId equals pl.Id
                                    where ss.IsActive && ms.IsActive && pl.IsActive
                                    select new { ss.Id, ss.Capacity })
            .ToListAsync(cancellationToken);

        var requiredWorkers = activeSubStages.Sum(x => x.Capacity);
        var activeWorkerIds = await _dbContext.Workers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var assignments = await _assignmentEngine.ResolveCurrentAssignmentsAsync(activeWorkerIds, asOf, cancellationToken);
        if (assignments.IsFailure)
        {
            return Result<StageReadinessDto>.Failure(assignments.Error!);
        }

        var activeSubStageIds = activeSubStages.Select(x => x.Id).ToHashSet();
        var assignmentsInActiveSubStages = assignments.Value!
            .Where(x => x.Value.EffectiveSubStageId.HasValue && activeSubStageIds.Contains(x.Value.EffectiveSubStageId.Value))
            .ToList();

        var attendanceByWorkerResult = await _attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(
            assignmentsInActiveSubStages.Select(x => x.Key),
            asOf,
            cancellationToken);

        if (attendanceByWorkerResult.IsFailure)
        {
            return Result<StageReadinessDto>.Failure(attendanceByWorkerResult.Error!);
        }

        var attendanceByWorker = attendanceByWorkerResult.Value!;
        var counts = ComputeReadinessCounts(
            requiredWorkers,
            assignmentsInActiveSubStages.Select(x => x.Key).Count(),
            attendanceByWorker,
            assignmentsInActiveSubStages.Select(x => x.Key).ToArray());

        return Result<StageReadinessDto>.Success(new StageReadinessDto
        {
            ScopeType = "Factory",
            ScopeEntityId = Guid.Empty,
            RequiredWorkers = counts.RequiredWorkers,
            AssignedWorkers = counts.AssignedWorkers,
            PresentWorkers = counts.PresentWorkers,
            LateWorkers = counts.LateWorkers,
            AbsentWorkers = counts.AbsentWorkers,
            UnassignedWorkers = counts.UnassignedWorkers,
            ReadinessPercent = counts.ReadinessPercent,
            AssignmentCoveragePercent = counts.AssignmentCoveragePercent,
            AttendanceDataStatus = counts.AttendanceDataStatus,
            Status = StageReadinessSnapshot.ReadinessFromPercent(counts.ReadinessPercent),
            CalculatedAtUtc = asOf
        });
    }

    public async Task<Result<ProductionLinesReadinessDto>> GetProductionLinesReadinessAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = asOfUtc ?? DateTime.UtcNow;

        var lineItems = await (from line in _dbContext.ProductionLines.AsNoTracking()
                              join mainStage in _dbContext.MainStages.AsNoTracking() on line.Id equals mainStage.ProductionLineId
                              join subStage in _dbContext.SubStages.AsNoTracking() on mainStage.Id equals subStage.MainStageId
                              where line.IsActive && mainStage.IsActive && subStage.IsActive
                              group subStage by new { line.Id, line.Name } into g
                              select new
                              {
                                  ProductionLineId = g.Key.Id,
                                  LineName = g.Key.Name,
                                  RequiredWorkers = g.Sum(x => x.Capacity),
                                  SubStageIds = g.Select(x => x.Id).ToArray()
                              })
            .ToListAsync(cancellationToken);

        var activeWorkerIds = await _dbContext.Workers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var assignments = await _assignmentEngine.ResolveCurrentAssignmentsAsync(activeWorkerIds, asOf, cancellationToken);
        if (assignments.IsFailure)
        {
            return Result<ProductionLinesReadinessDto>.Failure(assignments.Error!);
        }

        var assignmentByWorker = assignments.Value!;
        var subStageWorkerIds = assignmentByWorker
            .Where(x => x.Value.EffectiveSubStageId.HasValue)
            .Select(x => x.Key)
            .ToArray();

        var attendanceByWorkerResult = await _attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(subStageWorkerIds, asOf, cancellationToken);
        if (attendanceByWorkerResult.IsFailure)
        {
            return Result<ProductionLinesReadinessDto>.Failure(attendanceByWorkerResult.Error!);
        }

        var attendanceByWorker = attendanceByWorkerResult.Value!;

        var lineReadiness = lineItems
            .Select(item =>
            {
                var assignmentsInLine = assignments.Value!
                    .Where(x => x.Value.EffectiveSubStageId is not null && item.SubStageIds.Contains(x.Value.EffectiveSubStageId.Value))
                    .ToList();

                var counts = ComputeReadinessCounts(
                    item.RequiredWorkers,
                    assignmentsInLine.Count,
                    attendanceByWorker,
                    assignmentsInLine.Select(x => x.Key).ToArray());

                return new ProductionLineReadinessItemDto
                {
                    ScopeType = "ProductionLine",
                    ScopeEntityId = item.ProductionLineId,
                    LineName = item.LineName,
                    RequiredWorkers = counts.RequiredWorkers,
                    AssignedWorkers = counts.AssignedWorkers,
                    PresentWorkers = counts.PresentWorkers,
                    LateWorkers = counts.LateWorkers,
                    AbsentWorkers = counts.AbsentWorkers,
                    UnassignedWorkers = counts.UnassignedWorkers,
                    ReadinessPercent = counts.ReadinessPercent,
                    AssignmentCoveragePercent = counts.AssignmentCoveragePercent,
                    AttendanceDataStatus = counts.AttendanceDataStatus,
                    Status = StageReadinessSnapshot.ReadinessFromPercent(counts.ReadinessPercent)
                };
            })
            .ToList();

        var requiredWorkers = lineItems.Sum(x => x.RequiredWorkers);
        var assignedWorkers = lineReadiness.Sum(x => x.AssignedWorkers);
        var presentWorkers = lineReadiness.Sum(x => x.PresentWorkers);
        var lateWorkers = lineReadiness.Sum(x => x.LateWorkers);
        var absentWorkers = lineReadiness.Sum(x => x.AbsentWorkers);
        var unassignedWorkers = lineReadiness.Sum(x => x.UnassignedWorkers);
        var readinessPercent = StageReadinessSnapshot.CalculateReadinessPercent(requiredWorkers, presentWorkers, lateWorkers, absentWorkers, unassignedWorkers);
        var assignmentCoveragePercent = CalculateAssignmentCoveragePercent(requiredWorkers, assignedWorkers);
        var attendanceDataStatus = AggregateAttendanceDataStatus(lineReadiness.Select(x => x.AttendanceDataStatus));

        return Result<ProductionLinesReadinessDto>.Success(new ProductionLinesReadinessDto
        {
            ScopeEntityId = Guid.Empty,
            RequiredWorkers = requiredWorkers,
            AssignedWorkers = assignedWorkers,
            PresentWorkers = presentWorkers,
            LateWorkers = lateWorkers,
            AbsentWorkers = absentWorkers,
            UnassignedWorkers = unassignedWorkers,
            ReadinessPercent = readinessPercent,
            AssignmentCoveragePercent = assignmentCoveragePercent,
            AttendanceDataStatus = attendanceDataStatus,
            Status = StageReadinessSnapshot.ReadinessFromPercent(readinessPercent),
            CalculatedAtUtc = asOf,
            Items = lineReadiness.ToArray()
        });
    }

    public async Task<Result<StageReadinessDto>> GetSubStageReadinessAsync(
        Guid subStageId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (subStageId == Guid.Empty)
        {
            return Result<StageReadinessDto>.Failure(new Error("ValidationError", "SubStageId is required."));
        }

        var subStage = await _dbContext.SubStages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subStageId, cancellationToken);

        if (subStage is null)
        {
            return Result<StageReadinessDto>.Failure(new Error("NotFound", "SubStage not found."));
        }

        var asOf = asOfUtc ?? DateTime.UtcNow;
        var activeWorkerIds = await _dbContext.Workers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var assignments = await _assignmentEngine.ResolveCurrentAssignmentsAsync(activeWorkerIds, asOf, cancellationToken);
        if (assignments.IsFailure)
        {
            return Result<StageReadinessDto>.Failure(assignments.Error!);
        }

        var matchingAssignments = assignments.Value!
            .Where(x => x.Value.EffectiveSubStageId == subStageId)
            .ToList();

        var attendanceByWorkerResult = await _attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(
            matchingAssignments.Select(x => x.Key),
            asOf,
            cancellationToken);

        if (attendanceByWorkerResult.IsFailure)
        {
            return Result<StageReadinessDto>.Failure(attendanceByWorkerResult.Error!);
        }

        var counts = ComputeReadinessCounts(
            subStage.Capacity,
            matchingAssignments.Count,
            attendanceByWorkerResult.Value!,
            matchingAssignments.Select(x => x.Key).ToArray());

        return Result<StageReadinessDto>.Success(new StageReadinessDto
        {
            ScopeType = "SubStage",
            ScopeEntityId = subStageId,
            RequiredWorkers = counts.RequiredWorkers,
            AssignedWorkers = counts.AssignedWorkers,
            PresentWorkers = counts.PresentWorkers,
            LateWorkers = counts.LateWorkers,
            AbsentWorkers = counts.AbsentWorkers,
            UnassignedWorkers = counts.UnassignedWorkers,
            ReadinessPercent = counts.ReadinessPercent,
            AssignmentCoveragePercent = counts.AssignmentCoveragePercent,
            AttendanceDataStatus = counts.AttendanceDataStatus,
            Status = StageReadinessSnapshot.ReadinessFromPercent(counts.ReadinessPercent),
            CalculatedAtUtc = asOf
        });
    }

    public async Task<Result<IReadOnlyCollection<SubStageAttendanceSummaryDto>>> GetActiveSubStageAttendanceSummariesAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = asOfUtc ?? DateTime.UtcNow;
        var activeSubStages = await _dbContext.SubStages
            .AsNoTracking()
            .Where(stage => stage.IsActive)
            .OrderBy(stage => stage.MainStageId)
            .ThenBy(stage => stage.DefaultOrder)
            .Select(stage => new
            {
                stage.Id,
                stage.MainStageId,
                ProductionLineId = stage.MainStage!.ProductionLineId,
                FactoryId = stage.MainStage.ProductionLine!.FactoryId
            })
            .ToArrayAsync(cancellationToken);
        var activeSubStageIds = activeSubStages.Select(stage => stage.Id).ToArray();

        if (activeSubStageIds.Length == 0)
        {
            return Result<IReadOnlyCollection<SubStageAttendanceSummaryDto>>.Success([]);
        }

        var activeWorkerIds = await _dbContext.Workers
            .AsNoTracking()
            .Where(worker => worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active)
            .Select(worker => worker.Id)
            .ToArrayAsync(cancellationToken);

        var effectiveAssignmentsResult = await _assignmentEngine.ResolveEffectiveAssignmentsAsync(
            activeWorkerIds,
            asOf,
            cancellationToken);
        if (effectiveAssignmentsResult.IsFailure)
        {
            return Result<IReadOnlyCollection<SubStageAttendanceSummaryDto>>.Failure(effectiveAssignmentsResult.Error!);
        }

        var effectiveParticipations = effectiveAssignmentsResult.Value!
            .SelectMany(pair => pair.Value
                .Where(assignment => assignment.EffectiveSubStageId.HasValue && activeSubStageIds.Contains(assignment.EffectiveSubStageId.Value))
                .Select(assignment => new { SubStageId = assignment.EffectiveSubStageId!.Value, WorkerId = pair.Key }))
            .Distinct()
            .ToArray();
        var assignedWorkerIdsByStage = effectiveParticipations
            .GroupBy(item => item.SubStageId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.WorkerId).ToArray());
        var assignedWorkerIdsByMainStage = effectiveParticipations
            .Join(activeSubStages, item => item.SubStageId, stage => stage.Id, (item, stage) => new { ScopeId = stage.MainStageId, item.WorkerId })
            .Distinct()
            .GroupBy(item => item.ScopeId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.WorkerId).ToArray());
        var assignedWorkerIdsByProductionLine = effectiveParticipations
            .Join(activeSubStages, item => item.SubStageId, stage => stage.Id, (item, stage) => new { ScopeId = stage.ProductionLineId, item.WorkerId })
            .Distinct()
            .GroupBy(item => item.ScopeId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.WorkerId).ToArray());
        var assignedWorkerIdsByFactory = effectiveParticipations
            .Join(activeSubStages, item => item.SubStageId, stage => stage.Id, (item, stage) => new { ScopeId = stage.FactoryId, item.WorkerId })
            .Distinct()
            .GroupBy(item => item.ScopeId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.WorkerId).ToArray());

        var assignedWorkerIds = assignedWorkerIdsByStage.Values.SelectMany(workerIds => workerIds).Distinct().ToArray();
        var attendanceResult = await _attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(
            assignedWorkerIds,
            asOf,
            cancellationToken);
        if (attendanceResult.IsFailure)
        {
            return Result<IReadOnlyCollection<SubStageAttendanceSummaryDto>>.Failure(attendanceResult.Error!);
        }

        var attendanceByWorker = attendanceResult.Value!;
        var mainStageCounts = assignedWorkerIdsByMainStage.ToDictionary(
            pair => pair.Key,
            pair => CountDistinctAttendance(pair.Value, attendanceByWorker));
        var productionLineCounts = assignedWorkerIdsByProductionLine.ToDictionary(
            pair => pair.Key,
            pair => CountDistinctAttendance(pair.Value, attendanceByWorker));
        var factoryCounts = assignedWorkerIdsByFactory.ToDictionary(
            pair => pair.Key,
            pair => CountDistinctAttendance(pair.Value, attendanceByWorker));
        var summaries = activeSubStages
            .Select(stage =>
            {
                var mainStage = mainStageCounts.GetValueOrDefault(stage.MainStageId, DistinctAttendanceCounts.Empty);
                var productionLine = productionLineCounts.GetValueOrDefault(stage.ProductionLineId, DistinctAttendanceCounts.Empty);
                var factory = factoryCounts.GetValueOrDefault(stage.FactoryId, DistinctAttendanceCounts.Empty);
                return CreateSubStageAttendanceSummary(
                    stage.Id,
                    assignedWorkerIdsByStage.GetValueOrDefault(stage.Id, []),
                    attendanceByWorker) with
                {
                    MainStageDistinctAssignedWorkersCount = mainStage.Assigned,
                    MainStageDistinctPresentWorkersCount = mainStage.Present,
                    MainStageDistinctAbsentWorkersCount = mainStage.Absent,
                    ProductionLineDistinctAssignedWorkersCount = productionLine.Assigned,
                    ProductionLineDistinctPresentWorkersCount = productionLine.Present,
                    ProductionLineDistinctAbsentWorkersCount = productionLine.Absent,
                    FactoryDistinctAssignedWorkersCount = factory.Assigned,
                    FactoryDistinctPresentWorkersCount = factory.Present,
                    FactoryDistinctAbsentWorkersCount = factory.Absent
                };
            })
            .ToArray();

        return Result<IReadOnlyCollection<SubStageAttendanceSummaryDto>>.Success(summaries);
    }

    private static ReadinessCountResult ComputeReadinessCounts(
        int requiredWorkers,
        int assignedWorkers,
        Dictionary<Guid, AttendanceStatusRecord> attendanceByWorker,
        IReadOnlyCollection<Guid>? workerIds = null)
    {
        var present = 0;
        var late = 0;
        var absent = 0;
        var unassignedFromAttendance = 0;

        var relevantWorkers = (workerIds ?? attendanceByWorker.Keys.ToArray())
            .Distinct()
            .ToArray();

        foreach (var workerId in relevantWorkers)
        {
            if (!attendanceByWorker.TryGetValue(workerId, out var statusRecord))
            {
                unassignedFromAttendance++;
                continue;
            }

            if (statusRecord.Status == AttendanceStatus.Present)
            {
                present++;
            }
            else if (statusRecord.Status == AttendanceStatus.Late)
            {
                late++;
            }
            else if (statusRecord.Status == AttendanceStatus.Absent)
            {
                absent++;
            }
            else
            {
                unassignedFromAttendance++;
            }
        }

        var unassignedWorkers = Math.Max(0, requiredWorkers - assignedWorkers) + unassignedFromAttendance;
        var readinessPercent = StageReadinessSnapshot.CalculateReadinessPercent(requiredWorkers, present, late, absent, unassignedWorkers);
        var attendanceDataStatus = DetermineAttendanceDataStatus(requiredWorkers, assignedWorkers, relevantWorkers, attendanceByWorker);

        return new ReadinessCountResult(
            requiredWorkers,
            assignedWorkers,
            present,
            late,
            absent,
            unassignedWorkers,
            readinessPercent,
            CalculateAssignmentCoveragePercent(requiredWorkers, assignedWorkers),
            attendanceDataStatus);
    }

    private static decimal CalculateAssignmentCoveragePercent(int requiredWorkers, int assignedWorkers)
    {
        if (requiredWorkers <= 0)
            return 100m;

        return Math.Clamp((decimal)assignedWorkers / requiredWorkers * 100m, 0m, 100m);
    }

    private static string DetermineAttendanceDataStatus(
        int requiredWorkers,
        int assignedWorkers,
        IReadOnlyCollection<Guid> relevantWorkers,
        IReadOnlyDictionary<Guid, AttendanceStatusRecord> attendanceByWorker)
    {
        if (requiredWorkers <= 0)
            return "NotRequired";

        if (assignedWorkers <= 0 || relevantWorkers.Count == 0)
            return "NoAssignments";

        var attendanceRecordCount = relevantWorkers.Count(attendanceByWorker.ContainsKey);
        if (attendanceRecordCount == 0)
            return "Unavailable";

        return attendanceRecordCount == relevantWorkers.Count ? "Complete" : "Incomplete";
    }

    private static string AggregateAttendanceDataStatus(IEnumerable<string> statuses)
    {
        var values = statuses.Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length == 0 || values.All(x => x == "NotRequired"))
            return "NotRequired";
        if (values.All(x => x == "NoAssignments"))
            return "NoAssignments";
        if (values.All(x => x == "Complete"))
            return "Complete";
        if (values.All(x => x == "Unavailable"))
            return "Unavailable";
        return "Incomplete";
    }

    private static SubStageAttendanceSummaryDto CreateSubStageAttendanceSummary(
        Guid subStageId,
        IReadOnlyCollection<Guid> assignedWorkerIds,
        IReadOnlyDictionary<Guid, AttendanceStatusRecord> attendanceByWorker)
    {
        var assignedWorkersCount = assignedWorkerIds.Count;
        var presentWorkersCount = 0;
        var lateWorkersCount = 0;
        var absentWorkersCount = 0;
        var unresolvedWorkersCount = 0;

        foreach (var workerId in assignedWorkerIds)
        {
            if (!attendanceByWorker.TryGetValue(workerId, out var attendance))
            {
                unresolvedWorkersCount++;
                continue;
            }

            if (attendance.Status == AttendanceStatus.Present)
            {
                presentWorkersCount++;
            }
            else if (attendance.Status == AttendanceStatus.Late)
            {
                lateWorkersCount++;
            }
            else if (attendance.Status == AttendanceStatus.Absent)
            {
                absentWorkersCount++;
            }
            else
            {
                unresolvedWorkersCount++;
            }
        }

        var attendanceDataStatus = DetermineAttendanceDataStatus(
            assignedWorkersCount,
            assignedWorkersCount,
            assignedWorkerIds,
            attendanceByWorker);
        var attendanceStatus = assignedWorkersCount == 0
            ? "NoAssignments"
            : attendanceDataStatus != "Complete" || unresolvedWorkersCount > 0
                ? "NeedsSync"
                : presentWorkersCount + lateWorkersCount == assignedWorkersCount
                    ? "FullyPresent"
                    : absentWorkersCount == assignedWorkersCount
                        ? "AllAbsent"
                        : "PartiallyPresent";

        return new SubStageAttendanceSummaryDto(
            subStageId,
            assignedWorkersCount,
            presentWorkersCount + lateWorkersCount,
            lateWorkersCount,
            absentWorkersCount,
            unresolvedWorkersCount,
            attendanceDataStatus,
            attendanceStatus);
    }

    private static DistinctAttendanceCounts CountDistinctAttendance(
        IReadOnlyCollection<Guid> workerIds,
        IReadOnlyDictionary<Guid, AttendanceStatusRecord> attendanceByWorker)
    {
        var distinctWorkerIds = workerIds.Distinct().ToArray();
        var present = 0;
        var absent = 0;
        foreach (var workerId in distinctWorkerIds)
        {
            if (!attendanceByWorker.TryGetValue(workerId, out var attendance))
            {
                continue;
            }

            if (attendance.Status is AttendanceStatus.Present or AttendanceStatus.Late)
            {
                present++;
            }
            else if (attendance.Status == AttendanceStatus.Absent)
            {
                absent++;
            }
        }

        return new DistinctAttendanceCounts(distinctWorkerIds.Length, present, absent);
    }

    private sealed record DistinctAttendanceCounts(int Assigned, int Present, int Absent)
    {
        public static readonly DistinctAttendanceCounts Empty = new(0, 0, 0);
    }

    private sealed record ReadinessCountResult(
        int RequiredWorkers,
        int AssignedWorkers,
        int PresentWorkers,
        int LateWorkers,
        int AbsentWorkers,
        int UnassignedWorkers,
        decimal ReadinessPercent,
        decimal AssignmentCoveragePercent,
        string AttendanceDataStatus);
}
