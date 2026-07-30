using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

internal sealed class ProcessedAttendanceOrphanEngine(
    AppDbContext dbContext,
    IProcessedAttendanceInboxStore inboxStore,
    IAttendanceWorkdayPolicy workdayPolicy,
    ICairoTimeZoneProvider cairoTimeZoneProvider,
    IOptions<AttendanceSourceOptions> sourceOptions,
    IAttendanceSyncService attendanceSyncService,
    IAuditEngine auditEngine,
    ILogger<ProcessedAttendanceOrphanEngine> logger) : IProcessedAttendanceOrphanEngine
{
    internal const string ExecuteConfirmation = "REPAIR-PROCESSED-ATTENDANCE-ORPHANS";
    private const int MaximumBatchSize = 100;
    private const int MaximumDateSpanDays = 31;
    private const int MaximumScanRows = 5000;
    private readonly AttendanceSourceOptions options = sourceOptions.Value;

    public async Task<Result<ProcessedAttendanceOrphanPreviewDto>> PreviewAsync(
        ProcessedAttendanceOrphanQuery query,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(query.FromOperationalDate, query.ToOperationalDate, query.MaximumRows);
        if (validationError is not null)
        {
            return Result<ProcessedAttendanceOrphanPreviewDto>.Failure(validationError);
        }

        if (!options.UsesStaging)
        {
            return Result<ProcessedAttendanceOrphanPreviewDto>.Failure(new Error(
                "AttendanceStagingRequired",
                "Processed attendance orphan repair is available only when the durable staging source is enabled."));
        }

        var start = workdayPolicy.GetWindow(query.FromOperationalDate).StartLocal;
        var end = workdayPolicy.GetWindow(query.ToOperationalDate).EndLocal;
        var scanLimit = Math.Min(MaximumScanRows, Math.Max(query.MaximumRows + 1, query.MaximumRows * 20));
        var candidates = await inboxStore.ReadProcessedAsync(
            start,
            end,
            query.SourceUserId,
            Normalize(query.BadgeNumber),
            scanLimit,
            cancellationToken);

        var evaluated = await EvaluateAsync(candidates, query.BadgeNumber, cancellationToken);
        var items = evaluated
            .Where(item => item.IsOrphan)
            .Take(query.MaximumRows)
            .Select(item => item.Dto!)
            .ToArray();
        var groups = items
            .GroupBy(item => new { item.OperationalDate, item.WorkerId, item.WorkerName, item.BadgeNumber })
            .Select(group => new ProcessedAttendanceOrphanGroupDto(
                group.Key.OperationalDate,
                group.Key.WorkerId,
                group.Key.WorkerName,
                group.Key.BadgeNumber,
                group.Count()))
            .OrderBy(group => group.OperationalDate)
            .ThenBy(group => group.WorkerName)
            .ThenBy(group => group.WorkerId)
            .ToArray();

        return Result<ProcessedAttendanceOrphanPreviewDto>.Success(new ProcessedAttendanceOrphanPreviewDto(
            query with { BadgeNumber = Normalize(query.BadgeNumber) },
            items.Length,
            candidates.Count == scanLimit || evaluated.Count(item => item.IsOrphan) > query.MaximumRows,
            groups,
            items));
    }

    public async Task<Result<ProcessedAttendanceOrphanRepairDto>> RepairAsync(
        Guid actorUserId,
        ProcessedAttendanceOrphanRepairRequest request,
        string? requestMetadata = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<ProcessedAttendanceOrphanRepairDto>.Failure(new Error("Unauthorized", "An authenticated operator is required."));
        }

        var previewQuery = new ProcessedAttendanceOrphanQuery(
            request.FromOperationalDate,
            request.ToOperationalDate,
            request.SourceUserId,
            request.BadgeNumber,
            request.MaximumRows);
        var previewResult = await PreviewAsync(previewQuery, cancellationToken);
        if (previewResult.IsFailure)
        {
            return Result<ProcessedAttendanceOrphanRepairDto>.Failure(previewResult.Error!);
        }

        var preview = previewResult.Value!;
        if (!request.Execute)
        {
            return Result<ProcessedAttendanceOrphanRepairDto>.Success(new ProcessedAttendanceOrphanRepairDto(false, preview, []));
        }

        if (!string.Equals(request.Confirmation, ExecuteConfirmation, StringComparison.Ordinal))
        {
            return Result<ProcessedAttendanceOrphanRepairDto>.Failure(new Error(
                "ConfirmationRequired",
                $"Explicit confirmation value '{ExecuteConfirmation}' is required."));
        }

        var selectedIds = request.InboxIds is { Count: > 0 }
            ? request.InboxIds.Distinct().ToHashSet()
            : preview.Items.Select(item => item.InboxId).ToHashSet();
        if (selectedIds.Count > request.MaximumRows || selectedIds.Count > MaximumBatchSize)
        {
            return Result<ProcessedAttendanceOrphanRepairDto>.Failure(new Error("BatchLimitExceeded", $"A repair batch cannot exceed {MaximumBatchSize} rows."));
        }

        if (selectedIds.Except(preview.Items.Select(item => item.InboxId)).Any())
        {
            return Result<ProcessedAttendanceOrphanRepairDto>.Failure(new Error(
                "SelectionOutsidePreview",
                "Every selected inbox row must be present in the current bounded preview."));
        }

        var operationId = Guid.NewGuid();
        var initialResults = new List<ProcessedAttendanceOrphanRepairItemDto>();
        var datesToReplay = new HashSet<DateOnly>();
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            foreach (var previewItem in preview.Items.Where(item => selectedIds.Contains(item.InboxId)))
            {
                var locked = await inboxStore.ReadForUpdateAsync(previewItem.InboxId, cancellationToken);
                if (locked is null || !string.Equals(locked.ProcessingStatus, "Processed", StringComparison.OrdinalIgnoreCase))
                {
                    initialResults.Add(new(previewItem.InboxId, "NoLongerOrphan", null, "The inbox row is no longer Processed."));
                    continue;
                }

                var evaluation = (await EvaluateAsync([locked], request.BadgeNumber, cancellationToken)).Single();
                if (evaluation.Worker is null)
                {
                    initialResults.Add(new(locked.InboxId, "NoWorkerMapping", null, evaluation.Details));
                    continue;
                }

                if (evaluation.HasExactEvidence)
                {
                    var marked = await inboxStore.MarkAlreadyImportedAsync(
                        locked,
                        $"Repair operation {operationId:D} proved exact attendance evidence before replay.",
                        cancellationToken);
                    initialResults.Add(marked
                        ? new(locked.InboxId, "AlreadyImported", "AlreadyImported", "Exact attendance evidence already exists; no replay was performed.")
                        : new(locked.InboxId, "NoLongerOrphan", null, "The row changed concurrently before its outcome could be recorded."));
                    continue;
                }

                if (!evaluation.IsOrphan)
                {
                    initialResults.Add(new(locked.InboxId, "NoLongerOrphan", null, evaluation.Details));
                    continue;
                }

                var requeued = await inboxStore.RequeueAsync(
                    locked,
                    $"Selected by processed-orphan repair operation {operationId:D}; exact evidence was absent at recheck; previous AttemptCount={locked.AttemptCount}.",
                    cancellationToken);
                if (!requeued)
                {
                    initialResults.Add(new(locked.InboxId, "NoLongerOrphan", null, "The row changed concurrently before replay."));
                    continue;
                }

                datesToReplay.Add(evaluation.OperationalDate);
                initialResults.Add(new(locked.InboxId, "Requeued", "ProcessedOrphanRequeued", "Queued for replay through the attendance processor."));
            }

            var auditResult = await auditEngine.RecordAsync(
                actorUserId,
                AuditActionType.Resolve,
                "ProcessedAttendanceOrphanRepair",
                operationId.ToString("D"),
                after: new
                {
                    RequestedCount = selectedIds.Count,
                    RecordId = string.Join(",", selectedIds.Order()),
                    AddedCount = initialResults.Count(item => item.Result == "Requeued"),
                    SkippedCount = initialResults.Count(item => item.Result is "NoLongerOrphan" or "NoWorkerMapping"),
                    FailedCount = 0,
                    Result = "QueuedForControlledReplay"
                },
                requestMeta: requestMetadata,
                cancellationToken: cancellationToken);
            if (auditResult.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<ProcessedAttendanceOrphanRepairDto>.Failure(auditResult.Error!);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var syncFailures = new Dictionary<DateOnly, string>();
        foreach (var date in datesToReplay.Order())
        {
            // Mutation has committed; completing the bounded replay must not depend on a client
            // disconnect. AttendanceSyncCoordinator provides its own timeout and serialization.
            var sync = await attendanceSyncService.SyncForProductionDateAsync(date, CancellationToken.None);
            if (sync.IsFailure)
            {
                syncFailures[date] = sync.Error?.Code ?? "AttendanceSyncFailed";
            }
        }

        dbContext.ChangeTracker.Clear();
        var finalResults = new List<ProcessedAttendanceOrphanRepairItemDto>(initialResults.Count);
        foreach (var item in initialResults)
        {
            if (item.Result != "Requeued")
            {
                finalResults.Add(item);
                continue;
            }

            var previewItem = preview.Items.Single(candidate => candidate.InboxId == item.InboxId);
            if (syncFailures.TryGetValue(previewItem.OperationalDate, out var syncError))
            {
                finalResults.Add(new(item.InboxId, "Failed", syncError, "Replay did not complete; the row remains retryable."));
                continue;
            }

            var state = await inboxStore.ReadStateAsync(item.InboxId, cancellationToken);
            var finalEvaluation = await EvaluateByIdAsync(item.InboxId, cancellationToken);
            if (state is { ProcessingStatus: "Processed", ResolutionCode: "AlreadyImported" })
            {
                finalResults.Add(new(item.InboxId, "AlreadyImported", state.ResolutionCode, state.ResolutionDetails));
            }
            else if (state is { ProcessingStatus: "Processed" } && finalEvaluation is { HasExactEvidence: true })
            {
                finalResults.Add(new(item.InboxId, "Repaired", state.ResolutionCode, state.ResolutionDetails));
            }
            else if (state is { ProcessingStatus: "Skipped" })
            {
                finalResults.Add(new(item.InboxId, "Skipped", state.ResolutionCode, state.ResolutionDetails));
            }
            else if (state is { ProcessingStatus: "Failed" })
            {
                finalResults.Add(new(item.InboxId, "Failed", state.ResolutionCode, state.ResolutionDetails));
            }
            else
            {
                finalResults.Add(new(item.InboxId, "Failed", state?.ResolutionCode, state?.ResolutionDetails ?? "Replay did not produce a terminal valid outcome."));
            }
        }

        logger.LogInformation(
            "Processed attendance orphan repair completed. operationId={OperationId}, selected={SelectedCount}, repaired={RepairedCount}, alreadyImported={AlreadyImportedCount}, failed={FailedCount}",
            operationId,
            selectedIds.Count,
            finalResults.Count(item => item.Result == "Repaired"),
            finalResults.Count(item => item.Result == "AlreadyImported"),
            finalResults.Count(item => item.Result == "Failed"));

        return Result<ProcessedAttendanceOrphanRepairDto>.Success(new ProcessedAttendanceOrphanRepairDto(true, preview, finalResults));
    }

    private async Task<OrphanEvaluation?> EvaluateByIdAsync(long inboxId, CancellationToken cancellationToken)
    {
        var row = await inboxStore.ReadForUpdateAsync(inboxId, cancellationToken);
        return row is null ? null : (await EvaluateAsync([row], null, cancellationToken)).Single();
    }

    private async Task<IReadOnlyList<OrphanEvaluation>> EvaluateAsync(
        IReadOnlyCollection<ProcessedAttendanceInboxRow> rows,
        string? requestedBadge,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];

        var sourceUserIds = rows.Select(row => row.SourceUserId.ToString()).Distinct().ToArray();
        var badges = rows.Select(row => Normalize(row.BadgeNumber)).Where(value => value is not null).Cast<string>().Distinct().ToArray();
        var workers = await dbContext.Workers
            .AsNoTracking()
            .Where(worker => (worker.AttendanceUserId != null && sourceUserIds.Contains(worker.AttendanceUserId))
                || (worker.BadgeNumber != null && badges.Contains(worker.BadgeNumber)))
            .ToListAsync(cancellationToken);

        var identityResolver = new AttendanceWorkerIdentityResolver(workers);
        var workersById = workers.ToDictionary(worker => worker.Id);
        var resolved = rows.Select(row => new { Row = row, Worker = ResolveWorker(row, identityResolver, workersById) }).ToArray();
        var workerIds = resolved.Where(item => item.Worker is not null).Select(item => item.Worker!.Id).Distinct().ToArray();
        var minUtc = rows.Min(row => ToUtc(row.SourceCheckTimeLocal)).AddDays(-1);
        var maxUtc = rows.Max(row => ToUtc(row.SourceCheckTimeLocal)).AddDays(1);
        var records = workerIds.Length == 0
            ? []
            : await dbContext.AttendanceRecords.AsNoTracking()
                .Where(record => workerIds.Contains(record.WorkerId)
                    && record.AttendanceTimeUtc >= minUtc
                    && record.AttendanceTimeUtc < maxUtc)
                .ToListAsync(cancellationToken);

        var normalizedRequestedBadge = Normalize(requestedBadge);
        return resolved.Select(item =>
        {
            var worker = item.Worker;
            var expectedUtc = ToUtc(item.Row.SourceCheckTimeLocal);
            var type = NormalizePunchType(item.Row.SourceCheckType);
            var operationalDate = workdayPolicy.GetOperationalDate(expectedUtc);
            if (worker is null)
            {
                return new OrphanEvaluation(item.Row, null, expectedUtc, operationalDate, false, false, null, "No unique active worker mapping exists.");
            }

            if (normalizedRequestedBadge is not null && !string.Equals(Normalize(worker.BadgeNumber), normalizedRequestedBadge, StringComparison.OrdinalIgnoreCase))
            {
                return new OrphanEvaluation(item.Row, worker, expectedUtc, operationalDate, false, false, null, "The mapped worker does not match the requested badge filter.");
            }

            var hasExact = type is not null && records.Any(record => AttendancePunchEvidenceMatcher.IsExact(
                record,
                worker.Id,
                options.SourceName,
                expectedUtc,
                type == "I",
                item.Row.SourceRawId));
            var isProcessed = string.Equals(item.Row.ProcessingStatus, "Processed", StringComparison.OrdinalIgnoreCase);
            var isOrphan = isProcessed && !hasExact;
            var dto = isOrphan
                ? new ProcessedAttendanceOrphanItemDto(
                    item.Row.InboxId,
                    item.Row.SourceUserId,
                    item.Row.BadgeNumber ?? worker.BadgeNumber,
                    item.Row.SourceCheckTimeLocal,
                    item.Row.SourceCheckType,
                    item.Row.AttemptCount,
                    expectedUtc,
                    operationalDate,
                    worker.Id,
                    worker.FullName,
                    "ProcessedWithoutAttendance")
                : null;
            return new OrphanEvaluation(
                item.Row,
                worker,
                expectedUtc,
                operationalDate,
                hasExact,
                isOrphan,
                dto,
                hasExact ? "Exact attendance evidence exists." : "ProcessedWithoutAttendance");
        }).ToArray();
    }

    private static Worker? ResolveWorker(
        ProcessedAttendanceInboxRow row,
        AttendanceWorkerIdentityResolver identityResolver,
        IReadOnlyDictionary<Guid, Worker> workersById)
    {
        if (identityResolver.Resolve(row.SourceUserId.ToString(), row.BadgeNumber, out var workerId) != AttendanceWorkerIdentityResolution.Resolved
            || !workersById.TryGetValue(workerId, out var worker)) return null;
        return worker is { IsActive: true, EmploymentStatus: EmploymentStatus.Active } ? worker : null;
    }

    private DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(
        DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
        cairoTimeZoneProvider.TimeZone);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizePunchType(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "I" => "I",
        "O" => "O",
        _ => null
    };

    private static Error? Validate(DateOnly from, DateOnly to, int maximumRows)
    {
        if (from == default || to == default) return new Error("ValidationError", "A mandatory operational date range is required.");
        if (to < from) return new Error("ValidationError", "The end date must not precede the start date.");
        if (to.DayNumber - from.DayNumber + 1 > MaximumDateSpanDays) return new Error("ValidationError", $"The date range cannot exceed {MaximumDateSpanDays} days.");
        if (maximumRows is < 1 or > MaximumBatchSize) return new Error("ValidationError", $"MaximumRows must be between 1 and {MaximumBatchSize}.");
        return null;
    }

    private sealed record OrphanEvaluation(
        ProcessedAttendanceInboxRow Row,
        Worker? Worker,
        DateTime ExpectedUtc,
        DateOnly OperationalDate,
        bool HasExactEvidence,
        bool IsOrphan,
        ProcessedAttendanceOrphanItemDto? Dto,
        string Details);
}
