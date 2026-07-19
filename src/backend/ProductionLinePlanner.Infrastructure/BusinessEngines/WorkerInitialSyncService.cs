using Microsoft.EntityFrameworkCore;
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
/// Separate worker-master capability. Existing planner workers are comparison-only; only a new,
/// structurally valid source identity may be initialized, and no attendance transaction is read.
/// </summary>
public sealed class WorkerInitialSyncService(
    AppDbContext dbContext,
    IAttendanceEmployeeReader attendanceEmployeeReader,
    IWorkerSyncPolicy workerSyncPolicy,
    IAuthoritativeWorkerSnapshotValidator snapshotValidator,
    IAuditEngine auditEngine) : IWorkerInitialSyncService
{
    public async Task<Result<WorkerActiveServiceSyncPreviewDto>> PreviewActiveServiceSyncAsync(
        CancellationToken cancellationToken = default)
    {
        var sourceResult = await ReadSourceSnapshotAsync(cancellationToken);
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

        var startedAtUtc = DateTime.UtcNow;
        var sourceResult = await ReadSourceSnapshotAsync(cancellationToken);
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

        foreach (var candidate in plan.Rows.Where(row => row.Preview.Action == WorkerSyncActions.NewWorkerCandidate))
        {
            var workerResult = workerSyncPolicy.CreateNewWorker(candidate.Source!, DateTime.UtcNow);
            if (workerResult.IsFailure)
            {
                continue;
            }

            dbContext.Workers.Add(workerResult.Value!);
            createdCount++;
        }

        var completedAtUtc = DateTime.UtcNow;
        var identityConflictCount = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.IdentityConflict);
        var unsupportedCount = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.UnsupportedSourceState);
        var missingFromSourceCount = plan.Rows.Count(row => row.Source is null && row.Worker is not null);
        var unchangedCount = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.ExistingWorkerUnchanged);
        var result = new WorkerInitialSyncResultDto
        {
            SourceCount = sourceResult.Value!.Rows.Count,
            CreatedCount = createdCount,
            UpdatedCount = 0,
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
            await auditEngine.RecordAsync(
                actorUserId,
                AuditActionType.WorkerInitialSync,
                nameof(Worker),
                nameof(Worker),
                before: null,
                after: result,
                requestMeta: requestMeta,
                cancellationToken: cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Result<WorkerInitialSyncResultDto>.Failure(new Error(
                "WorkerInitialSyncFailed",
                "Unable to persist worker import results."));
        }

        return Result<WorkerInitialSyncResultDto>.Success(result);
    }

    private async Task<Result<WorkerSourceSnapshot>> ReadSourceSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sourceResult = await attendanceEmployeeReader.GetAllAsync(cancellationToken);
            if (sourceResult.IsFailure)
            {
                return Result<WorkerSourceSnapshot>.Failure(sourceResult.Error!);
            }

            var orderedRows = (sourceResult.Value ?? [])
                .OrderBy(row => WorkerSyncPolicy.Normalize(row.AttendanceUserId), StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => WorkerSyncPolicy.Normalize(row.BadgeNumber), StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => WorkerSyncPolicy.Normalize(row.EmployeeCode), StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => WorkerSyncPolicy.Normalize(row.Name), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Result<WorkerSourceSnapshot>.Success(new WorkerSourceSnapshot(
                orderedRows,
                IsComplete: false,
                AbsenceIsAuthoritative: false,
                EmploymentStatusIsAuthoritative: false,
                DepartmentIsAuthoritative: false,
                ShiftIsAuthoritative: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Result<WorkerSourceSnapshot>.Failure(new Error(
                "AttendanceSourceError",
                "Unable to read worker data from attendance source."));
        }
    }

    private WorkerSyncPlan BuildPlan(WorkerSourceSnapshot snapshot, IReadOnlyCollection<Worker> localWorkers)
    {
        var validation = snapshotValidator.Inspect(snapshot);
        var attendanceUsers = BuildLookup(localWorkers, worker => worker.AttendanceUserId);
        var badges = BuildLookup(localWorkers, worker => worker.BadgeNumber);
        var employeeCodes = BuildLookup(localWorkers, worker => worker.EmployeeCode);
        var matchedWorkerIds = new HashSet<Guid>();
        var rows = new List<WorkerSyncPlanRow>();

        var sourceIndex = 0;
        foreach (var source in snapshot.Rows)
        {
            var sourceAttendanceUserId = WorkerSyncPolicy.Normalize(source.AttendanceUserId);
            var sourceBadge = WorkerSyncPolicy.Normalize(source.BadgeNumber);
            var sourceEmployeeCode = WorkerSyncPolicy.Normalize(source.EmployeeCode);
            var sourceConflicts = new List<string>();

            if (validation.InvalidSourceRowIndexes.Contains(sourceIndex))
            {
                rows.Add(new WorkerSyncPlanRow(
                    source,
                    null,
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

            var candidateWorkers = new Dictionary<Guid, Worker>();
            AddCandidates(candidateWorkers, attendanceUsers, sourceAttendanceUserId);
            AddCandidates(candidateWorkers, badges, sourceBadge);
            AddCandidates(candidateWorkers, employeeCodes, sourceEmployeeCode);

            if (candidateWorkers.Count > 1)
            {
                rows.Add(new WorkerSyncPlanRow(
                    source,
                    null,
                    CreatePreviewRow(
                        WorkerSyncActions.IdentityConflict,
                        null,
                        source,
                        workerSyncPolicy.ProtectedLocalFields,
                        ["SourceIdentityMatchesMultipleWorkers"],
                        "The source identities resolve to more than one local worker.")));
                sourceIndex++;
                continue;
            }

            if (candidateWorkers.Count == 1)
            {
                var worker = candidateWorkers.Values.Single();
                matchedWorkerIds.Add(worker.Id);
                var decision = workerSyncPolicy.EvaluateExistingWorker(worker, source);
                rows.Add(new WorkerSyncPlanRow(
                    source,
                    worker,
                    CreatePreviewRow(
                        decision.Action,
                        worker,
                        source,
                        decision.ProtectedLocalFields,
                        decision.IdentityConflicts,
                        decision.Action == WorkerSyncActions.IdentityConflict
                            ? "Identity differences require explicit reconciliation and are never applied automatically."
                            : "Existing planner-owned worker data remains unchanged.")));
                sourceIndex++;
                continue;
            }

            var createResult = workerSyncPolicy.CreateNewWorker(source, DateTime.UnixEpoch);
            rows.Add(new WorkerSyncPlanRow(
                source,
                null,
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
                CreatePreviewRow(
                    WorkerSyncActions.ExistingWorkerUnchanged,
                    worker,
                    null,
                    workerSyncPolicy.ProtectedLocalFields,
                    [],
                    "Missing from CurrentEmployeesImport does not imply LeftEmployment.")));
        }

        return new WorkerSyncPlan(snapshot, validation, rows);
    }

    private WorkerActiveServiceSyncPreviewDto MapPreview(WorkerSyncPlan plan, IReadOnlyCollection<Worker> localWorkers)
    {
        var existingUnchanged = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.ExistingWorkerUnchanged);
        var newCandidates = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.NewWorkerCandidate);
        var identityConflicts = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.IdentityConflict);
        var unsupported = plan.Rows.Count(row => row.Preview.Action == WorkerSyncActions.UnsupportedSourceState);

        return new WorkerActiveServiceSyncPreviewDto
        {
            IsReadOnly = true,
            CanApply = false,
            CurrentLocalWorkers = localWorkers.Count,
            ActiveOnServiceWorkersInZkTime = plan.Snapshot.Rows.Count(row => row.IsActive),
            WorkersToRemainActive = existingUnchanged,
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

    private static void AddCandidates(
        IDictionary<Guid, Worker> candidates,
        IReadOnlyDictionary<string, List<Worker>> lookup,
        string? identity)
    {
        if (identity is null || !lookup.TryGetValue(identity, out var matches)) return;
        foreach (var worker in matches) candidates[worker.Id] = worker;
    }

    private sealed record WorkerSyncPlan(
        WorkerSourceSnapshot Snapshot,
        WorkerSnapshotValidation Validation,
        IReadOnlyCollection<WorkerSyncPlanRow> Rows);

    private sealed record WorkerSyncPlanRow(
        AttendanceEmployeeRecord? Source,
        Worker? Worker,
        WorkerMasterSyncPreviewRowDto Preview);
}
