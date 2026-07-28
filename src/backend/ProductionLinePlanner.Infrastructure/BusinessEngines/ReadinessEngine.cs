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

        var activeLineStages = await (from modelStage in _dbContext.ProductModelStages.AsNoTracking()
                                      join subStage in _dbContext.SubStages.AsNoTracking() on modelStage.SubStageId equals subStage.Id
                                      join mainStage in _dbContext.MainStages.AsNoTracking() on subStage.MainStageId equals mainStage.Id
                                      join line in _dbContext.ProductionLines.AsNoTracking() on modelStage.ProductionLineId equals line.Id
                                      where modelStage.IsActive
                                            && modelStage.IsRequired
                                            && subStage.IsActive
                                            && mainStage.IsActive
                                            && line.IsActive
                                            && line.DepartmentId == subStage.DepartmentId
                                      select new { modelStage.ProductionLineId, SubStageId = subStage.Id, subStage.Capacity })
            .Distinct()
            .ToListAsync(cancellationToken);

        var requiredWorkers = activeLineStages.Sum(x => x.Capacity);
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

        var activeLineStageKeys = activeLineStages
            .Select(stage => (stage.ProductionLineId, stage.SubStageId))
            .ToHashSet();
        var assignmentsInActiveSubStages = assignments.Value!
            .Where(x => x.Value.EffectiveSubStageId.HasValue
                && x.Value.ProductionLineId.HasValue
                && activeLineStageKeys.Contains((x.Value.ProductionLineId.Value, x.Value.EffectiveSubStageId.Value)))
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

        var activeLineStages = await (from modelStage in _dbContext.ProductModelStages.AsNoTracking()
                                      join subStage in _dbContext.SubStages.AsNoTracking() on modelStage.SubStageId equals subStage.Id
                                      join mainStage in _dbContext.MainStages.AsNoTracking() on subStage.MainStageId equals mainStage.Id
                                      join line in _dbContext.ProductionLines.AsNoTracking() on modelStage.ProductionLineId equals line.Id
                                      where modelStage.IsActive
                                            && modelStage.IsRequired
                                            && subStage.IsActive
                                            && mainStage.IsActive
                                            && line.IsActive
                                            && line.DepartmentId == subStage.DepartmentId
                                      select new
                                      {
                                          ProductionLineId = line.Id,
                                          LineName = line.Name,
                                          SubStageId = subStage.Id,
                                          subStage.Capacity
                                      })
            .Distinct()
            .ToListAsync(cancellationToken);
        var lineItems = activeLineStages
            .GroupBy(stage => new { stage.ProductionLineId, stage.LineName })
            .Select(group => new
            {
                group.Key.ProductionLineId,
                group.Key.LineName,
                RequiredWorkers = group.Sum(stage => stage.Capacity),
                SubStageIds = group.Select(stage => stage.SubStageId).ToHashSet()
            })
            .ToArray();

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
                    .Where(x => x.Value.EffectiveSubStageId is not null
                        && x.Value.ProductionLineId == item.ProductionLineId
                        && item.SubStageIds.Contains(x.Value.EffectiveSubStageId.Value))
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
                stage.DepartmentId,
                FactoryId = stage.MainStage!.Department!.FactoryId
            })
            .ToArrayAsync(cancellationToken);
        var activeSubStageIds = activeSubStages.Select(stage => stage.Id).ToArray();

        if (activeSubStageIds.Length == 0)
        {
            return Result<IReadOnlyCollection<SubStageAttendanceSummaryDto>>.Success([]);
        }

        var activeLines = await _dbContext.ProductionLines
            .AsNoTracking()
            .Where(line => line.IsActive && line.DepartmentId.HasValue)
            .Select(line => new { line.Id, DepartmentId = line.DepartmentId!.Value })
            .ToArrayAsync(cancellationToken);

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
                .Select(assignment => new { SubStageId = assignment.EffectiveSubStageId!.Value, WorkerId = pair.Key, assignment.ProductionLineId }))
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
        var assignedWorkerIdsByStageAndLine = effectiveParticipations
            .Where(item => item.ProductionLineId.HasValue)
            .GroupBy(item => (item.SubStageId, ProductionLineId: item.ProductionLineId!.Value))
            .ToDictionary(group => group.Key, group => group.Select(item => item.WorkerId).Distinct().ToArray());
        var assignedWorkerIdsByMainStageAndLine = effectiveParticipations
            .Where(item => item.ProductionLineId.HasValue)
            .Join(activeSubStages, item => item.SubStageId, stage => stage.Id,
                (item, stage) => new { stage.MainStageId, ProductionLineId = item.ProductionLineId!.Value, item.WorkerId })
            .Distinct()
            .GroupBy(item => (item.MainStageId, item.ProductionLineId))
            .ToDictionary(group => group.Key, group => group.Select(item => item.WorkerId).ToArray());
        var assignedWorkerIdsByProductionLine = effectiveParticipations
            .Where(item => item.ProductionLineId.HasValue)
            .Select(item => new { ScopeId = item.ProductionLineId!.Value, item.WorkerId })
            .Distinct()
            .GroupBy(item => item.ScopeId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.WorkerId).ToArray());
        var assignedWorkerIdsByDepartment = effectiveParticipations
            .Join(activeSubStages, item => item.SubStageId, stage => stage.Id, (item, stage) => new { ScopeId = stage.DepartmentId, item.WorkerId })
            .Distinct()
            .GroupBy(item => item.ScopeId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.WorkerId).ToArray());
        var assignedWorkerIdsByFactory = effectiveParticipations
            .Where(item => item.ProductionLineId.HasValue)
            .Join(_dbContext.ProductionLines.AsNoTracking(), item => item.ProductionLineId!.Value, line => line.Id, (item, line) => new { ScopeId = line.FactoryId, item.WorkerId })
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
        var departmentCounts = assignedWorkerIdsByDepartment.ToDictionary(
            pair => pair.Key,
            pair => CountDistinctAttendance(pair.Value, attendanceByWorker));
        var factoryCounts = assignedWorkerIdsByFactory.ToDictionary(
            pair => pair.Key,
            pair => CountDistinctAttendance(pair.Value, attendanceByWorker));
        var summaries = activeSubStages
            .Select(stage =>
            {
                var mainStage = mainStageCounts.GetValueOrDefault(stage.MainStageId, DistinctAttendanceCounts.Empty);
                var department = departmentCounts.GetValueOrDefault(stage.DepartmentId, DistinctAttendanceCounts.Empty);
                var factory = factoryCounts.GetValueOrDefault(stage.FactoryId, DistinctAttendanceCounts.Empty);
                return CreateSubStageAttendanceSummary(
                    stage.Id,
                    assignedWorkerIdsByStage.GetValueOrDefault(stage.Id, []),
                    attendanceByWorker) with
                {
                    MainStageDistinctAssignedWorkersCount = mainStage.Assigned,
                    MainStageDistinctPresentWorkersCount = mainStage.Present,
                    MainStageDistinctAbsentWorkersCount = mainStage.Absent,
                    DepartmentDistinctAssignedWorkersCount = department.Assigned,
                    DepartmentDistinctPresentWorkersCount = department.Present,
                    DepartmentDistinctAbsentWorkersCount = department.Absent,
                    FactoryDistinctAssignedWorkersCount = factory.Assigned,
                    FactoryDistinctPresentWorkersCount = factory.Present,
                    FactoryDistinctAbsentWorkersCount = factory.Absent,
                    ProductionLines = activeLines
                        .Where(line => line.DepartmentId == stage.DepartmentId)
                        .Select(line =>
                        {
                            var stageLineSummary = CreateSubStageAttendanceSummary(
                                stage.Id,
                                assignedWorkerIdsByStageAndLine.GetValueOrDefault((stage.Id, line.Id), []),
                                attendanceByWorker);
                            var stageLine = CountDistinctAttendance(
                                assignedWorkerIdsByStageAndLine.GetValueOrDefault((stage.Id, line.Id), []),
                                attendanceByWorker);
                            var mainStageLine = CountDistinctAttendance(
                                assignedWorkerIdsByMainStageAndLine.GetValueOrDefault((stage.MainStageId, line.Id), []),
                                attendanceByWorker);
                            var productionLine = productionLineCounts.GetValueOrDefault(line.Id, DistinctAttendanceCounts.Empty);
                            return new ProductionLineAttendanceSummaryDto(
                                line.Id,
                                stageLine.Assigned,
                                stageLine.Present,
                                stageLine.Absent,
                                stageLineSummary.AttendanceDataStatus,
                                stageLineSummary.AttendanceStatus,
                                mainStageLine.Assigned,
                                mainStageLine.Present,
                                mainStageLine.Absent,
                                productionLine.Assigned,
                                productionLine.Present,
                                productionLine.Absent);
                        })
                        .ToArray()
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
