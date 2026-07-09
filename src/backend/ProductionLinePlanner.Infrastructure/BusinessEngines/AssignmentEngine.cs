using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class AssignmentEngine : IAssignmentEngine
{
    private const string TimelineActionCreate = "Create";
    private const string TimelineActionUpdate = "Update";
    private const string TimelineActionCancel = "Cancel";
    private const string TimelineActionAutoReturn = "AutoReturn";
    private const string TempStatusActive = "Active";
    private const string TempStatusScheduled = "Scheduled";
    private const string TempStatusCancelled = "Cancelled";
    private const string TempStatusCompleted = "Completed";

    private readonly AppDbContext _dbContext;
    private readonly IAuditEngine _auditEngine;

    public AssignmentEngine(AppDbContext dbContext, IAuditEngine auditEngine)
    {
        _dbContext = dbContext;
        _auditEngine = auditEngine;
    }

    public async Task<Result<CurrentWorkerAssignmentDto>> GetCurrentAssignmentAsync(
        Guid workerId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (workerId == Guid.Empty)
        {
            return Result<CurrentWorkerAssignmentDto>.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        var workerExists = await _dbContext.Workers
            .AsNoTracking()
            .AnyAsync(x => x.Id == workerId && x.IsActive, cancellationToken);

        if (!workerExists)
        {
            return Result<CurrentWorkerAssignmentDto>.Failure(new Error("NotFound", "Worker not found."));
        }

        var asOf = asOfUtc ?? DateTime.UtcNow;
        var assignments = await ResolveCurrentAssignmentsAsync([workerId], asOf, cancellationToken);

        if (assignments.IsFailure)
        {
            return Result<CurrentWorkerAssignmentDto>.Failure(assignments.Error!);
        }

        var assignment = assignments.Value!.GetValueOrDefault(workerId);
        if (assignment is null)
        {
            return Result<CurrentWorkerAssignmentDto>.Success(new CurrentWorkerAssignmentDto
            {
                WorkerId = workerId
            });
        }

        return Result<CurrentWorkerAssignmentDto>.Success(new CurrentWorkerAssignmentDto
        {
            WorkerId = assignment.WorkerId,
            EffectiveSubStageId = assignment.EffectiveSubStageId,
            AssignmentType = assignment.AssignmentType,
            StartedAtUtc = assignment.StartsAtUtc,
            EndsAtUtc = assignment.EndsAtUtc,
            FromSubStageId = assignment.FromSubStageId,
            ToSubStageId = assignment.ToSubStageId,
            ReplacementForWorkerId = assignment.ReplacementForWorkerId
        });
    }

    public async Task<Result<Dictionary<Guid, WorkerAssignmentState>>> ResolveCurrentAssignmentsAsync(
        IEnumerable<Guid> workerIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var uniqueWorkerIds = workerIds.Distinct().ToArray();
        if (uniqueWorkerIds.Length == 0)
        {
            return Result<Dictionary<Guid, WorkerAssignmentState>>.Success(new Dictionary<Guid, WorkerAssignmentState>());
        }

        var finalizeResult = await FinalizeCompletedTemporaryAssignmentsAsync(asOfUtc, cancellationToken);
        if (finalizeResult.IsFailure)
        {
            return Result<Dictionary<Guid, WorkerAssignmentState>>.Failure(finalizeResult.Error!);
        }

        var defaultAssignments = await _dbContext.WorkerDefaultAssignments
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

        var activeTemporaryAssignments = await _dbContext.WorkerTemporaryAssignments
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

        var results = new Dictionary<Guid, WorkerAssignmentState>(uniqueWorkerIds.Length);
        foreach (var workerId in uniqueWorkerIds)
        {
            if (temporaryByWorker.TryGetValue(workerId, out var tempAssignment))
            {
                results[workerId] = new WorkerAssignmentState(
                    WorkerId: workerId,
                    AssignmentType: tempAssignment.ReplacementForWorkerId is null ? AssignmentType.Temporary : AssignmentType.Replacement,
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
                results[workerId] = new WorkerAssignmentState(
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

            results[workerId] = new WorkerAssignmentState(
                WorkerId: workerId,
                AssignmentType: null,
                StartsAtUtc: null,
                EndsAtUtc: null,
                EffectiveSubStageId: null,
                FromSubStageId: null,
                ToSubStageId: null,
                ReplacementForWorkerId: null);
        }

        return Result<Dictionary<Guid, WorkerAssignmentState>>.Success(results);
    }

    public async Task<Result<AssignmentActionResultDto>> CreateOrUpdateDefaultAssignmentAsync(
        CreateDefaultAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (request.WorkerId == Guid.Empty)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        if (request.SubStageId == Guid.Empty)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "SubStageId is required."));
        }

        var worker = await _dbContext.Workers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.WorkerId && x.IsActive, cancellationToken);

        if (worker is null)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "Worker not found or inactive."));
        }

        var subStage = await _dbContext.SubStages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.SubStageId && x.IsActive, cancellationToken);

        if (subStage is null)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "SubStage not found or inactive."));
        }

        var now = DateTime.UtcNow;

        var currentDefaults = await _dbContext.WorkerDefaultAssignments
            .Where(x => x.WorkerId == request.WorkerId && x.IsActive)
            .OrderByDescending(x => x.AssignedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        var currentDefault = currentDefaults.FirstOrDefault();
        if (currentDefault is not null)
        {
            foreach (var duplicate in currentDefaults.Skip(1))
            {
                _dbContext.Entry(duplicate).Property(nameof(WorkerDefaultAssignment.IsActive)).CurrentValue = false;
                _dbContext.Entry(duplicate).Property(nameof(WorkerDefaultAssignment.UpdatedAtUtc)).CurrentValue = now;
            }
        }

        if (currentDefault is not null && currentDefault.SubStageId == request.SubStageId)
        {
            await _auditEngine.RecordAsync(
                actorUserId,
                AuditActionType.Update,
                nameof(WorkerDefaultAssignment),
                currentDefault.Id.ToString(),
                before: currentDefault,
                requestMeta: requestMeta);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<AssignmentActionResultDto>.Success(new AssignmentActionResultDto
            {
                AssignmentId = currentDefault.Id,
                WorkerId = currentDefault.WorkerId,
                SubStageId = currentDefault.SubStageId,
                AssignmentType = AssignmentType.Default.ToString(),
                StartsAtUtc = currentDefault.AssignedAt,
                IsCreated = false
            });
        }

        Guid? previousSubStageId = currentDefault?.SubStageId;
        if (currentDefault is not null)
        {
            _dbContext.Entry(currentDefault).Property(nameof(WorkerDefaultAssignment.IsActive)).CurrentValue = false;
            _dbContext.Entry(currentDefault).Property(nameof(WorkerDefaultAssignment.UpdatedAtUtc)).CurrentValue = now;
        }

        var assignment = new WorkerDefaultAssignment(
            id: Guid.NewGuid(),
            workerId: request.WorkerId,
            subStageId: request.SubStageId,
            assignedByUserId: actorUserId,
            assignedAtUtc: now,
            reason: request.Reason,
            isActive: true,
            createdAtUtc: now);

        _dbContext.WorkerDefaultAssignments.Add(assignment);

        var timelineAction = currentDefault is null ? TimelineActionCreate : TimelineActionUpdate;
        _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
            id: Guid.NewGuid(),
            workerId: request.WorkerId,
            fromSubStageId: previousSubStageId,
            toSubStageId: request.SubStageId,
            assignmentType: AssignmentType.Default.ToString(),
            actionType: timelineAction,
            reason: request.Reason,
            startAtUtc: now,
            endAtUtc: null,
            performedByUserId: actorUserId,
            isAutomatic: false,
            relatedTemporaryAssignmentId: null,
            replacementForWorkerId: null,
            createdAtUtc: now));

        await _auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(WorkerDefaultAssignment),
            assignment.Id.ToString(),
            before: assignment,
            requestMeta: requestMeta);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AssignmentActionResultDto>.Success(new AssignmentActionResultDto
        {
            AssignmentId = assignment.Id,
            WorkerId = assignment.WorkerId,
            SubStageId = assignment.SubStageId,
            AssignmentType = assignment.AssignmentType.ToString(),
            StartsAtUtc = assignment.AssignedAt,
            IsCreated = true
        });
    }

    public async Task<Result<AssignmentActionResultDto>> CreateTemporaryAssignmentAsync(
        CreateTemporaryAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (request.WorkerId == Guid.Empty)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        if (request.FromSubStageId == Guid.Empty || request.ToSubStageId == Guid.Empty)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "FromSubStageId and ToSubStageId are required."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "Reason is required."));
        }

        if (request.StartAtUtc >= request.EndAtUtc)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "EndAtUtc must be after StartAtUtc."));
        }

        var now = DateTime.UtcNow;
        var worker = await _dbContext.Workers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.WorkerId && x.IsActive, cancellationToken);

        if (worker is null)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "Worker not found or inactive."));
        }

        var validFromSubStage = await _dbContext.SubStages
            .AsNoTracking()
            .AnyAsync(x => x.Id == request.FromSubStageId && x.IsActive, cancellationToken);

        if (!validFromSubStage)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "FromSubStage is invalid or inactive."));
        }

        var validToSubStage = await _dbContext.SubStages
            .AsNoTracking()
            .AnyAsync(x => x.Id == request.ToSubStageId && x.IsActive, cancellationToken);

        if (!validToSubStage)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "ToSubStage is invalid or inactive."));
        }

        var hasConflict = await _dbContext.WorkerTemporaryAssignments.AnyAsync(x =>
            x.WorkerId == request.WorkerId &&
            (x.Status == TempStatusScheduled || x.Status == TempStatusActive) &&
            x.StartAtUtc < request.EndAtUtc &&
            x.EndAtUtc > request.StartAtUtc,
            cancellationToken);

        if (hasConflict)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "Worker has overlapping temporary assignment."));
        }

        var status = request.StartAtUtc <= now
            ? TempStatusActive
            : TempStatusScheduled;

        var entity = new WorkerTemporaryAssignment(
            id: Guid.NewGuid(),
            workerId: request.WorkerId,
            fromSubStageId: request.FromSubStageId,
            toSubStageId: request.ToSubStageId,
            startAtUtc: request.StartAtUtc,
            endAtUtc: request.EndAtUtc,
            assignedByUserId: actorUserId,
            reason: request.Reason,
            replacementForWorkerId: null,
            status: status,
            createdAtUtc: now);

        _dbContext.WorkerTemporaryAssignments.Add(entity);
        _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
            id: Guid.NewGuid(),
            workerId: request.WorkerId,
            fromSubStageId: entity.FromSubStageId,
            toSubStageId: entity.ToSubStageId,
            assignmentType: AssignmentType.Temporary.ToString(),
            actionType: TimelineActionCreate,
            reason: request.Reason,
            startAtUtc: request.StartAtUtc,
            endAtUtc: request.EndAtUtc,
            performedByUserId: actorUserId,
            isAutomatic: false,
            relatedTemporaryAssignmentId: null,
            replacementForWorkerId: null,
            createdAtUtc: now));

        await _auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(WorkerTemporaryAssignment),
            entity.Id.ToString(),
            before: entity,
            requestMeta: requestMeta);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AssignmentActionResultDto>.Success(new AssignmentActionResultDto
        {
            AssignmentId = entity.Id,
            WorkerId = entity.WorkerId,
            FromSubStageId = entity.FromSubStageId,
            ToSubStageId = entity.ToSubStageId,
            AssignmentType = entity.AssignmentType.ToString(),
            StartsAtUtc = entity.StartAtUtc,
            EndsAtUtc = entity.EndAtUtc,
            Status = entity.Status,
            IsCreated = true
        });
    }

    public async Task<Result<AssignmentActionResultDto>> CreateReplacementAssignmentAsync(
        CreateReplacementAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (request.ReplacementWorkerId == Guid.Empty || request.ReplacedWorkerId == Guid.Empty || request.SubStageId == Guid.Empty)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "ReplacementWorkerId, ReplacedWorkerId and SubStageId are required."));
        }

        if (request.ReplacementWorkerId == request.ReplacedWorkerId)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "Replacement worker must differ from replaced worker."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "Reason is required."));
        }

        if (request.StartAtUtc >= request.EndAtUtc)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "EndAtUtc must be after StartAtUtc."));
        }

        var now = DateTime.UtcNow;

        var replacementWorker = await _dbContext.Workers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.ReplacementWorkerId && x.IsActive, cancellationToken);

        if (replacementWorker is null)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "Replacement worker not found or inactive."));
        }

        var replacedWorker = await _dbContext.Workers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.ReplacedWorkerId && x.IsActive, cancellationToken);

        if (replacedWorker is null)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "Replaced worker not found or inactive."));
        }

        var subStage = await _dbContext.SubStages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.SubStageId && x.IsActive, cancellationToken);

        if (subStage is null)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "SubStage not found or inactive."));
        }

        var replacedWorkerDefault = await _dbContext.WorkerDefaultAssignments
            .AsNoTracking()
            .Where(x => x.WorkerId == request.ReplacedWorkerId && x.IsActive)
            .OrderByDescending(x => x.AssignedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (replacedWorkerDefault is not null && replacedWorkerDefault.SubStageId != request.SubStageId)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "Replaced worker default assignment is in a different sub-stage."));
        }

        var conflict = await _dbContext.WorkerTemporaryAssignments.AnyAsync(x =>
            x.WorkerId == request.ReplacementWorkerId &&
            (x.Status == TempStatusScheduled || x.Status == TempStatusActive) &&
            x.StartAtUtc < request.EndAtUtc &&
            x.EndAtUtc > request.StartAtUtc,
            cancellationToken);

        if (conflict)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "Replacement worker already has overlapping temporary assignment."));
        }

        var status = request.StartAtUtc <= now
            ? TempStatusActive
            : TempStatusScheduled;

        var fromSubStageId = replacedWorkerDefault?.SubStageId ?? request.SubStageId;

        var entity = new WorkerTemporaryAssignment(
            id: Guid.NewGuid(),
            workerId: request.ReplacementWorkerId,
            fromSubStageId: fromSubStageId,
            toSubStageId: request.SubStageId,
            startAtUtc: request.StartAtUtc,
            endAtUtc: request.EndAtUtc,
            assignedByUserId: actorUserId,
            reason: request.Reason,
            replacementForWorkerId: request.ReplacedWorkerId,
            status: status,
            createdAtUtc: now);

        _dbContext.WorkerTemporaryAssignments.Add(entity);
        _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
            id: Guid.NewGuid(),
            workerId: request.ReplacementWorkerId,
            fromSubStageId: fromSubStageId,
            toSubStageId: request.SubStageId,
            assignmentType: AssignmentType.Replacement.ToString(),
            actionType: TimelineActionCreate,
            reason: request.Reason,
            startAtUtc: request.StartAtUtc,
            endAtUtc: request.EndAtUtc,
            performedByUserId: actorUserId,
            isAutomatic: false,
            relatedTemporaryAssignmentId: null,
            replacementForWorkerId: request.ReplacedWorkerId,
            createdAtUtc: now));

        await _auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Create,
            nameof(WorkerTemporaryAssignment),
            entity.Id.ToString(),
            before: entity,
            requestMeta: requestMeta);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AssignmentActionResultDto>.Success(new AssignmentActionResultDto
        {
            AssignmentId = entity.Id,
            WorkerId = entity.WorkerId,
            FromSubStageId = entity.FromSubStageId,
            ToSubStageId = entity.ToSubStageId,
            AssignmentType = entity.AssignmentType.ToString(),
            ReplacementForWorkerId = entity.ReplacementForWorkerId,
            StartsAtUtc = entity.StartAtUtc,
            EndsAtUtc = entity.EndAtUtc,
            Status = entity.Status,
            IsCreated = true
        });
    }

    public async Task<Result<CancelTemporaryAssignmentResultDto>> CancelTemporaryAssignmentAsync(
        Guid assignmentId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        var assignment = await _dbContext.WorkerTemporaryAssignments
            .FirstOrDefaultAsync(
                x => x.Id == assignmentId &&
                     (x.Status == TempStatusScheduled || x.Status == TempStatusActive),
                cancellationToken);

        if (assignment is null)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(new Error("NotFound", "Temporary assignment not found."));
        }

        var now = DateTime.UtcNow;
        _dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.Status)).CurrentValue = TempStatusCancelled;
        _dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.EndAtUtc)).CurrentValue = now;
        _dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.UpdatedAtUtc)).CurrentValue = now;

        _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
            id: Guid.NewGuid(),
            workerId: assignment.WorkerId,
            fromSubStageId: assignment.FromSubStageId,
            toSubStageId: assignment.ToSubStageId,
            assignmentType: assignment.AssignmentType.ToString(),
            actionType: TimelineActionCancel,
            reason: assignment.Reason,
            startAtUtc: assignment.StartAtUtc,
            endAtUtc: now,
            performedByUserId: actorUserId,
            isAutomatic: false,
            relatedTemporaryAssignmentId: assignment.Id,
            replacementForWorkerId: assignment.ReplacementForWorkerId,
            createdAtUtc: now));

        await _auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Cancel,
            nameof(WorkerTemporaryAssignment),
            assignment.Id.ToString(),
            before: assignment,
            requestMeta: requestMeta);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<CancelTemporaryAssignmentResultDto>.Success(new CancelTemporaryAssignmentResultDto
        {
            AssignmentId = assignment.Id,
            CancelledAt = now,
            Status = TempStatusCancelled
        });
    }

    public async Task<Result<PagedResult<AssignmentTimelineDto>>> GetWorkerTimelineAsync(
        Guid workerId,
        int page = 1,
        int pageSize = 50,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 200)
        {
            return Result<PagedResult<AssignmentTimelineDto>>.Failure(new Error("ValidationError", "page and pageSize must be positive, pageSize max 200."));
        }

        var query = _dbContext.AssignmentTimelineEntries
            .AsNoTracking()
            .Where(x => x.WorkerId == workerId);

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.StartAtUtc >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x => x.StartAtUtc <= toDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var entries = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AssignmentTimelineDto
            {
                Id = x.Id,
                WorkerId = x.WorkerId,
                FromSubStageId = x.FromSubStageId,
                ToSubStageId = x.ToSubStageId,
                AssignmentType = x.AssignmentType,
                ActionType = x.ActionType,
                Reason = x.Reason,
                StartAtUtc = x.StartAtUtc,
                EndAtUtc = x.EndAtUtc,
                PerformedByUserId = x.PerformedByUserId,
                IsAutomatic = x.IsAutomatic,
                RelatedTemporaryAssignmentId = x.RelatedTemporaryAssignmentId,
                ReplacementForWorkerId = x.ReplacementForWorkerId,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Result<PagedResult<AssignmentTimelineDto>>.Success(PagedResult<AssignmentTimelineDto>.Success(entries, page, pageSize, totalCount));
    }

    public async Task<Result<SubStageCurrentWorkersDto>> GetSubStageWorkersAsync(
        Guid subStageId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (subStageId == Guid.Empty)
        {
            return Result<SubStageCurrentWorkersDto>.Failure(new Error("ValidationError", "SubStageId is required."));
        }

        if (!await _dbContext.SubStages.AnyAsync(x => x.Id == subStageId, cancellationToken))
        {
            return Result<SubStageCurrentWorkersDto>.Failure(new Error("NotFound", "SubStage not found."));
        }

        var asOf = asOfUtc ?? DateTime.UtcNow;
        var workers = await _dbContext.Workers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.FullName, x.EmployeeCode })
            .ToListAsync(cancellationToken);

        if (workers.Count == 0)
        {
            return Result<SubStageCurrentWorkersDto>.Success(new SubStageCurrentWorkersDto
            {
                SubStageId = subStageId,
                WorkersCount = 0,
                Items = []
            });
        }

        var assignments = await ResolveCurrentAssignmentsAsync(workers.Select(x => x.Id), asOf, cancellationToken);
        if (assignments.IsFailure)
        {
            return Result<SubStageCurrentWorkersDto>.Failure(assignments.Error!);
        }

        var items = workers
            .Where(x => assignments.Value!.TryGetValue(x.Id, out var assignment)
                        && assignment.EffectiveSubStageId == subStageId)
            .Select(x =>
            {
                var assignment = assignments.Value![x.Id];
                return new SubStageCurrentWorkerDto
                {
                    WorkerId = x.Id,
                    FullName = x.FullName,
                    EmployeeCode = x.EmployeeCode,
                    AssignmentType = assignment.AssignmentType ?? AssignmentType.Default,
                    FromSubStageId = assignment.FromSubStageId,
                    ReplacementForWorkerId = assignment.ReplacementForWorkerId
                };
            })
            .OrderBy(x => x.FullName)
            .ToArray();

        return Result<SubStageCurrentWorkersDto>.Success(new SubStageCurrentWorkersDto
        {
            SubStageId = subStageId,
            WorkersCount = items.Length,
            Items = items
        });
    }

    private async Task<Result> FinalizeCompletedTemporaryAssignmentsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var endedAssignments = await _dbContext.WorkerTemporaryAssignments
            .Where(x => (x.Status == TempStatusScheduled || x.Status == TempStatusActive) && x.EndAtUtc <= asOfUtc)
            .ToListAsync(cancellationToken);

        if (endedAssignments.Count == 0)
        {
            return Result.Success();
        }

        foreach (var assignment in endedAssignments)
        {
            _dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.Status)).CurrentValue = TempStatusCompleted;
            _dbContext.Entry(assignment).Property(nameof(WorkerTemporaryAssignment.UpdatedAtUtc)).CurrentValue = asOfUtc;
            _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
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

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
