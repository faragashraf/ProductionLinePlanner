using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Synchronizes ZKTime worker identities before attendance is matched. Only external attendance
/// identifiers may change on an existing planner worker; planner-owned fields are protected.
/// </summary>
public sealed class WorkerInitialSyncService(
    AppDbContext dbContext,
    IWorkerIdentitySource workerIdentitySource,
    IWorkerSyncPolicy workerSyncPolicy,
    IAuthoritativeWorkerSnapshotValidator snapshotValidator,
    IAuditEngine auditEngine,
    ILogger<WorkerInitialSyncService> logger) : IWorkerInitialSyncService
{
    public async Task<Result<WorkerActiveServiceSyncPreviewDto>> PreviewActiveServiceSyncAsync(
        CancellationToken cancellationToken = default)
    {
        var sourceResult = await ReadSourceSnapshotAsync(claim: false, cancellationToken);
        if (sourceResult.IsFailure)
        {
            return Result<WorkerActiveServiceSyncPreviewDto>.Failure(sourceResult.Error!);
        }

        var localWorkers = await dbContext.Workers
            .AsNoTracking()
            .OrderBy(worker => worker.EmployeeCode)
            .ThenBy(worker => worker.Id)
            .ToArrayAsync(cancellationToken);
        var plan = BuildPlan(sourceResult.Value!, localWorkers);

        return Result<WorkerActiveServiceSyncPreviewDto>.Success(MapPreview(plan, localWorkers));
    }

    public async Task<Result<WorkerInitialSyncResultDto>> SyncWorkersAsync(
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<WorkerInitialSyncResultDto>.Failure(new Error("Unauthorized", "User context is required."));
        }

        return await SynchronizeCoreAsync(actorUserId, requestMeta, cancellationToken);
    }

    public Task<Result<WorkerInitialSyncResultDto>> SyncWorkersForAttendanceAsync(
        CancellationToken cancellationToken = default) =>
        SynchronizeCoreAsync(actorUserId: null, requestMeta: null, cancellationToken);

    private async Task<Result<WorkerInitialSyncResultDto>> SynchronizeCoreAsync(
        Guid? actorUserId,
        string? requestMeta,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        var sourceResult = await ReadSourceSnapshotAsync(claim: true, cancellationToken);
        if (sourceResult.IsFailure)
        {
            return Result<WorkerInitialSyncResultDto>.Failure(sourceResult.Error!);
        }

        var localWorkers = await dbContext.Workers
            .OrderBy(worker => worker.EmployeeCode)
            .ThenBy(worker => worker.Id)
            .ToArrayAsync(cancellationToken);
        var plan = BuildPlan(sourceResult.Value!, localWorkers);
        var createdCount = 0;
        var updatedCount = 0;

        var failedSourceIds = new HashSet<long>();
        foreach (var candidate in plan.Rows.Where(row => row.IsClaimed && row.Preview.Action == WorkerSyncActions.NewWorkerCandidate))
        {
            var workerResult = workerSyncPolicy.CreateNewWorker(candidate.Source!, DateTime.UtcNow);
            if (workerResult.IsFailure)
            {
                if (candidate.SourceRecordId.HasValue)
                {
                    failedSourceIds.Add(candidate.SourceRecordId.Value);
                }
                continue;
            }

            dbContext.Workers.Add(workerResult.Value!);
            createdCount++;
        }

        foreach (var existing in plan.Rows.Where(row => row.IsClaimed && row.Preview.Action == WorkerSyncActions.ExistingWorkerUpdated))
        {
            if (existing.Worker is not null &&
                workerSyncPolicy.SynchronizeExistingWorker(existing.Worker, existing.Source!, DateTime.UtcNow))
            {
                updatedCount++;
            }
        }

        var completedAtUtc = DateTime.UtcNow;
        var identityConflictCount = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.IdentityConflict);
        var unsupportedCount = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.UnsupportedSourceState);
        var missingFromSourceCount = plan.Rows.Count(row => row.Source is null && row.Worker is not null);
        var unchangedCount = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.ExistingWorkerUnchanged);
        var result = new WorkerInitialSyncResultDto
        {
            SourceCount = sourceResult.Value!.Snapshot.Rows.Count,
            CreatedCount = createdCount,
            UpdatedCount = updatedCount,
            UnchangedCount = unchangedCount,
            MissingFromSourceCount = missingFromSourceCount,
            MarkedInactiveCount = 0,
            ReactivatedCount = 0,
            WarningCount = identityConflictCount + unsupportedCount,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (actorUserId.HasValue)
            {
                await auditEngine.RecordAsync(
                    actorUserId.Value,
                    AuditActionType.WorkerInitialSync,
                    nameof(Worker),
                    nameof(Worker),
                    before: null,
                    after: result,
                    requestMeta: requestMeta,
                    cancellationToken: cancellationToken);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError(exception, "Worker identity synchronization failed before it could be committed.");
            await FailClaimedWorkerRowsAsync(sourceResult.Value!.Batch, "WorkerPersistenceFailed");
            return Result<WorkerInitialSyncResultDto>.Failure(new Error(
                "WorkerInitialSyncFailed",
                "Unable to persist worker import results."));
        }

        var acknowledgement = await workerIdentitySource.CompleteBatchAsync(
            sourceResult.Value!.Batch,
            CreateWorkerOutcomes(plan, failedSourceIds),
            cancellationToken);
        if (acknowledgement.IsFailure)
        {
            return Result<WorkerInitialSyncResultDto>.Failure(acknowledgement.Error!);
        }

        return Result<WorkerInitialSyncResultDto>.Success(result);
    }

    private async Task<Result<WorkerSourceRead>> ReadSourceSnapshotAsync(
        bool claim,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourceResult = claim
                ? await workerIdentitySource.ClaimBatchAsync(cancellationToken)
                : await workerIdentitySource.ReadSnapshotAsync(cancellationToken);
            if (sourceResult.IsFailure)
            {
                return Result<WorkerSourceRead>.Failure(sourceResult.Error!);
            }

            var orderedItems = sourceResult.Value!.Items
                .OrderBy(item => WorkerSyncPolicy.Normalize(item.Worker.AttendanceUserId), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => WorkerSyncPolicy.Normalize(item.Worker.BadgeNumber), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => WorkerSyncPolicy.Normalize(item.Worker.EmployeeCode), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => WorkerSyncPolicy.Normalize(item.Worker.Name), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var snapshot = new WorkerSourceSnapshot(
                orderedItems.Select(item => item.Worker).ToArray(),
                IsComplete: false,
                AbsenceIsAuthoritative: false,
                EmploymentStatusIsAuthoritative: false,
                DepartmentIsAuthoritative: false,
                ShiftIsAuthoritative: false);
            return Result<WorkerSourceRead>.Success(new WorkerSourceRead(snapshot, orderedItems, sourceResult.Value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Result<WorkerSourceRead>.Failure(new Error(
                "AttendanceSourceError",
                "Unable to read worker data from attendance source."));
        }
    }

    private WorkerSyncPlan BuildPlan(WorkerSourceRead sourceRead, IReadOnlyCollection<Worker> localWorkers)
    {
        var snapshot = sourceRead.Snapshot;
        var validation = snapshotValidator.Inspect(snapshot);
        var attendanceUsers = BuildLookup(localWorkers, worker => worker.AttendanceUserId);
        var badges = BuildLookup(localWorkers, worker => worker.BadgeNumber);
        var employeeCodes = BuildLookup(localWorkers, worker => worker.EmployeeCode);
        var matchedWorkerIds = new HashSet<Guid>();
        var rows = new List<WorkerSyncPlanRow>();

        var sourceIndex = 0;
        foreach (var sourceItem in sourceRead.Items)
        {
            var source = sourceItem.Worker;
            var sourceAttendanceUserId = WorkerSyncPolicy.Normalize(source.AttendanceUserId);
            var sourceBadge = WorkerSyncPolicy.Normalize(source.BadgeNumber);
            var sourceEmployeeCode = WorkerSyncPolicy.Normalize(source.EmployeeCode);
            var sourceConflicts = new List<string>();

            if (validation.InvalidSourceRowIndexes.Contains(sourceIndex))
            {
                rows.Add(new WorkerSyncPlanRow(
                    source,
                    null,
                    sourceItem.SourceRecordId,
                    sourceItem.IsClaimed,
                    CreatePreviewRow(
                        WorkerSyncActions.UnsupportedSourceState,
                        null,
                        source,
                        [],
                        ["InvalidSourceIdentity"],
                        "Null or invalid attendance, badge, or employee-code identity.")));
                sourceIndex++;
                continue;
            }

            if (sourceAttendanceUserId is not null && validation.DuplicateAttendanceUserIds.Contains(sourceAttendanceUserId))
                sourceConflicts.Add("DuplicateSourceAttendanceUserId");
            if (sourceBadge is not null && validation.DuplicateBadgeNumbers.Contains(sourceBadge))
                sourceConflicts.Add("DuplicateSourceBadgeNumber");
            if (sourceEmployeeCode is not null && validation.DuplicateEmployeeCodes.Contains(sourceEmployeeCode))
                sourceConflicts.Add("DuplicateSourceEmployeeCode");

            if (sourceConflicts.Count > 0)
            {
                rows.Add(new WorkerSyncPlanRow(
                    source,
                    null,
                    sourceItem.SourceRecordId,
                    sourceItem.IsClaimed,
                    CreatePreviewRow(
                        WorkerSyncActions.IdentityConflict,
                        null,
                        source,
                        workerSyncPolicy.ProtectedLocalFields,
                        sourceConflicts,
                        "Duplicate identities make this source row unsafe to match.")));
                sourceIndex++;
                continue;
            }

            var match = ResolveWorkerByIdentityPriority(
                attendanceUsers,
                badges,
                employeeCodes,
                sourceAttendanceUserId,
                sourceBadge,
                sourceEmployeeCode);

            if (match.IsConflict)
            {
                rows.Add(new WorkerSyncPlanRow(
                    source,
                    null,
                    sourceItem.SourceRecordId,
                    sourceItem.IsClaimed,
                    CreatePreviewRow(
                        WorkerSyncActions.IdentityConflict,
                        null,
                        source,
                        workerSyncPolicy.ProtectedLocalFields,
                        [match.ConflictReason!],
                        "The source identities resolve to more than one local worker.")));
                sourceIndex++;
                continue;
            }

            if (match.Worker is not null)
            {
                var worker = match.Worker;
                matchedWorkerIds.Add(worker.Id);
                var decision = workerSyncPolicy.EvaluateExistingWorker(worker, source);
                rows.Add(new WorkerSyncPlanRow(
                    source,
                    worker,
                    sourceItem.SourceRecordId,
                    sourceItem.IsClaimed,
                    CreatePreviewRow(
                        decision.Action,
                        worker,
                        source,
                        decision.ProtectedLocalFields,
                        decision.IdentityConflicts,
                        decision.Action == WorkerSyncActions.ExistingWorkerUpdated
                            ? "External attendance identifiers will be reconciled; planner-owned data remains unchanged."
                            : "Existing planner-owned worker data remains unchanged.")));
                sourceIndex++;
                continue;
            }

            var createResult = workerSyncPolicy.CreateNewWorker(source, DateTime.UnixEpoch);
            rows.Add(new WorkerSyncPlanRow(
                source,
                null,
                sourceItem.SourceRecordId,
                sourceItem.IsClaimed,
                CreatePreviewRow(
                    createResult.IsSuccess ? WorkerSyncActions.NewWorkerCandidate : WorkerSyncActions.UnsupportedSourceState,
                    null,
                    source,
                    workerSyncPolicy.ProtectedLocalFields,
                    createResult.IsSuccess ? [] : [createResult.Error!.Code],
                    createResult.IsSuccess
                        ? "Only a new worker may initialize its local name from the source."
                        : createResult.Error!.Message)));
            sourceIndex++;
        }

        foreach (var worker in localWorkers.Where(worker => !matchedWorkerIds.Contains(worker.Id)))
        {
            rows.Add(new WorkerSyncPlanRow(
                null,
                worker,
                null,
                false,
                CreatePreviewRow(
                    WorkerSyncActions.ExistingWorkerUnchanged,
                    worker,
                    null,
                    workerSyncPolicy.ProtectedLocalFields,
                    [],
                    "Missing from USERINFO does not imply LeftEmployment.")));
        }

        return new WorkerSyncPlan(snapshot, validation, rows);
    }

    private static SourceProcessingOutcome[] CreateWorkerOutcomes(
        WorkerSyncPlan plan,
        IReadOnlySet<long> failedSourceIds) =>
        plan.Rows
            .Where(row => row.IsClaimed && row.SourceRecordId.HasValue)
            .Select(row =>
            {
                var explicitlyFailed = failedSourceIds.Contains(row.SourceRecordId!.Value);
                var invalid = row.Preview.Action is WorkerSyncActions.IdentityConflict or WorkerSyncActions.UnsupportedSourceState;
                return explicitlyFailed
                    ? SourceProcessingOutcome.Failed(row.SourceRecordId.Value, "WorkerCreationFailed")
                    : invalid
                        ? SourceProcessingOutcome.Failed(row.SourceRecordId.Value, row.Preview.Action)
                        : SourceProcessingOutcome.Processed(row.SourceRecordId.Value);
            })
            .ToArray();

    private async Task FailClaimedWorkerRowsAsync(WorkerIdentitySourceBatch batch, string errorCode)
    {
        var outcomes = batch.Items
            .Where(item => item.IsClaimed && item.SourceRecordId.HasValue)
            .Select(item => SourceProcessingOutcome.Failed(item.SourceRecordId!.Value, errorCode))
            .ToArray();
        var acknowledgement = await workerIdentitySource.CompleteBatchAsync(batch, outcomes, CancellationToken.None);
        if (acknowledgement.IsFailure)
        {
            logger.LogWarning("Worker staging rows could not be released after a failed domain transaction.");
        }
    }

    private WorkerActiveServiceSyncPreviewDto MapPreview(WorkerSyncPlan plan, IReadOnlyCollection<Worker> localWorkers)
    {
        var existingUnchanged = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.ExistingWorkerUnchanged);
        var existingUpdated = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.ExistingWorkerUpdated);
        var newCandidates = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.NewWorkerCandidate);
        var identityConflicts = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.IdentityConflict);
        var unsupported = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.UnsupportedSourceState);

        return new WorkerActiveServiceSyncPreviewDto
        {
            IsReadOnly = true,
            CanApply = false,
            CurrentLocalWorkers = localWorkers.Count,
            ActiveOnServiceWorkersInZkTime = plan.Snapshot.Rows.Count(row => row.IsActive),
            WorkersToRemainActive = existingUnchanged + existingUpdated,
            WorkersToReactivate = 0,
            WorkersToCreate = newCandidates,
            WorkersToMarkInactiveOrExcluded = 0,
            WorkersAlreadyInactiveOrExcluded = localWorkers.Count(worker => !worker.IsActive),
            WorkersSafelyRemovable = 0,
            WarningCount = identityConflicts + unsupported,
            IdentityConflictCount = identityConflicts,
            UnsupportedSourceStateCount = unsupported,
            SnapshotIssues = plan.Validation.Issues,
            Rows = plan.Rows.Select(row => row.Preview).ToArray()
        };
    }

    private WorkerMasterSyncPreviewRowDto CreatePreviewRow(
        string action,
        Worker? worker,
        AttendanceEmployeeRecord? source,
        IReadOnlyCollection<string> protectedFields,
        IReadOnlyCollection<string> conflicts,
        string reason) =>
        new(
            action,
            worker?.Id,
            worker?.EmployeeCode,
            worker?.FullName,
            source?.AttendanceUserId,
            source?.BadgeNumber,
            source?.EmployeeCode,
            source?.Name,
            source?.EmploymentStatus ?? (source is null ? null : source.IsActive ? "ObservedPresentInCurrentEmployeesImport" : "ObservedAbsentFromCurrentEmployeesImport"),
            source?.DepartmentId,
            source?.Department,
            source?.Shift,
            protectedFields,
            conflicts,
            reason);

    private static Dictionary<string, List<Worker>> BuildLookup(
        IEnumerable<Worker> workers,
        Func<Worker, string?> selector)
    {
        var lookup = new Dictionary<string, List<Worker>>(StringComparer.OrdinalIgnoreCase);
        foreach (var worker in workers)
        {
            var identity = WorkerSyncPolicy.Normalize(selector(worker));
            if (identity is null) continue;
            if (!lookup.TryGetValue(identity, out var matches))
            {
                matches = [];
                lookup[identity] = matches;
            }
            matches.Add(worker);
        }
        return lookup;
    }

    private static IdentityResolution ResolveWorkerByIdentityPriority(
        IReadOnlyDictionary<string, List<Worker>> attendanceUsers,
        IReadOnlyDictionary<string, List<Worker>> badges,
        IReadOnlyDictionary<string, List<Worker>> employeeCodes,
        string? attendanceUserId,
        string? badgeNumber,
        string? employeeCode)
    {
        var orderedLookups = new[]
        {
            (Name: "AttendanceUserId", Lookup: attendanceUsers, Identity: attendanceUserId),
            (Name: "BadgeNumber", Lookup: badges, Identity: badgeNumber),
            (Name: "EmployeeCode", Lookup: employeeCodes, Identity: employeeCode)
        };

        Worker? selected = null;
        foreach (var (_, lookup, identity) in orderedLookups)
        {
            if (identity is null || !lookup.TryGetValue(identity, out var matches))
            {
                continue;
            }

            if (matches.Count != 1)
            {
                return new IdentityResolution(null, "LocalIdentityIsNotUnique");
            }

            selected ??= matches[0];
            if (selected.Id != matches[0].Id)
            {
                return new IdentityResolution(null, "SourceIdentityMatchesMultipleWorkers");
            }
        }

        return new IdentityResolution(selected, null);
    }

    private sealed record WorkerSyncPlan(
        WorkerSourceSnapshot Snapshot,
        WorkerSnapshotValidation Validation,
        IReadOnlyCollection<WorkerSyncPlanRow> Rows);

    private sealed record WorkerSyncPlanRow(
        AttendanceEmployeeRecord? Source,
        Worker? Worker,
        long? SourceRecordId,
        bool IsClaimed,
        WorkerMasterSyncPreviewRowDto Preview);

    private sealed record WorkerSourceRead(
        WorkerSourceSnapshot Snapshot,
        IReadOnlyCollection<WorkerIdentitySourceItem> Items,
        WorkerIdentitySourceBatch Batch);

    private sealed record IdentityResolution(Worker? Worker, string? ConflictReason)
    {
        public bool IsConflict => ConflictReason is not null;
    }
}
