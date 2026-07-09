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
            Status = StageReadinessSnapshot.ReadinessFromPercent(counts.ReadinessPercent),
            CalculatedAtUtc = asOf
        });
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

        var relevantWorkers = workerIds ?? attendanceByWorker.Keys.ToArray();

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
        var readyCount = attendanceByWorker.Count == 0 ? assignedWorkers : present;
        var readinessPercent = StageReadinessSnapshot.CalculateReadinessPercent(requiredWorkers, readyCount, late, absent, unassignedWorkers);

        return new ReadinessCountResult(
            requiredWorkers,
            assignedWorkers,
            present,
            late,
            absent,
            unassignedWorkers,
            readinessPercent);
    }

    private sealed record ReadinessCountResult(
        int RequiredWorkers,
        int AssignedWorkers,
        int PresentWorkers,
        int LateWorkers,
        int AbsentWorkers,
        int UnassignedWorkers,
        decimal ReadinessPercent);
}
