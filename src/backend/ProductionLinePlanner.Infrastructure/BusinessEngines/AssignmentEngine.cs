using System.Data;
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
    private const int MaxReasonLength = 500;

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
            .AnyAsync(x => x.Id == workerId && x.IsActive && x.EmploymentStatus == EmploymentStatus.Active, cancellationToken);

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
                    AssignmentId: tempAssignment.Id,
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
                    AssignmentId: defaultAssignment.Id,
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
                AssignmentId: null,
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
        var actorValidationResult = ValidateActor(actorUserId);
        if (actorValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(actorValidationResult.Error!);
        }

        var workerIdValidationResult = ValidateRequiredGuid(request.WorkerId, nameof(request.WorkerId));
        if (workerIdValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(workerIdValidationResult.Error!);
        }

        var subStageIdValidationResult = ValidateRequiredGuid(request.SubStageId, nameof(request.SubStageId));
        if (subStageIdValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(subStageIdValidationResult.Error!);
        }

        if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length > MaxReasonLength)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", $"Reason must be at most {MaxReasonLength} characters."));
        }

        if ((await ResolveActiveWorkerAsync(request.WorkerId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "Worker not found or inactive."));
        }

        if ((await ResolveActiveSubStageAsync(request.SubStageId, cancellationToken)).IsFailure)
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

        if (currentDefault is not null && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "سبب تغيير التعيين الدائم مطلوب عند نقل العامل من مرحلة دائمة قائمة."));
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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "The worker assignment changed while it was being saved. Refresh and try again."));
        }

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

    public async Task<Result<AssignmentActionResultDto>> RemoveDefaultAssignmentAsync(
        Guid workerId,
        Guid subStageId,
        string reason,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        var actorValidation = ValidateActor(actorUserId);
        if (actorValidation.IsFailure) return Result<AssignmentActionResultDto>.Failure(actorValidation.Error!);
        var workerValidation = ValidateRequiredGuid(workerId, nameof(workerId));
        if (workerValidation.IsFailure) return Result<AssignmentActionResultDto>.Failure(workerValidation.Error!);
        var stageValidation = ValidateRequiredGuid(subStageId, nameof(subStageId));
        if (stageValidation.IsFailure) return Result<AssignmentActionResultDto>.Failure(stageValidation.Error!);
        var reasonValidation = ValidateReason(reason, nameof(reason));
        if (reasonValidation.IsFailure) return Result<AssignmentActionResultDto>.Failure(reasonValidation.Error!);

        var assignment = await _dbContext.WorkerDefaultAssignments
            .SingleOrDefaultAsync(x => x.WorkerId == workerId && x.SubStageId == subStageId && x.IsActive, cancellationToken);
        if (assignment is null)
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "An active default assignment for this worker and stage was not found."));

        var now = DateTime.UtcNow;
        var before = new { assignment.Id, assignment.WorkerId, assignment.SubStageId, assignment.IsActive, assignment.AssignedAt };
        assignment.Deactivate(now);
        _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
            id: Guid.NewGuid(),
            workerId: assignment.WorkerId,
            fromSubStageId: assignment.SubStageId,
            toSubStageId: null,
            assignmentType: AssignmentType.Default.ToString(),
            actionType: TimelineActionCancel,
            reason: reason.Trim(),
            startAtUtc: assignment.AssignedAt,
            endAtUtc: now,
            performedByUserId: actorUserId,
            isAutomatic: false,
            relatedTemporaryAssignmentId: null,
            replacementForWorkerId: null,
            createdAtUtc: now));

        await _auditEngine.RecordAsync(
            actorUserId,
            AuditActionType.Cancel,
            nameof(WorkerDefaultAssignment),
            assignment.Id.ToString(),
            before,
            new { assignment.Id, assignment.WorkerId, assignment.SubStageId, assignment.IsActive, assignment.UpdatedAtUtc },
            requestMeta,
            cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "The worker assignment changed while it was being saved. Refresh and try again."));
        }

        return Result<AssignmentActionResultDto>.Success(new AssignmentActionResultDto
        {
            AssignmentId = assignment.Id,
            WorkerId = assignment.WorkerId,
            SubStageId = assignment.SubStageId,
            AssignmentType = AssignmentType.Default.ToString(),
            StartsAtUtc = assignment.AssignedAt,
            IsCreated = false
        });
    }

    public async Task<Result<AssignmentActionResultDto>> CreateTemporaryAssignmentAsync(
        CreateTemporaryAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        var actorValidationResult = ValidateActor(actorUserId);
        if (actorValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(actorValidationResult.Error!);
        }

        var workerIdValidationResult = ValidateRequiredGuid(request.WorkerId, nameof(request.WorkerId));
        if (workerIdValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(workerIdValidationResult.Error!);
        }

        var fromSubStageValidationResult = ValidateRequiredGuid(request.FromSubStageId, nameof(request.FromSubStageId));
        if (fromSubStageValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(fromSubStageValidationResult.Error!);
        }

        var toSubStageValidationResult = ValidateRequiredGuid(request.ToSubStageId, nameof(request.ToSubStageId));
        if (toSubStageValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(toSubStageValidationResult.Error!);
        }

        if (request.FromSubStageId == request.ToSubStageId)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "FromSubStageId must differ from ToSubStageId."));
        }

        var reasonValidationResult = ValidateReason(request.Reason, nameof(request.Reason));
        if (reasonValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(reasonValidationResult.Error!);
        }

        var requestWindowValidationResult = ValidateTemporaryWindow(request.StartAtUtc, request.EndAtUtc);
        if (requestWindowValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(requestWindowValidationResult.Error!);
        }

        var now = DateTime.UtcNow;
        if ((await ResolveActiveWorkerAsync(request.WorkerId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "Worker not found or inactive."));
        }

        if ((await ResolveActiveSubStageAsync(request.FromSubStageId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "FromSubStage is invalid or inactive."));
        }

        if ((await ResolveActiveSubStageAsync(request.ToSubStageId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "ToSubStage is invalid or inactive."));
        }

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var sourceAssignment = await ResolveCurrentAssignmentsAsync([request.WorkerId], request.StartAtUtc, cancellationToken);
            if (sourceAssignment.IsFailure || !sourceAssignment.Value!.TryGetValue(request.WorkerId, out var effectiveAssignment) || effectiveAssignment.EffectiveSubStageId != request.FromSubStageId)
            {
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "العامل لم يكن معينًا فعليًا بالمرحلة المصدر عند وقت النقل."));
            }

            var overlapValidationResult = await ValidateTemporaryOverlapAsync(request.WorkerId, request.StartAtUtc, request.EndAtUtc, null, cancellationToken);
            if (overlapValidationResult.IsFailure)
            {
                return Result<AssignmentActionResultDto>.Failure(overlapValidationResult.Error!);
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
                before: null,
                after: entity,
                requestMeta: requestMeta);

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

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
        catch (DbUpdateException)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "تعذر حفظ النقل المؤقت بسبب تعارض متزامن. حدّث البيانات وحاول مرة أخرى."));
        }
    }

    public async Task<Result<AssignmentActionResultDto>> CreateReplacementAssignmentAsync(
        CreateReplacementAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        var actorValidationResult = ValidateActor(actorUserId);
        if (actorValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(actorValidationResult.Error!);
        }

        var replacementWorkerIdValidationResult = ValidateRequiredGuid(request.ReplacementWorkerId, nameof(request.ReplacementWorkerId));
        if (replacementWorkerIdValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(replacementWorkerIdValidationResult.Error!);
        }

        var replacedWorkerIdValidationResult = ValidateRequiredGuid(request.ReplacedWorkerId, nameof(request.ReplacedWorkerId));
        if (replacedWorkerIdValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(replacedWorkerIdValidationResult.Error!);
        }

        if (request.ReplacementWorkerId == request.ReplacedWorkerId)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "Replacement worker must differ from replaced worker."));
        }

        var subStageIdValidationResult = ValidateRequiredGuid(request.SubStageId, nameof(request.SubStageId));
        if (subStageIdValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(subStageIdValidationResult.Error!);
        }

        var reasonValidationResult = ValidateReason(request.Reason, nameof(request.Reason));
        if (reasonValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(reasonValidationResult.Error!);
        }

        var requestWindowValidationResult = ValidateTemporaryWindow(request.StartAtUtc, request.EndAtUtc);
        if (requestWindowValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(requestWindowValidationResult.Error!);
        }

        var now = DateTime.UtcNow;

        if ((await ResolveActiveWorkerAsync(request.ReplacementWorkerId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "Replacement worker not found or inactive."));
        }

        if ((await ResolveActiveWorkerAsync(request.ReplacedWorkerId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "Replaced worker not found or inactive."));
        }

        if ((await ResolveActiveSubStageAsync(request.SubStageId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "SubStage not found or inactive."));
        }

        var replacedWorkerDefault = await _dbContext.WorkerDefaultAssignments
            .AsNoTracking()
            .Where(x => x.WorkerId == request.ReplacedWorkerId && x.IsActive)
            .OrderByDescending(x => x.AssignedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (replacedWorkerDefault is null)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "Replaced worker default assignment is required."));
        }

        if (replacedWorkerDefault.SubStageId != request.SubStageId)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "Replaced worker default assignment is in a different sub-stage."));
        }

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var activeReplacedAssignment = await _dbContext.WorkerDefaultAssignments
                .AsNoTracking()
                .Where(x => x.WorkerId == request.ReplacedWorkerId && x.IsActive)
                .OrderByDescending(x => x.AssignedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (activeReplacedAssignment is null || activeReplacedAssignment.SubStageId != request.SubStageId)
            {
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "تم تغيير تعيين العامل المستبدَل قبل حفظ البديل."));
            }

            var sourceAssignment = await ResolveCurrentAssignmentsAsync([request.ReplacementWorkerId], request.StartAtUtc, cancellationToken);
            if (sourceAssignment.IsFailure || !sourceAssignment.Value!.TryGetValue(request.ReplacementWorkerId, out var effectiveAssignment) || !effectiveAssignment.EffectiveSubStageId.HasValue)
            {
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "يجب أن يكون العامل البديل معينًا فعليًا بمرحلة مصدر عند وقت النقل."));
            }

            var overlapValidationResult = await ValidateTemporaryOverlapAsync(request.ReplacementWorkerId, request.StartAtUtc, request.EndAtUtc, null, cancellationToken);
            if (overlapValidationResult.IsFailure)
            {
                return Result<AssignmentActionResultDto>.Failure(overlapValidationResult.Error!);
            }

            var status = request.StartAtUtc <= now
                ? TempStatusActive
                : TempStatusScheduled;

            var fromSubStageId = effectiveAssignment.EffectiveSubStageId.Value;

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
            before: null,
            after: entity,
            requestMeta: requestMeta);

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

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
        catch (DbUpdateException)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "تعذر حفظ البديل المؤقت بسبب تعارض متزامن. حدّث البيانات وحاول مرة أخرى."));
        }
    }

    public async Task<Result<CancelTemporaryAssignmentResultDto>> CancelTemporaryAssignmentAsync(
        Guid assignmentId,
        string reason,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        var actorValidationResult = ValidateActor(actorUserId);
        if (actorValidationResult.IsFailure)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(actorValidationResult.Error!);
        }

        var assignmentIdValidationResult = ValidateRequiredGuid(assignmentId, nameof(assignmentId));
        if (assignmentIdValidationResult.IsFailure)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(assignmentIdValidationResult.Error!);
        }

        var reasonValidationResult = ValidateReason(reason, nameof(reason));
        if (reasonValidationResult.IsFailure)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(reasonValidationResult.Error!);
        }

        var now = DateTime.UtcNow;
        await FinalizeCompletedTemporaryAssignmentsAsync(now, cancellationToken);

        var assignment = await _dbContext.WorkerTemporaryAssignments
            .FirstOrDefaultAsync(
                x => x.Id == assignmentId &&
                     (x.Status == TempStatusScheduled || x.Status == TempStatusActive),
                cancellationToken);

        if (assignment is null)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(new Error("NotFound", "لم يتم العثور على تعيين مؤقت نشط لإلغائه."));
        }

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
            reason: reason.Trim(),
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
            after: new { assignment.Id, assignment.Status, CancelReason = reason.Trim(), CancelledAtUtc = now },
            requestMeta: requestMeta);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(new Error("Conflict", "تم تعديل التعيين المؤقت بواسطة مستخدم آخر. حدّث البيانات وحاول مرة أخرى."));
        }

        return Result<CancelTemporaryAssignmentResultDto>.Success(new CancelTemporaryAssignmentResultDto
        {
            AssignmentId = assignment.Id,
            CancelledAt = now,
            Status = TempStatusCancelled
        });
    }

    public async Task<Result<AssignmentActionResultDto>> MoveCurrentAssignmentAsync(
        MoveCurrentWorkerAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var validation in new[]
                 {
                     ValidateActor(actorUserId),
                     ValidateRequiredGuid(request.WorkerId, nameof(request.WorkerId)),
                     ValidateRequiredGuid(request.SourceAssignmentId, nameof(request.SourceAssignmentId)),
                     ValidateRequiredGuid(request.FromSubStageId, nameof(request.FromSubStageId)),
                     ValidateRequiredGuid(request.ToSubStageId, nameof(request.ToSubStageId)),
                     ValidateReason(request.Reason, nameof(request.Reason))
                 })
        {
            if (validation.IsFailure) return Result<AssignmentActionResultDto>.Failure(validation.Error!);
        }

        if (request.FromSubStageId == request.ToSubStageId)
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "يجب أن تختلف مرحلة المصدر عن مرحلة النقل."));
        if (request.EffectiveAtUtc == default || request.EffectiveAtUtc < DateTime.UtcNow.AddMinutes(-5))
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "وقت سريان النقل يجب أن يكون الوقت الحالي أو وقتًا مستقبليًا قريبًا."));
        if (request.TemporaryEndAtUtc.HasValue)
        {
            var window = ValidateTemporaryWindow(request.EffectiveAtUtc, request.TemporaryEndAtUtc.Value);
            if (window.IsFailure) return Result<AssignmentActionResultDto>.Failure(window.Error!);
        }

        if ((await ResolveActiveWorkerAsync(request.WorkerId, cancellationToken)).IsFailure ||
            (await ResolveActiveSubStageAsync(request.FromSubStageId, cancellationToken)).IsFailure ||
            (await ResolveActiveSubStageAsync(request.ToSubStageId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "العامل أو مرحلة النقل غير نشطين."));
        }

        var now = DateTime.UtcNow;
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var resolved = await ResolveCurrentAssignmentsAsync([request.WorkerId], request.EffectiveAtUtc, cancellationToken);
            if (resolved.IsFailure || !resolved.Value!.TryGetValue(request.WorkerId, out var current) ||
                current.AssignmentId != request.SourceAssignmentId || current.EffectiveSubStageId != request.FromSubStageId)
            {
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "تم تغيير تعيين العامل قبل تنفيذ النقل. حدّث المرحلة وحاول مرة أخرى."));
            }

            if (current.AssignmentType == AssignmentType.Default)
            {
                if (!request.TemporaryEndAtUtc.HasValue && request.EffectiveAtUtc > now.AddSeconds(30))
                {
                    return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "النقل الحالي الدائم يسري الآن فقط. استخدم النقل المؤقت لوقت مستقبلي."));
                }
                var defaultAssignment = await _dbContext.WorkerDefaultAssignments.SingleOrDefaultAsync(
                    x => x.Id == request.SourceAssignmentId && x.WorkerId == request.WorkerId && x.SubStageId == request.FromSubStageId && x.IsActive,
                    cancellationToken);
                if (defaultAssignment is null)
                    return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "لم يعد التعيين الحالي صالحًا للنقل."));

                if (request.TemporaryEndAtUtc.HasValue)
                {
                    var overlap = await ValidateTemporaryOverlapAsync(request.WorkerId, request.EffectiveAtUtc, request.TemporaryEndAtUtc.Value, null, cancellationToken);
                    if (overlap.IsFailure) return Result<AssignmentActionResultDto>.Failure(overlap.Error!);
                    var temporary = CreateTemporaryEntity(request.WorkerId, request.FromSubStageId, request.ToSubStageId, request.EffectiveAtUtc, request.TemporaryEndAtUtc.Value, actorUserId, request.Reason, now);
                    _dbContext.WorkerTemporaryAssignments.Add(temporary);
                    AddAssignmentTimeline(temporary, TimelineActionCreate, request.Reason, actorUserId, now);
                    await _auditEngine.RecordAsync(actorUserId, AuditActionType.Create, nameof(WorkerTemporaryAssignment), temporary.Id.ToString(), null, temporary, requestMeta, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                    return ToAssignmentResult(temporary);
                }

                var before = new { defaultAssignment.Id, defaultAssignment.WorkerId, defaultAssignment.SubStageId, defaultAssignment.IsActive };
                defaultAssignment.Deactivate(now);
                var destination = new WorkerDefaultAssignment(Guid.NewGuid(), request.WorkerId, request.ToSubStageId, actorUserId, now, request.Reason, true, now);
                _dbContext.WorkerDefaultAssignments.Add(destination);
                _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(Guid.NewGuid(), request.WorkerId, request.FromSubStageId, request.ToSubStageId, AssignmentType.Default.ToString(), TimelineActionUpdate, request.Reason.Trim(), now, null, actorUserId, false, null, null, now));
                await _auditEngine.RecordAsync(actorUserId, AuditActionType.Update, nameof(WorkerDefaultAssignment), defaultAssignment.Id.ToString(), before, destination, requestMeta, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return Result<AssignmentActionResultDto>.Success(new AssignmentActionResultDto { AssignmentId = destination.Id, WorkerId = destination.WorkerId, SubStageId = destination.SubStageId, AssignmentType = AssignmentType.Default.ToString(), StartsAtUtc = destination.AssignedAt, Status = "Active", IsCreated = true });
            }

            var sourceTemporary = await _dbContext.WorkerTemporaryAssignments.SingleOrDefaultAsync(
                x => x.Id == request.SourceAssignmentId && x.WorkerId == request.WorkerId &&
                     (x.Status == TempStatusActive || x.Status == TempStatusScheduled) &&
                     x.StartAtUtc <= request.EffectiveAtUtc && x.EndAtUtc > request.EffectiveAtUtc &&
                     x.ToSubStageId == request.FromSubStageId,
                cancellationToken);
            if (sourceTemporary is null)
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "لم يعد النقل المؤقت المصدر ساريًا عند وقت النقل."));

            var destinationEnd = request.TemporaryEndAtUtc ?? sourceTemporary.EndAtUtc;
            var temporalWindow = ValidateTemporaryWindow(request.EffectiveAtUtc, destinationEnd);
            if (temporalWindow.IsFailure) return Result<AssignmentActionResultDto>.Failure(temporalWindow.Error!);
            var overlapValidation = await ValidateTemporaryOverlapAsync(request.WorkerId, request.EffectiveAtUtc, destinationEnd, sourceTemporary.Id, cancellationToken);
            if (overlapValidation.IsFailure) return Result<AssignmentActionResultDto>.Failure(overlapValidation.Error!);

            _dbContext.Entry(sourceTemporary).Property(nameof(WorkerTemporaryAssignment.Status)).CurrentValue = TempStatusCancelled;
            _dbContext.Entry(sourceTemporary).Property(nameof(WorkerTemporaryAssignment.EndAtUtc)).CurrentValue = request.EffectiveAtUtc;
            _dbContext.Entry(sourceTemporary).Property(nameof(WorkerTemporaryAssignment.UpdatedAtUtc)).CurrentValue = now;
            _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(Guid.NewGuid(), request.WorkerId, sourceTemporary.FromSubStageId, sourceTemporary.ToSubStageId, sourceTemporary.AssignmentType.ToString(), TimelineActionCancel, request.Reason.Trim(), sourceTemporary.StartAtUtc, request.EffectiveAtUtc, actorUserId, false, sourceTemporary.Id, sourceTemporary.ReplacementForWorkerId, now));

            var replacement = CreateTemporaryEntity(request.WorkerId, request.FromSubStageId, request.ToSubStageId, request.EffectiveAtUtc, destinationEnd, actorUserId, request.Reason, now);
            _dbContext.WorkerTemporaryAssignments.Add(replacement);
            AddAssignmentTimeline(replacement, TimelineActionCreate, request.Reason, actorUserId, now);
            await _auditEngine.RecordAsync(actorUserId, AuditActionType.Update, nameof(WorkerTemporaryAssignment), sourceTemporary.Id.ToString(), sourceTemporary, replacement, requestMeta, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return ToAssignmentResult(replacement);
        }
        catch (DbUpdateException)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "تعذر نقل العامل بسبب تعارض متزامن. حدّث البيانات وحاول مرة أخرى."));
        }
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
            .Where(x => x.IsActive && x.EmploymentStatus == EmploymentStatus.Active)
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

    private static Result ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "بيانات المستخدم مطلوبة لتنفيذ التعيين."));
        }

        return Result.Success();
    }

    private static Result ValidateRequiredGuid(Guid value, string fieldName)
    {
        if (value == Guid.Empty)
        {
            return Result.Failure(new Error("ValidationError", $"الحقل {fieldName} مطلوب."));
        }

        return Result.Success();
    }

    private static Result ValidateReason(string? reason, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(new Error("ValidationError", "سبب الإجراء مطلوب."));
        }

        if (reason.Length > MaxReasonLength)
        {
            return Result.Failure(new Error("ValidationError", $"سبب الإجراء يجب ألا يتجاوز {MaxReasonLength} حرفًا."));
        }

        return Result.Success();
    }

    private static Result ValidateTemporaryWindow(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (startAtUtc == default || endAtUtc == default)
        {
            return Result.Failure(new Error("ValidationError", "وقت بداية ونهاية النقل المؤقت مطلوبان."));
        }

        if (startAtUtc >= endAtUtc)
        {
            return Result.Failure(new Error("ValidationError", "وقت نهاية النقل المؤقت يجب أن يكون بعد وقت البداية."));
        }

        return Result.Success();
    }

    private async Task<Result> ValidateTemporaryOverlapAsync(
        Guid workerId,
        DateTime startAtUtc,
        DateTime endAtUtc,
        Guid? excludedAssignmentId,
        CancellationToken cancellationToken)
    {
        var hasConflict = await _dbContext.WorkerTemporaryAssignments.AnyAsync(x =>
            x.WorkerId == workerId &&
            (!excludedAssignmentId.HasValue || x.Id != excludedAssignmentId.Value) &&
            (x.Status == TempStatusScheduled || x.Status == TempStatusActive) &&
            x.StartAtUtc < endAtUtc &&
            x.EndAtUtc > startAtUtc,
            cancellationToken);

        if (hasConflict)
        {
            return Result.Failure(new Error("Conflict", "للعامل تعيين مؤقت متداخل في هذا الوقت."));
        }

        return Result.Success();
    }

    private static WorkerTemporaryAssignment CreateTemporaryEntity(
        Guid workerId,
        Guid fromSubStageId,
        Guid toSubStageId,
        DateTime startAtUtc,
        DateTime endAtUtc,
        Guid actorUserId,
        string reason,
        DateTime now)
        => new(Guid.NewGuid(), workerId, fromSubStageId, toSubStageId, startAtUtc, endAtUtc, actorUserId, reason, null, startAtUtc <= now ? TempStatusActive : TempStatusScheduled, now);

    private void AddAssignmentTimeline(WorkerTemporaryAssignment assignment, string action, string reason, Guid actorUserId, DateTime now)
        => _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
            Guid.NewGuid(), assignment.WorkerId, assignment.FromSubStageId, assignment.ToSubStageId,
            assignment.AssignmentType.ToString(), action, reason.Trim(), assignment.StartAtUtc,
            assignment.EndAtUtc, actorUserId, false, assignment.Id, assignment.ReplacementForWorkerId, now));

    private static Result<AssignmentActionResultDto> ToAssignmentResult(WorkerTemporaryAssignment assignment)
        => Result<AssignmentActionResultDto>.Success(new AssignmentActionResultDto
        {
            AssignmentId = assignment.Id,
            WorkerId = assignment.WorkerId,
            FromSubStageId = assignment.FromSubStageId,
            ToSubStageId = assignment.ToSubStageId,
            AssignmentType = assignment.AssignmentType.ToString(),
            StartsAtUtc = assignment.StartAtUtc,
            EndsAtUtc = assignment.EndAtUtc,
            Status = assignment.Status,
            IsCreated = true
        });

    private async Task<Result<Worker>> ResolveActiveWorkerAsync(Guid workerId, CancellationToken cancellationToken)
    {
        var worker = await _dbContext.Workers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == workerId && x.IsActive && x.EmploymentStatus == EmploymentStatus.Active, cancellationToken);

        if (worker is null)
        {
            return Result<Worker>.Failure(new Error("NotFound", "Worker not found or inactive."));
        }

        return Result<Worker>.Success(worker);
    }

    private async Task<Result<SubStage>> ResolveActiveSubStageAsync(Guid subStageId, CancellationToken cancellationToken)
    {
        var subStage = await _dbContext.SubStages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);

        if (subStage is null)
        {
            return Result<SubStage>.Failure(new Error("NotFound", "SubStage not found or inactive."));
        }

        return Result<SubStage>.Success(subStage);
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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error("Conflict", "تمت معالجة انتهاء التعيين المؤقت بواسطة مستخدم آخر."));
        }
        return Result.Success();
    }
}
