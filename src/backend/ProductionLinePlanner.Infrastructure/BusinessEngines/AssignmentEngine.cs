using System.Data;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Notifications;
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
    private const string BulkStageSelectionRemovalReason = "إزالة من تحديد عمال المرحلة الجماعي";
    private const int MaxReasonLength = 500;

    // Historical temporary-assignment records remain readable, but no new
    // non-permanent movement is available from the active application flow.
    private static bool NonPermanentAssignmentsAreDisabled => true;
    private static Error NonPermanentAssignmentsDisabledError => new(
        "FeatureDisabled",
        "التسكين غير الدائم متوقف حاليًا. استخدم التسكين الدائم فقط.");

    private readonly AppDbContext _dbContext;
    private readonly IAuditEngine _auditEngine;
    private readonly IAssignmentNotificationDispatcher? _assignmentNotificationDispatcher;

    public AssignmentEngine(
        AppDbContext dbContext,
        IAuditEngine auditEngine,
        IAssignmentNotificationDispatcher? assignmentNotificationDispatcher = null)
    {
        _dbContext = dbContext;
        _auditEngine = auditEngine;
        _assignmentNotificationDispatcher = assignmentNotificationDispatcher;
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
        var effective = await ResolveEffectiveAssignmentsAsync(workerIds, asOfUtc, cancellationToken);
        if (effective.IsFailure)
            return Result<Dictionary<Guid, WorkerAssignmentState>>.Failure(effective.Error!);

        var results = effective.Value!.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .OrderByDescending(assignment => assignment.AssignmentType is AssignmentType.Temporary or AssignmentType.Replacement)
                .ThenByDescending(assignment => assignment.StartsAtUtc)
                .ThenByDescending(assignment => assignment.AssignmentId)
                .FirstOrDefault()
                ?? EmptyAssignment(pair.Key));
        return Result<Dictionary<Guid, WorkerAssignmentState>>.Success(results);
    }

    public async Task<Result<Dictionary<Guid, IReadOnlyCollection<WorkerAssignmentState>>>> ResolveEffectiveAssignmentsAsync(
        IEnumerable<Guid> workerIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var uniqueWorkerIds = workerIds.Distinct().ToArray();
        if (uniqueWorkerIds.Length == 0)
            return Result<Dictionary<Guid, IReadOnlyCollection<WorkerAssignmentState>>>.Success(new Dictionary<Guid, IReadOnlyCollection<WorkerAssignmentState>>());

        var defaultAssignments = await _dbContext.WorkerDefaultAssignments
            .AsNoTracking()
            .Where(x => uniqueWorkerIds.Contains(x.WorkerId) && x.IsActive)
            .Select(x => new { x.WorkerId, x.AssignedAt, x.Id, x.SubStageId, x.ProductionLineId })
            .ToListAsync(cancellationToken);

        var byWorker = uniqueWorkerIds.ToDictionary(
            workerId => workerId,
            _ => new List<WorkerAssignmentState>());

        foreach (var assignment in defaultAssignments
                     .OrderBy(x => x.AssignedAt)
                     .ThenBy(x => x.Id))
        {
            byWorker[assignment.WorkerId].Add(new WorkerAssignmentState(
                assignment.Id,
                assignment.WorkerId,
                AssignmentType.Default,
                assignment.AssignedAt,
                null,
                assignment.SubStageId,
                null,
                null,
                null,
                ProductionLineId: assignment.ProductionLineId));
        }

        return Result<Dictionary<Guid, IReadOnlyCollection<WorkerAssignmentState>>>.Success(
            byWorker.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyCollection<WorkerAssignmentState>)pair.Value
                    .OrderBy(assignment => assignment.EffectiveSubStageId)
                    .ThenBy(assignment => assignment.AssignmentType)
                    .ThenBy(assignment => assignment.AssignmentId)
                    .ToArray()));
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

        var lineIdValidationResult = ValidateRequiredGuid(request.ProductionLineId, nameof(request.ProductionLineId));
        if (lineIdValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(lineIdValidationResult.Error!);
        }

        if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length > MaxReasonLength)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", $"Reason must be at most {MaxReasonLength} characters."));
        }

        if ((await ResolveActiveWorkerAsync(request.WorkerId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("NotFound", "Worker not found or inactive."));
        }

        if ((await ResolveActiveLineStageContextAsync(request.ProductionLineId, request.SubStageId, cancellationToken)).IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "المرحلة لا تتبع قسم خط الإنتاج المحدد أو أنها غير نشطة."));
        }

        var now = DateTime.UtcNow;

        var currentDefault = await _dbContext.WorkerDefaultAssignments
            .SingleOrDefaultAsync(x => x.WorkerId == request.WorkerId && x.ProductionLineId == request.ProductionLineId && x.SubStageId == request.SubStageId && x.IsActive, cancellationToken);

        if (currentDefault is not null)
        {
            return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "العامل مشارك بالفعل في هذه المرحلة ولا يمكن إنشاء تعيين دائم مكرر."));
        }

        var assignment = new WorkerDefaultAssignment(
            id: Guid.NewGuid(),
            workerId: request.WorkerId,
            subStageId: request.SubStageId,
            assignedByUserId: actorUserId,
            assignedAtUtc: now,
            reason: request.Reason,
            isActive: true,
            createdAtUtc: now,
            productionLineId: request.ProductionLineId);

        _dbContext.WorkerDefaultAssignments.Add(assignment);

        _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
            id: Guid.NewGuid(),
            workerId: request.WorkerId,
            fromSubStageId: null,
            toSubStageId: request.SubStageId,
            assignmentType: AssignmentType.Default.ToString(),
            actionType: TimelineActionCreate,
            reason: request.Reason?.Trim() ?? string.Empty,
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

        await DispatchAssignmentNotificationAsync(actorUserId, assignment.WorkerId, null, assignment.SubStageId, assignment.Id, assignment.AssignmentType.ToString(), assignment.ProductionLineId, cancellationToken);

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

    public async Task<Result<StageDefaultAssignmentsUpdateResultDto>> UpdateStageDefaultAssignmentsAsync(
        Guid productionLineId,
        Guid subStageId,
        IReadOnlyCollection<Guid>? workerIds,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        var actorValidation = ValidateActor(actorUserId);
        if (actorValidation.IsFailure)
            return Result<StageDefaultAssignmentsUpdateResultDto>.Failure(actorValidation.Error!);

        var lineValidation = ValidateRequiredGuid(productionLineId, nameof(productionLineId));
        if (lineValidation.IsFailure)
            return Result<StageDefaultAssignmentsUpdateResultDto>.Failure(lineValidation.Error!);

        var stageValidation = ValidateRequiredGuid(subStageId, nameof(subStageId));
        if (stageValidation.IsFailure)
            return Result<StageDefaultAssignmentsUpdateResultDto>.Failure(stageValidation.Error!);

        if ((await ResolveActiveLineStageContextAsync(productionLineId, subStageId, cancellationToken)).IsFailure)
            return Result<StageDefaultAssignmentsUpdateResultDto>.Failure(new Error("ValidationError", "المرحلة لا تتبع قسم خط الإنتاج المحدد أو أنها غير نشطة."));

        var requestedWorkerIds = workerIds?.ToArray() ?? [];
        if (requestedWorkerIds.Any(workerId => workerId == Guid.Empty) || requestedWorkerIds.Distinct().Count() != requestedWorkerIds.Length)
            return Result<StageDefaultAssignmentsUpdateResultDto>.Failure(new Error("ValidationError", "WorkerIds must contain unique, valid workers."));

        if (requestedWorkerIds.Length > 0)
        {
            var activeWorkerIds = await _dbContext.Workers
                .AsNoTracking()
                .Where(worker => requestedWorkerIds.Contains(worker.Id) && worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active)
                .Select(worker => worker.Id)
                .ToArrayAsync(cancellationToken);
            if (activeWorkerIds.Length != requestedWorkerIds.Length)
                return Result<StageDefaultAssignmentsUpdateResultDto>.Failure(new Error("NotFound", "One or more workers were not found or are inactive."));
        }

        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var currentAssignments = await _dbContext.WorkerDefaultAssignments
                .Where(assignment => assignment.ProductionLineId == productionLineId && assignment.SubStageId == subStageId && assignment.IsActive)
                .OrderBy(assignment => assignment.WorkerId)
                .ToListAsync(cancellationToken);
            var requestedWorkerIdSet = requestedWorkerIds.ToHashSet();
            var currentWorkerIdSet = currentAssignments.Select(assignment => assignment.WorkerId).ToHashSet();
            var workerIdsToAdd = requestedWorkerIds.Where(workerId => !currentWorkerIdSet.Contains(workerId)).ToArray();
            var assignmentsToRemove = currentAssignments.Where(assignment => !requestedWorkerIdSet.Contains(assignment.WorkerId)).ToArray();
            var notificationRequests = new List<AssignmentNotificationDispatchRequest>();
            var now = DateTime.UtcNow;

            foreach (var workerId in workerIdsToAdd)
            {
                var assignment = new WorkerDefaultAssignment(
                    Guid.NewGuid(),
                    workerId,
                    subStageId,
                    actorUserId,
                    now,
                    reason: null,
                    isActive: true,
                    createdAtUtc: now,
                    productionLineId: productionLineId);
                _dbContext.WorkerDefaultAssignments.Add(assignment);
                _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
                    Guid.NewGuid(), workerId, null, subStageId, AssignmentType.Default.ToString(), TimelineActionCreate,
                    string.Empty, now, null, actorUserId, false, null, null, now));
                notificationRequests.Add(new AssignmentNotificationDispatchRequest(actorUserId, workerId, null, subStageId, assignment.Id, assignment.AssignmentType.ToString(), productionLineId));
                await _auditEngine.RecordAsync(actorUserId, AuditActionType.Create, nameof(WorkerDefaultAssignment), assignment.Id.ToString(), null, assignment, requestMeta, cancellationToken);
            }

            foreach (var assignment in assignmentsToRemove)
            {
                var before = new { assignment.Id, assignment.WorkerId, assignment.SubStageId, assignment.IsActive, assignment.AssignedAt };
                assignment.Deactivate(now);
                _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(
                    Guid.NewGuid(), assignment.WorkerId, subStageId, null, AssignmentType.Default.ToString(), TimelineActionCancel,
                    BulkStageSelectionRemovalReason, assignment.AssignedAt, now, actorUserId, false, null, null, now));
                notificationRequests.Add(new AssignmentNotificationDispatchRequest(actorUserId, assignment.WorkerId, subStageId, null, assignment.Id, assignment.AssignmentType.ToString(), productionLineId));
                await _auditEngine.RecordAsync(
                    actorUserId,
                    AuditActionType.Cancel,
                    nameof(WorkerDefaultAssignment),
                    assignment.Id.ToString(),
                    before,
                    new { assignment.Id, assignment.WorkerId, assignment.SubStageId, assignment.IsActive, assignment.UpdatedAtUtc },
                    requestMeta,
                    cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            foreach (var notificationRequest in notificationRequests)
                await DispatchAssignmentNotificationAsync(notificationRequest, cancellationToken);

            return Result<StageDefaultAssignmentsUpdateResultDto>.Success(new StageDefaultAssignmentsUpdateResultDto(
                subStageId,
                workerIdsToAdd.Length,
                assignmentsToRemove.Length,
                requestedWorkerIds.OrderBy(workerId => workerId).ToArray()));
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            return Result<StageDefaultAssignmentsUpdateResultDto>.Failure(new Error("Conflict", "Stage staffing changed while it was being saved. Refresh and try again."));
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            return Result<StageDefaultAssignmentsUpdateResultDto>.Failure(new Error("Conflict", "Stage staffing changed while it was being saved. Refresh and try again."));
        }
    }

    public async Task<Result<AssignmentActionResultDto>> RemoveDefaultAssignmentAsync(
        Guid workerId,
        Guid productionLineId,
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
        var lineValidation = ValidateRequiredGuid(productionLineId, nameof(productionLineId));
        if (lineValidation.IsFailure) return Result<AssignmentActionResultDto>.Failure(lineValidation.Error!);
        var stageValidation = ValidateRequiredGuid(subStageId, nameof(subStageId));
        if (stageValidation.IsFailure) return Result<AssignmentActionResultDto>.Failure(stageValidation.Error!);
        var reasonValidation = ValidateReason(reason, nameof(reason));
        if (reasonValidation.IsFailure) return Result<AssignmentActionResultDto>.Failure(reasonValidation.Error!);

        var assignment = await _dbContext.WorkerDefaultAssignments
            .SingleOrDefaultAsync(x => x.WorkerId == workerId && x.ProductionLineId == productionLineId && x.SubStageId == subStageId && x.IsActive, cancellationToken);
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

        await DispatchAssignmentNotificationAsync(actorUserId, assignment.WorkerId, assignment.SubStageId, null, assignment.Id, assignment.AssignmentType.ToString(), assignment.ProductionLineId, cancellationToken);

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
        if (NonPermanentAssignmentsAreDisabled)
            return Result<AssignmentActionResultDto>.Failure(NonPermanentAssignmentsDisabledError);

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

        if (request.ParticipationMode == TemporaryAssignmentMode.TemporaryMove)
        {
            var fromSubStageValidationResult = ValidateRequiredGuid(request.FromSubStageId ?? Guid.Empty, nameof(request.FromSubStageId));
            if (fromSubStageValidationResult.IsFailure)
                return Result<AssignmentActionResultDto>.Failure(fromSubStageValidationResult.Error!);
        }

        var toSubStageValidationResult = ValidateRequiredGuid(request.ToSubStageId, nameof(request.ToSubStageId));
        if (toSubStageValidationResult.IsFailure)
        {
            return Result<AssignmentActionResultDto>.Failure(toSubStageValidationResult.Error!);
        }

        if (request.ParticipationMode == TemporaryAssignmentMode.TemporaryMove && request.FromSubStageId == request.ToSubStageId)
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

        if (request.FromSubStageId.HasValue && (await ResolveActiveSubStageAsync(request.FromSubStageId.Value, cancellationToken)).IsFailure)
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
            var effectiveAssignments = await ResolveEffectiveAssignmentsAsync([request.WorkerId], request.StartAtUtc, cancellationToken);
            if (effectiveAssignments.IsFailure)
                return Result<AssignmentActionResultDto>.Failure(effectiveAssignments.Error!);

            if (request.ParticipationMode == TemporaryAssignmentMode.TemporaryMove &&
                !effectiveAssignments.Value![request.WorkerId].Any(assignment => assignment.AssignmentType == AssignmentType.Default && assignment.EffectiveSubStageId == request.FromSubStageId))
            {
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "العامل لم يكن معينًا فعليًا بالمرحلة المصدر عند وقت النقل."));
            }

            var overlapValidationResult = await ValidateTemporaryOverlapAsync(request.WorkerId, request.ToSubStageId, request.StartAtUtc, request.EndAtUtc, null, cancellationToken);
            if (overlapValidationResult.IsFailure)
            {
                return Result<AssignmentActionResultDto>.Failure(overlapValidationResult.Error!);
            }

            if (effectiveAssignments.Value![request.WorkerId].Any(assignment => assignment.EffectiveSubStageId == request.ToSubStageId))
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "العامل مشارك بالفعل في المرحلة المستهدفة؛ لا يمكن إنشاء مشاركة مؤقتة مكررة."));

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
                participationMode: request.ParticipationMode,
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

            await DispatchAssignmentNotificationAsync(actorUserId, entity.WorkerId, entity.FromSubStageId, entity.ToSubStageId, entity.Id, entity.AssignmentType.ToString(), cancellationToken);

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
        if (NonPermanentAssignmentsAreDisabled)
            return Result<AssignmentActionResultDto>.Failure(NonPermanentAssignmentsDisabledError);

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
            .AnyAsync(x => x.WorkerId == request.ReplacedWorkerId && x.SubStageId == request.SubStageId && x.IsActive, cancellationToken);

        if (!replacedWorkerDefault)
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
                .AnyAsync(x => x.WorkerId == request.ReplacedWorkerId && x.SubStageId == request.SubStageId && x.IsActive, cancellationToken);
            if (!activeReplacedAssignment)
            {
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "تم تغيير تعيين العامل المستبدَل قبل حفظ البديل."));
            }

            var sourceAssignments = await ResolveEffectiveAssignmentsAsync([request.ReplacementWorkerId], request.StartAtUtc, cancellationToken);
            if (sourceAssignments.IsFailure)
                return Result<AssignmentActionResultDto>.Failure(sourceAssignments.Error!);

            var sourceAssignment = sourceAssignments.Value![request.ReplacementWorkerId]
                .Where(assignment => assignment.EffectiveSubStageId != request.SubStageId)
                .Where(assignment => !request.FromSubStageId.HasValue || assignment.EffectiveSubStageId == request.FromSubStageId)
                .OrderByDescending(assignment => assignment.AssignmentType == AssignmentType.Default)
                .ThenBy(assignment => assignment.EffectiveSubStageId)
                .FirstOrDefault();
            if (sourceAssignment?.EffectiveSubStageId is not Guid fromSubStageId)
            {
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "يجب أن يكون العامل البديل معينًا فعليًا بمرحلة مصدر عند وقت النقل."));
            }

            var overlapValidationResult = await ValidateTemporaryOverlapAsync(request.ReplacementWorkerId, request.SubStageId, request.StartAtUtc, request.EndAtUtc, null, cancellationToken);
            if (overlapValidationResult.IsFailure)
            {
                return Result<AssignmentActionResultDto>.Failure(overlapValidationResult.Error!);
            }

            if (sourceAssignments.Value![request.ReplacementWorkerId].Any(assignment => assignment.EffectiveSubStageId == request.SubStageId))
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "العامل البديل مشارك بالفعل في هذه المرحلة."));

            var status = request.StartAtUtc <= now
                ? TempStatusActive
                : TempStatusScheduled;

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
            participationMode: TemporaryAssignmentMode.TemporaryMove,
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

        await DispatchAssignmentNotificationAsync(actorUserId, entity.WorkerId, entity.FromSubStageId, entity.ToSubStageId, entity.Id, entity.AssignmentType.ToString(), cancellationToken);

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
        if (NonPermanentAssignmentsAreDisabled)
            return Result<CancelTemporaryAssignmentResultDto>.Failure(NonPermanentAssignmentsDisabledError);

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
        var finalizeResult = await FinalizeCompletedTemporaryAssignmentsCoreAsync(now, cancellationToken);
        if (finalizeResult.IsFailure)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(finalizeResult.Error!);
        }

        var assignment = await _dbContext.WorkerTemporaryAssignments
            .FirstOrDefaultAsync(
                x => x.Id == assignmentId &&
                     (x.Status == TempStatusScheduled || x.Status == TempStatusActive),
                cancellationToken);

        if (assignment is null)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(new Error("NotFound", "لم يتم العثور على تعيين مؤقت نشط لإلغائه."));
        }

        var before = new
        {
            assignment.Id,
            assignment.WorkerId,
            assignment.FromSubStageId,
            assignment.ToSubStageId,
            assignment.StartAtUtc,
            assignment.EndAtUtc,
            assignment.Status,
            assignment.UpdatedAtUtc
        };
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
            before,
            after: new { assignment.Id, Status = TempStatusCancelled, assignment.StartAtUtc, EndAtUtc = now, UpdatedAtUtc = now, CancelReason = reason.Trim(), CancelledAtUtc = now },
            requestMeta: requestMeta);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CancelTemporaryAssignmentResultDto>.Failure(new Error("Conflict", "تم تعديل التعيين المؤقت بواسطة مستخدم آخر. حدّث البيانات وحاول مرة أخرى."));
        }

        await DispatchAssignmentNotificationAsync(actorUserId, assignment.WorkerId, assignment.FromSubStageId, assignment.ToSubStageId, assignment.Id, assignment.AssignmentType.ToString(), cancellationToken);

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
        if (NonPermanentAssignmentsAreDisabled)
            return Result<AssignmentActionResultDto>.Failure(NonPermanentAssignmentsDisabledError);

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
            var defaultAssignment = await _dbContext.WorkerDefaultAssignments.SingleOrDefaultAsync(
                x => x.Id == request.SourceAssignmentId && x.WorkerId == request.WorkerId && x.SubStageId == request.FromSubStageId && x.IsActive,
                cancellationToken);

            if (defaultAssignment is not null)
            {
                if (!request.TemporaryEndAtUtc.HasValue && request.EffectiveAtUtc > now.AddSeconds(30))
                {
                    return Result<AssignmentActionResultDto>.Failure(new Error("ValidationError", "النقل الحالي الدائم يسري الآن فقط. استخدم النقل المؤقت لوقت مستقبلي."));
                }
                if (request.TemporaryEndAtUtc.HasValue)
                {
                    var overlap = await ValidateTemporaryOverlapAsync(request.WorkerId, request.ToSubStageId, request.EffectiveAtUtc, request.TemporaryEndAtUtc.Value, null, cancellationToken);
                    if (overlap.IsFailure) return Result<AssignmentActionResultDto>.Failure(overlap.Error!);
                    if (await HasEffectiveParticipationAsync(request.WorkerId, request.ToSubStageId, request.EffectiveAtUtc, cancellationToken))
                        return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "العامل مشارك بالفعل في المرحلة المستهدفة."));
                    var temporary = CreateTemporaryEntity(request.WorkerId, request.FromSubStageId, request.ToSubStageId, request.EffectiveAtUtc, request.TemporaryEndAtUtc.Value, actorUserId, request.Reason, now);
                    _dbContext.WorkerTemporaryAssignments.Add(temporary);
                    AddAssignmentTimeline(temporary, TimelineActionCreate, request.Reason, actorUserId, now);
                    await _auditEngine.RecordAsync(actorUserId, AuditActionType.Create, nameof(WorkerTemporaryAssignment), temporary.Id.ToString(), null, temporary, requestMeta, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                    await DispatchAssignmentNotificationAsync(actorUserId, temporary.WorkerId, temporary.FromSubStageId, temporary.ToSubStageId, temporary.Id, temporary.AssignmentType.ToString(), cancellationToken);
                    return ToAssignmentResult(temporary);
                }

                var before = new { defaultAssignment.Id, defaultAssignment.WorkerId, defaultAssignment.SubStageId, defaultAssignment.IsActive, defaultAssignment.AssignedAt, defaultAssignment.UpdatedAtUtc };
                if (await HasEffectiveParticipationAsync(request.WorkerId, request.ToSubStageId, request.EffectiveAtUtc, cancellationToken))
                    return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "العامل مشارك بالفعل في المرحلة المستهدفة."));
                defaultAssignment.Deactivate(now);
                var destination = new WorkerDefaultAssignment(Guid.NewGuid(), request.WorkerId, request.ToSubStageId, actorUserId, now, request.Reason, true, now, defaultAssignment.ProductionLineId);
                _dbContext.WorkerDefaultAssignments.Add(destination);
                _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(Guid.NewGuid(), request.WorkerId, request.FromSubStageId, request.ToSubStageId, AssignmentType.Default.ToString(), TimelineActionUpdate, request.Reason.Trim(), now, null, actorUserId, false, null, null, now));
                await _auditEngine.RecordAsync(actorUserId, AuditActionType.Update, nameof(WorkerDefaultAssignment), defaultAssignment.Id.ToString(), before, destination, requestMeta, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                await DispatchAssignmentNotificationAsync(actorUserId, destination.WorkerId, request.FromSubStageId, destination.SubStageId, destination.Id, destination.AssignmentType.ToString(), cancellationToken);
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
            var overlapValidation = await ValidateTemporaryOverlapAsync(request.WorkerId, request.ToSubStageId, request.EffectiveAtUtc, destinationEnd, sourceTemporary.Id, cancellationToken);
            if (overlapValidation.IsFailure) return Result<AssignmentActionResultDto>.Failure(overlapValidation.Error!);
            if (await HasEffectiveParticipationAsync(request.WorkerId, request.ToSubStageId, request.EffectiveAtUtc, cancellationToken))
                return Result<AssignmentActionResultDto>.Failure(new Error("Conflict", "العامل مشارك بالفعل في المرحلة المستهدفة."));

            var sourceBefore = new
            {
                sourceTemporary.Id,
                sourceTemporary.WorkerId,
                sourceTemporary.FromSubStageId,
                sourceTemporary.ToSubStageId,
                sourceTemporary.StartAtUtc,
                sourceTemporary.EndAtUtc,
                sourceTemporary.Status,
                sourceTemporary.UpdatedAtUtc
            };
            _dbContext.Entry(sourceTemporary).Property(nameof(WorkerTemporaryAssignment.Status)).CurrentValue = TempStatusCancelled;
            _dbContext.Entry(sourceTemporary).Property(nameof(WorkerTemporaryAssignment.EndAtUtc)).CurrentValue = request.EffectiveAtUtc;
            _dbContext.Entry(sourceTemporary).Property(nameof(WorkerTemporaryAssignment.UpdatedAtUtc)).CurrentValue = now;
            _dbContext.AssignmentTimelineEntries.Add(new AssignmentTimelineEntry(Guid.NewGuid(), request.WorkerId, sourceTemporary.FromSubStageId, sourceTemporary.ToSubStageId, sourceTemporary.AssignmentType.ToString(), TimelineActionCancel, request.Reason.Trim(), sourceTemporary.StartAtUtc, request.EffectiveAtUtc, actorUserId, false, sourceTemporary.Id, sourceTemporary.ReplacementForWorkerId, now));

            var replacement = CreateTemporaryEntity(request.WorkerId, request.FromSubStageId, request.ToSubStageId, request.EffectiveAtUtc, destinationEnd, actorUserId, request.Reason, now);
            _dbContext.WorkerTemporaryAssignments.Add(replacement);
            AddAssignmentTimeline(replacement, TimelineActionCreate, request.Reason, actorUserId, now);
            await _auditEngine.RecordAsync(actorUserId, AuditActionType.Update, nameof(WorkerTemporaryAssignment), sourceTemporary.Id.ToString(), sourceBefore, replacement, requestMeta, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            await DispatchAssignmentNotificationAsync(actorUserId, replacement.WorkerId, replacement.FromSubStageId, replacement.ToSubStageId, replacement.Id, replacement.AssignmentType.ToString(), cancellationToken);
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

        var assignments = await ResolveEffectiveAssignmentsAsync(workers.Select(x => x.Id), asOf, cancellationToken);
        if (assignments.IsFailure)
        {
            return Result<SubStageCurrentWorkersDto>.Failure(assignments.Error!);
        }

        var items = workers
            .SelectMany(worker => assignments.Value![worker.Id]
                .Where(assignment => assignment.EffectiveSubStageId == subStageId)
                .Select(assignment => new { Worker = worker, Assignment = assignment }))
            .Select(item =>
            {
                var assignment = item.Assignment;
                return new SubStageCurrentWorkerDto
                {
                    WorkerId = item.Worker.Id,
                    FullName = item.Worker.FullName,
                    EmployeeCode = item.Worker.EmployeeCode,
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

    public async Task<Result<IReadOnlyCollection<SubStageAssignmentCoverageDto>>> GetActiveSubStageAssignmentCoverageAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var stages = await _dbContext.SubStages
            .AsNoTracking()
            .Where(stage => stage.IsActive)
            .OrderBy(stage => stage.MainStageId)
            .ThenBy(stage => stage.DefaultOrder)
            .Select(stage => new
            {
                stage.Id,
                stage.Capacity,
                stage.MainStageId,
                stage.DepartmentId,
                FactoryId = stage.MainStage!.Department!.FactoryId
            })
            .ToArrayAsync(cancellationToken);

        if (stages.Length == 0)
        {
            return Result<IReadOnlyCollection<SubStageAssignmentCoverageDto>>.Success([]);
        }

        var activeLines = await _dbContext.ProductionLines
            .AsNoTracking()
            .Where(line => line.IsActive && line.DepartmentId.HasValue)
            .Select(line => new { line.Id, DepartmentId = line.DepartmentId!.Value })
            .ToArrayAsync(cancellationToken);

        var workerIds = await _dbContext.Workers
            .AsNoTracking()
            .Where(worker => worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active)
            .Select(worker => worker.Id)
            .ToArrayAsync(cancellationToken);

        var assignmentsResult = await ResolveEffectiveAssignmentsAsync(
            workerIds,
            asOfUtc ?? DateTime.UtcNow,
            cancellationToken);
        if (assignmentsResult.IsFailure)
        {
            return Result<IReadOnlyCollection<SubStageAssignmentCoverageDto>>.Failure(assignmentsResult.Error!);
        }

        var effectiveParticipations = assignmentsResult.Value!
            .SelectMany(pair => pair.Value
                .Where(assignment => assignment.EffectiveSubStageId.HasValue)
                .Select(assignment => new { SubStageId = assignment.EffectiveSubStageId!.Value, WorkerId = pair.Key, assignment.ProductionLineId }))
            .Distinct()
            .ToArray();
        var assignedWorkersByStage = effectiveParticipations
            .GroupBy(item => item.SubStageId)
            .ToDictionary(group => group.Key, group => group.Count());
        var distinctWorkersByMainStage = effectiveParticipations
            .Join(stages, item => item.SubStageId, stage => stage.Id, (item, stage) => new { ScopeId = stage.MainStageId, item.WorkerId })
            .Distinct()
            .GroupBy(item => item.ScopeId)
            .ToDictionary(group => group.Key, group => group.Count());
        var assignedWorkersByStageAndLine = effectiveParticipations
            .Where(item => item.ProductionLineId.HasValue)
            .GroupBy(item => (item.SubStageId, ProductionLineId: item.ProductionLineId!.Value))
            .ToDictionary(group => group.Key, group => group.Select(item => item.WorkerId).Distinct().Count());
        var distinctWorkersByMainStageAndLine = effectiveParticipations
            .Where(item => item.ProductionLineId.HasValue)
            .Join(stages, item => item.SubStageId, stage => stage.Id,
                (item, stage) => new { stage.MainStageId, ProductionLineId = item.ProductionLineId!.Value, item.WorkerId })
            .Distinct()
            .GroupBy(item => (item.MainStageId, item.ProductionLineId))
            .ToDictionary(group => group.Key, group => group.Count());
        var distinctWorkersByProductionLine = effectiveParticipations
            .Where(item => item.ProductionLineId.HasValue)
            .Select(item => new { ScopeId = item.ProductionLineId!.Value, item.WorkerId })
            .Distinct()
            .GroupBy(item => item.ScopeId)
            .ToDictionary(group => group.Key, group => group.Count());
        var distinctWorkersByDepartment = effectiveParticipations
            .Join(stages, item => item.SubStageId, stage => stage.Id, (item, stage) => new { ScopeId = stage.DepartmentId, item.WorkerId })
            .Distinct()
            .GroupBy(item => item.ScopeId)
            .ToDictionary(group => group.Key, group => group.Count());
        var distinctWorkersByFactory = effectiveParticipations
            .Where(item => item.ProductionLineId.HasValue)
            .Join(_dbContext.ProductionLines.AsNoTracking(), item => item.ProductionLineId!.Value, line => line.Id, (item, line) => new { ScopeId = line.FactoryId, item.WorkerId })
            .Distinct()
            .GroupBy(item => item.ScopeId)
            .ToDictionary(group => group.Key, group => group.Count());

        IReadOnlyCollection<SubStageAssignmentCoverageDto> summaries = stages
            .Select(stage =>
            {
                var assignedWorkersCount = assignedWorkersByStage.GetValueOrDefault(stage.Id);
                var hasAuthoritativeRequiredWorkerCount = stage.Capacity > 0;
                int? requiredWorkersCount = hasAuthoritativeRequiredWorkerCount ? stage.Capacity : null;
                int? assignmentCoveragePercent = requiredWorkersCount.HasValue
                    ? Math.Min(100, (int)Math.Round((decimal)assignedWorkersCount * 100m / requiredWorkersCount.Value, MidpointRounding.AwayFromZero))
                    : null;
                var staffingStatus = !hasAuthoritativeRequiredWorkerCount
                    ? "RequirementNotDefined"
                    : assignedWorkersCount == 0
                        ? "Unstaffed"
                        : assignedWorkersCount < requiredWorkersCount
                            ? "Understaffed"
                            : "Staffed";

                return new SubStageAssignmentCoverageDto(
                    stage.Id,
                    assignedWorkersCount,
                    requiredWorkersCount,
                    hasAuthoritativeRequiredWorkerCount,
                    assignmentCoveragePercent,
                    staffingStatus)
                {
                    MainStageDistinctWorkersCount = distinctWorkersByMainStage.GetValueOrDefault(stage.MainStageId),
                    DepartmentDistinctWorkersCount = distinctWorkersByDepartment.GetValueOrDefault(stage.DepartmentId),
                    FactoryDistinctWorkersCount = distinctWorkersByFactory.GetValueOrDefault(stage.FactoryId),
                    ProductionLines = activeLines
                        .Where(line => line.DepartmentId == stage.DepartmentId)
                        .Select(line => new ProductionLineStaffingCoverageDto(
                            line.Id,
                            assignedWorkersByStageAndLine.GetValueOrDefault((stage.Id, line.Id)),
                            distinctWorkersByMainStageAndLine.GetValueOrDefault((stage.MainStageId, line.Id)),
                            distinctWorkersByProductionLine.GetValueOrDefault(line.Id)))
                        .ToArray()
                };
            })
            .ToArray();

        return Result<IReadOnlyCollection<SubStageAssignmentCoverageDto>>.Success(summaries);
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
        Guid toSubStageId,
        DateTime startAtUtc,
        DateTime endAtUtc,
        Guid? excludedAssignmentId,
        CancellationToken cancellationToken)
    {
        var hasConflict = await _dbContext.WorkerTemporaryAssignments.AnyAsync(x =>
            x.WorkerId == workerId &&
            x.ToSubStageId == toSubStageId &&
            (!excludedAssignmentId.HasValue || x.Id != excludedAssignmentId.Value) &&
            (x.Status == TempStatusScheduled || x.Status == TempStatusActive) &&
            x.StartAtUtc < endAtUtc &&
            x.EndAtUtc > startAtUtc,
            cancellationToken);

        if (hasConflict)
        {
            return Result.Failure(new Error("Conflict", "للعامل مشاركة مؤقتة متداخلة في المرحلة نفسها."));
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
        => new(Guid.NewGuid(), workerId, fromSubStageId, toSubStageId, startAtUtc, endAtUtc, actorUserId, reason, null, TemporaryAssignmentMode.TemporaryMove, startAtUtc <= now ? TempStatusActive : TempStatusScheduled, now);

    private async Task<bool> HasEffectiveParticipationAsync(Guid workerId, Guid subStageId, DateTime atUtc, CancellationToken cancellationToken)
    {
        var effective = await ResolveEffectiveAssignmentsAsync([workerId], atUtc, cancellationToken);
        return effective.IsSuccess && effective.Value![workerId].Any(assignment => assignment.EffectiveSubStageId == subStageId);
    }

    private static WorkerAssignmentState EmptyAssignment(Guid workerId) => new(
        AssignmentId: null,
        WorkerId: workerId,
        AssignmentType: null,
        StartsAtUtc: null,
        EndsAtUtc: null,
        EffectiveSubStageId: null,
        FromSubStageId: null,
        ToSubStageId: null,
        ReplacementForWorkerId: null);

    private Task DispatchAssignmentNotificationAsync(
        Guid actorUserId,
        Guid workerId,
        Guid? fromSubStageId,
        Guid? toSubStageId,
        Guid assignmentId,
        string assignmentType,
        CancellationToken cancellationToken) =>
        DispatchAssignmentNotificationAsync(actorUserId, workerId, fromSubStageId, toSubStageId, assignmentId, assignmentType, null, cancellationToken);

    private Task DispatchAssignmentNotificationAsync(
        Guid actorUserId,
        Guid workerId,
        Guid? fromSubStageId,
        Guid? toSubStageId,
        Guid assignmentId,
        string assignmentType,
        Guid? productionLineId,
        CancellationToken cancellationToken) =>
        DispatchAssignmentNotificationAsync(
            new AssignmentNotificationDispatchRequest(actorUserId, workerId, fromSubStageId, toSubStageId, assignmentId, assignmentType, productionLineId),
            cancellationToken);

    private Task DispatchAssignmentNotificationAsync(
        AssignmentNotificationDispatchRequest request,
        CancellationToken cancellationToken) =>
        _assignmentNotificationDispatcher?.DispatchAsync(request, cancellationToken) ?? Task.CompletedTask;

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

    private async Task<Result> ResolveActiveLineStageContextAsync(Guid productionLineId, Guid subStageId, CancellationToken cancellationToken)
    {
        var valid = await _dbContext.ProductionLines.AsNoTracking()
            .Where(line => line.Id == productionLineId && line.IsActive && line.DepartmentId != null)
            .AnyAsync(line => _dbContext.SubStages.Any(stage =>
                stage.Id == subStageId && stage.IsActive && stage.DepartmentId == line.DepartmentId), cancellationToken);
        return valid
            ? Result.Success()
            : Result.Failure(new Error("ValidationError", "المرحلة لا تتبع قسم خط الإنتاج المحدد أو أنها غير نشطة."));
    }

    public async Task<Result<int>> FinalizeCompletedTemporaryAssignmentsAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var result = await FinalizeCompletedTemporaryAssignmentsCoreAsync(asOfUtc ?? DateTime.UtcNow, cancellationToken);
        return result.IsFailure
            ? Result<int>.Failure(result.Error!)
            : Result<int>.Success(result.Value!);
    }

    private async Task<Result<int>> FinalizeCompletedTemporaryAssignmentsCoreAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var endedAssignments = await _dbContext.WorkerTemporaryAssignments
            .Where(x => (x.Status == TempStatusScheduled || x.Status == TempStatusActive) && x.EndAtUtc <= asOfUtc)
            .ToListAsync(cancellationToken);

        if (endedAssignments.Count == 0)
        {
            return Result<int>.Success(0);
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
            return Result<int>.Failure(new Error("Conflict", "تمت معالجة انتهاء التعيين المؤقت بواسطة مستخدم آخر."));
        }
        foreach (var assignment in endedAssignments)
        {
            await DispatchAssignmentNotificationAsync(
                assignment.AssignedByUserId,
                assignment.WorkerId,
                assignment.FromSubStageId,
                assignment.ToSubStageId,
                assignment.Id,
                assignment.AssignmentType.ToString(),
                cancellationToken);
        }
        return Result<int>.Success(endedAssignments.Count);
    }
}
