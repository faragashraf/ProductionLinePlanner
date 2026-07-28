using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

public sealed class AttendanceSyncService : IAttendanceReadService, IAttendanceSyncRunner
{
    private const string TempStatusActive = "Active";
    private const string TempStatusScheduled = "Scheduled";
    private const string SyncAbsentStatus = "sync-no-source";

    private readonly AppDbContext _appDbContext;
    private readonly IAttendanceSource _attendanceSource;
    private readonly AttendanceSourceOptions _sourceOptions;
    private readonly ILogger<AttendanceSyncService> _logger;
    private readonly ICairoTimeZoneProvider _cairoTimeZoneProvider;
    private readonly IWorkerInitialSyncService _workerSyncService;
    private readonly IManufacturingRealtimeChangeContext? _realtimeChangeContext;

    public AttendanceSyncService(
        AppDbContext appDbContext,
        IAttendanceSource attendanceSource,
        IOptions<AttendanceSourceOptions> sourceOptions,
        ILogger<AttendanceSyncService> logger,
        ICairoTimeZoneProvider cairoTimeZoneProvider,
        IWorkerInitialSyncService workerSyncService,
        IManufacturingRealtimeChangeContext? realtimeChangeContext = null)
    {
        _appDbContext = appDbContext;
        _attendanceSource = attendanceSource;
        _sourceOptions = sourceOptions.Value;
        _logger = logger;
        _cairoTimeZoneProvider = cairoTimeZoneProvider;
        _workerSyncService = workerSyncService;
        _realtimeChangeContext = realtimeChangeContext;
    }

    public async Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(
        Guid? factoryId = null,
        Guid? lineId = null,
        DateTime? dateUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (dateUtc.HasValue && dateUtc.Value.Kind != DateTimeKind.Utc)
        {
            return Result<AttendanceWorkerStateDto[]>.Failure(new Error("ValidationError", "Date must be UTC."));
        }

        var forDate = GetDateOnly(dateUtc);
        var workerFilter = await GetVisibleWorkerIdsForScopeAsync(
            forDate,
            factoryId,
            lineId,
            cancellationToken);

        var workers = await _appDbContext.Workers
            .AsNoTracking()
            .Where(x => workerFilter.Contains(x.Id) && x.IsActive && x.EmploymentStatus == EmploymentStatus.Active)
            .Select(x => new
            {
                x.Id,
                x.EmployeeCode,
                x.FullName,
                x.AttendanceUserId,
                x.BadgeNumber
            })
            .ToListAsync(cancellationToken);

        if (workers.Count == 0)
        {
            return Result<AttendanceWorkerStateDto[]>.Success(Array.Empty<AttendanceWorkerStateDto>());
        }

        var attendanceByWorker = await GetLatestAttendanceByWorkerForDateAsync(
            workers.Select(x => x.Id),
            forDate,
            cancellationToken);

        var items = workers
            .Select(worker =>
            {
                if (attendanceByWorker.TryGetValue(worker.Id, out var attendance))
                {
                    return new AttendanceWorkerStateDto
                    {
                        WorkerId = worker.Id,
                        EmployeeCode = worker.EmployeeCode,
                        FullName = worker.FullName,
                        AttendanceStatus = attendance.AttendanceStatus,
                        AttendanceTimeUtc = attendance.AttendanceTimeUtc,
                        Source = attendance.Source,
                        AttendanceUserId = worker.AttendanceUserId,
                        BadgeNumber = worker.BadgeNumber
                    };
                }

                return new AttendanceWorkerStateDto
                {
                    WorkerId = worker.Id,
                    EmployeeCode = worker.EmployeeCode,
                    FullName = worker.FullName,
                    AttendanceStatus = AttendanceStatus.Unassigned,
                    AttendanceTimeUtc = forDate,
                    Source = null,
                    AttendanceUserId = worker.AttendanceUserId,
                    BadgeNumber = worker.BadgeNumber
                };
            })
            .OrderBy(x => x.EmployeeCode)
            .ToArray();

        return Result<AttendanceWorkerStateDto[]>.Success(items);
    }

    public async Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(
        Guid workerId,
        DateTime? fromDateUtc = null,
        DateTime? toDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _appDbContext.Workers.AnyAsync(x => x.Id == workerId && x.IsActive && x.EmploymentStatus == EmploymentStatus.Active, cancellationToken))
        {
            return Result<AttendanceRecordDto[]>.Failure(new Error("NotFound", "Worker not found."));
        }

        if (fromDateUtc.HasValue && fromDateUtc.Value.Kind != DateTimeKind.Utc)
        {
            return Result<AttendanceRecordDto[]>.Failure(new Error("ValidationError", "fromDate must be UTC."));
        }

        if (toDateUtc.HasValue && toDateUtc.Value.Kind != DateTimeKind.Utc)
        {
            return Result<AttendanceRecordDto[]>.Failure(new Error("ValidationError", "toDate must be UTC."));
        }

        if (fromDateUtc.HasValue && toDateUtc.HasValue && toDateUtc.Value < fromDateUtc.Value)
        {
            return Result<AttendanceRecordDto[]>.Failure(new Error("ValidationError", "toDate must be after fromDate."));
        }

        IQueryable<AttendanceRecord> query = _appDbContext.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.WorkerId == workerId)
            .OrderByDescending(x => x.AttendanceTimeUtc);

        if (fromDateUtc.HasValue)
        {
            query = query.Where(x => x.AttendanceTimeUtc >= fromDateUtc.Value);
        }

        if (toDateUtc.HasValue)
        {
            query = query.Where(x => x.AttendanceTimeUtc <= toDateUtc.Value);
        }

        var items = await query
            .Select(x => new AttendanceRecordDto
            {
                Id = x.Id,
                WorkerId = x.WorkerId,
                AttendanceTimeUtc = x.AttendanceTimeUtc,
                AttendanceStatus = x.AttendanceStatus,
                Source = x.Source,
                AttendanceUserId = x.AttendanceUserId,
                BadgeNumber = x.BadgeNumber,
                SourceRawId = x.SourceRawId
            })
            .ToArrayAsync(cancellationToken);

        return Result<AttendanceRecordDto[]>.Success(items);
    }

    public async Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(
        Guid subStageId,
        DateTime? dateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var subStage = await _appDbContext.SubStages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);

        if (subStage is null)
        {
            return Result<AttendanceSubStageAttendanceDto>.Failure(new Error("NotFound", "SubStage not found."));
        }

        if (dateUtc.HasValue && dateUtc.Value.Kind != DateTimeKind.Utc)
        {
            return Result<AttendanceSubStageAttendanceDto>.Failure(new Error("ValidationError", "Date must be UTC."));
        }

        var forDate = GetDateOnly(dateUtc);
        var activeWorkerIds = await _appDbContext.Workers
            .AsNoTracking()
            .Where(x => x.IsActive && x.EmploymentStatus == EmploymentStatus.Active)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var assignments = await ResolveCurrentAssignmentsAsync(
            activeWorkerIds,
            forDate,
            cancellationToken);

        var stageWorkers = assignments
            .Where(x => x.Value.EffectiveSubStageId == subStageId)
            .Select(x => x.Key)
            .ToArray();

        var workers = await _appDbContext.Workers
            .AsNoTracking()
            .Where(x => stageWorkers.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.EmployeeCode,
                x.FullName,
                x.AttendanceUserId,
                x.BadgeNumber
            })
            .ToListAsync(cancellationToken);

        var attendanceByWorker = await GetLatestAttendanceByWorkerForDateAsync(
            workers.Select(x => x.Id),
            forDate,
            cancellationToken);

        var present = 0;
        var late = 0;
        var absent = 0;
        var unassigned = 0;

        var workerItems = workers.Select(worker =>
        {
            if (!attendanceByWorker.TryGetValue(worker.Id, out var attendance))
            {
                unassigned++;
                return new AttendanceWorkerStateDto
                {
                    WorkerId = worker.Id,
                    EmployeeCode = worker.EmployeeCode,
                    FullName = worker.FullName,
                    AttendanceStatus = AttendanceStatus.Unassigned,
                    AttendanceTimeUtc = forDate,
                    Source = null,
                    AttendanceUserId = worker.AttendanceUserId,
                    BadgeNumber = worker.BadgeNumber
                };
            }

            if (attendance.AttendanceStatus == AttendanceStatus.Present)
            {
                present++;
            }
            else if (attendance.AttendanceStatus == AttendanceStatus.Late)
            {
                late++;
            }
            else if (attendance.AttendanceStatus == AttendanceStatus.Absent)
            {
                absent++;
            }
            else
            {
                unassigned++;
            }

            return new AttendanceWorkerStateDto
            {
                WorkerId = worker.Id,
                EmployeeCode = worker.EmployeeCode,
                FullName = worker.FullName,
                AttendanceStatus = attendance.AttendanceStatus,
                AttendanceTimeUtc = attendance.AttendanceTimeUtc,
                Source = attendance.Source,
                AttendanceUserId = worker.AttendanceUserId,
                BadgeNumber = worker.BadgeNumber
            };
        }).ToArray();

        return Result<AttendanceSubStageAttendanceDto>.Success(new AttendanceSubStageAttendanceDto
        {
            SubStageId = subStageId,
            SubStageName = subStage.Name,
            DateUtc = forDate,
            Capacity = subStage.Capacity,
            AssignedWorkers = stageWorkers.Length,
            PresentWorkers = present,
            LateWorkers = late,
            AbsentWorkers = absent,
            UnassignedWorkers = Math.Max(0, Math.Max(0, subStage.Capacity - stageWorkers.Length) + unassigned),
            Workers = workerItems
        });
    }

    public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default)
    {
        var cairoNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _cairoTimeZoneProvider.TimeZone);
        return SyncForProductionDateAsync(DateOnly.FromDateTime(cairoNow), cancellationToken);
    }

    public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default)
        => RunAsync(new AttendanceSyncExecutionContext(productionDate, Guid.NewGuid().ToString("N"), "direct"), cancellationToken);

    public async Task<Result<AttendanceSyncResultDto>> RunAsync(
        AttendanceSyncExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        using var realtimeScope = _realtimeChangeContext?.Begin("ZkTimeSync", context.CorrelationId, context.ProductionDate);
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        using var internalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _sourceOptions.SyncReadTimeoutSeconds)));
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, internalTimeout.Token);

        _logger.LogInformation(
            "Attendance sync started. correlationId={CorrelationId}, date={SyncDate}, trigger={TriggerType}, startedAtUtc={StartedAtUtc}",
            context.CorrelationId,
            context.ProductionDate,
            context.TriggerType,
            startedAtUtc);

        try
        {
            var result = await SyncCoreAsync(context, operationCancellation.Token);
            _logger.LogInformation(
                "Attendance sync finished. correlationId={CorrelationId}, date={SyncDate}, trigger={TriggerType}, finishedAtUtc={FinishedAtUtc}, elapsedMs={ElapsedMs}, successful={Successful}, sourceUsers={SourceUsersCount}, sourceCheckIns={SourceCheckInsCount}",
                context.CorrelationId,
                context.ProductionDate,
                context.TriggerType,
                DateTime.UtcNow,
                stopwatch.ElapsedMilliseconds,
                result.IsSuccess,
                result.Value?.SourceUsersCount ?? 0,
                result.Value?.SourceCheckInsCount ?? 0);
            return result;
        }
        catch (OperationCanceledException exception) when (AttendanceSyncFailureClassifier.Classify(exception, cancellationToken.IsCancellationRequested, internalTimeout.IsCancellationRequested) == AttendanceSyncFailureClassifier.ClientCancelled)
        {
            _logger.LogWarning(
                "Attendance sync cancelled by request token. correlationId={CorrelationId}, date={SyncDate}, trigger={TriggerType}, elapsedMs={ElapsedMs}, cancellationSource={CancellationSource}",
                context.CorrelationId,
                context.ProductionDate,
                context.TriggerType,
                stopwatch.ElapsedMilliseconds,
                "request-token");
            return Result<AttendanceSyncResultDto>.Failure(new Error(AttendanceSyncFailureClassifier.ClientCancelled, "Attendance synchronization request was cancelled by the client."));
        }
        catch (OperationCanceledException exception) when (AttendanceSyncFailureClassifier.Classify(exception, cancellationToken.IsCancellationRequested, internalTimeout.IsCancellationRequested) == AttendanceSyncFailureClassifier.InternalTimeout)
        {
            _logger.LogWarning(
                "Attendance sync cancelled by internal timeout. correlationId={CorrelationId}, date={SyncDate}, trigger={TriggerType}, elapsedMs={ElapsedMs}, cancellationSource={CancellationSource}",
                context.CorrelationId,
                context.ProductionDate,
                context.TriggerType,
                stopwatch.ElapsedMilliseconds,
                "internal-timeout");
            return Result<AttendanceSyncResultDto>.Failure(new Error(AttendanceSyncFailureClassifier.InternalTimeout, "Attendance synchronization exceeded its bounded source-read timeout."));
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(
                exception,
                "Attendance sync was cancelled by an undetermined provider source. correlationId={CorrelationId}, date={SyncDate}, trigger={TriggerType}, elapsedMs={ElapsedMs}, cancellationSource={CancellationSource}",
                context.CorrelationId,
                context.ProductionDate,
                context.TriggerType,
                stopwatch.ElapsedMilliseconds,
                "undetermined");
            return Result<AttendanceSyncResultDto>.Failure(new Error(AttendanceSyncFailureClassifier.Cancelled, "Attendance synchronization was cancelled before completion."));
        }
        catch (Exception exception) when (AttendanceSyncFailureClassifier.Classify(exception, cancellationToken.IsCancellationRequested, internalTimeout.IsCancellationRequested) == AttendanceSyncFailureClassifier.SourceTimeout)
        {
            _logger.LogWarning(
                exception,
                "Attendance source SQL command timed out. correlationId={CorrelationId}, date={SyncDate}, trigger={TriggerType}, elapsedMs={ElapsedMs}, cancellationSource={CancellationSource}",
                context.CorrelationId,
                context.ProductionDate,
                context.TriggerType,
                stopwatch.ElapsedMilliseconds,
                "sql-command-timeout");
            return Result<AttendanceSyncResultDto>.Failure(new Error(AttendanceSyncFailureClassifier.SourceTimeout, "Attendance source query timed out."));
        }
    }

    private async Task<Result<AttendanceSyncResultDto>> SyncCoreAsync(
        AttendanceSyncExecutionContext context,
        CancellationToken cancellationToken)
    {
        var productionDate = context.ProductionDate;
        if (productionDate == default)
        {
            return Result<AttendanceSyncResultDto>.Failure(new Error("ValidationError", "Production date is required."));
        }

        var workerSyncResult = await _workerSyncService.SyncWorkersForAttendanceAsync(cancellationToken);
        if (workerSyncResult.IsFailure)
        {
            _logger.LogError(
                "Attendance sync stopped because worker identity synchronization failed. correlationId={CorrelationId}, date={SyncDate}, errorCode={ErrorCode}",
                context.CorrelationId,
                productionDate,
                workerSyncResult.Error?.Code);
            return Result<AttendanceSyncResultDto>.Failure(new Error(
                "WorkerSyncFailed",
                "Worker synchronization must complete before attendance can be matched."));
        }

        var (startUtc, endUtc, startLocal, endLocal) = GetEgyptDayBounds(productionDate);

        List<Worker> workers;
        var sourceBatchResult = await _attendanceSource.ClaimAsync(startLocal, endLocal, cancellationToken);
        if (sourceBatchResult.IsFailure)
        {
            return Result<AttendanceSyncResultDto>.Failure(sourceBatchResult.Error!);
        }

        var sourceBatch = sourceBatchResult.Value!;
        var sourceCheckIns = sourceBatch.Punches.ToArray();

        try
        {
            workers = await _appDbContext.Workers
                .AsNoTracking()
                .Where(x => !string.IsNullOrWhiteSpace(x.AttendanceUserId) || !string.IsNullOrWhiteSpace(x.BadgeNumber))
                .ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException exception) when (exception.Number == -2)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RetryAttendanceBatchAsync(sourceBatch, "ApplicationWorkerReadFailed");
            _logger.LogError(exception, "Failed to read application workers during attendance sync.");
            return Result<AttendanceSyncResultDto>.Failure(new Error("AttendanceSourceError", "Unable to connect to attendance source or read required tables."));
        }

        try
        {
        var sourceUsersCount = sourceBatch.SourceUsersCount;
        var sourceCheckInsCount = sourceCheckIns.Length;
        _logger.LogInformation(
            "Attendance sync source reads completed. correlationId={CorrelationId}, date={SyncDate}, trigger={TriggerType}, sourceUsers={SourceUsersCount}, sourceCheckIns={SourceCheckInsCount}, workersRead={WorkersReadCount}",
            context.CorrelationId,
            productionDate,
            context.TriggerType,
            sourceUsersCount,
            sourceCheckInsCount,
            workers.Count);

        // Identity resolution intentionally includes inactive workers. A resolved inactive worker is a
        // business skip, not an unresolvable identity or an attendance record candidate. Daily
        // attendance summaries continue to be built only for active employees.
        var mappedWorkers = workers
            .Where(x => x.IsActive &&
                        x.EmploymentStatus == EmploymentStatus.Active &&
                        (!x.EmploymentEndDate.HasValue || DateOnly.FromDateTime(x.EmploymentEndDate.Value) >= productionDate))
            .ToArray();

        if (workers.Count == 0)
        {
            _logger.LogWarning(
                "Attendance sync completed with no mapped workers. sourceUsers={SourceUsersCount}, sourceCheckIns={SourceCheckInsCount}, date={SyncDate}",
                sourceUsersCount,
                sourceCheckInsCount,
                productionDate);

            var emptyUnmatchedSourceUsersCount = sourceCheckIns
                .Select(x => NormalizeSourceIdentity(x.UserId))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var emptyBatchAcknowledgement = await _attendanceSource.CompleteAsync(
                sourceBatch,
                sourceCheckIns
                    .Where(punch => punch.SourceRecordId.HasValue)
                    .Select(punch => SourceProcessingOutcome.Retry(
                        punch.SourceRecordId!.Value,
                        "WorkerIdentityNotResolved"))
                    .ToArray(),
                cancellationToken);
            if (emptyBatchAcknowledgement.IsFailure)
            {
                await RetryAttendanceBatchAsync(sourceBatch, "AttendanceAcknowledgementFailed");
                return Result<AttendanceSyncResultDto>.Failure(emptyBatchAcknowledgement.Error!);
            }

            return Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto
            {
                CorrelationId = context.CorrelationId,
                TriggerType = context.TriggerType,
                SyncDateUtc = startUtc,
                SourceUsersCount = sourceUsersCount,
                SourceCheckInsCount = sourceCheckInsCount,
                MatchedWorkersCount = 0,
                UnmatchedSourceUsersCount = emptyUnmatchedSourceUsersCount,
                WorkersWithoutAttendanceCount = 0,
                InsertedRecords = 0,
                UpdatedRecords = 0,
                SkippedRecords = 0
            });
        }

        var attendanceUserMap = BuildIdentityLookup(workers, x => x.AttendanceUserId);
        var badgeMap = BuildIdentityLookup(workers, x => x.BadgeNumber);
        var workersById = workers.ToDictionary(worker => worker.Id);
        var badgeBySourceUserId = sourceCheckIns
            .Where(x => x.UserId is not null)
            .GroupBy(x => NormalizeIdentity(x.UserId!.ToString()))
            .Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!, g => NormalizeIdentity(g.First().BadgeNumber), StringComparer.OrdinalIgnoreCase);

        var validCheckIns = sourceCheckIns
            .Where(x => ValidateSourcePunch(x) is null)
            .Select(x => new
            {
                WorkerUserId = NormalizeSourceIdentity(x.UserId)!,
                x.CheckTimeLocal,
                CheckTimeUtc = ToUtcFromEgyptSourceTime(x.CheckTimeLocal),
                RawSourceIdentifier = x.SourceRawId,
                x.SourceRecordId
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.WorkerUserId))
            .ToList();

        var matchedByWorker = new Dictionary<Guid, (DateTime FirstIn, DateTime LastOut, string SourceRawId)>();
        var unmatchedSourceUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processingOutcomes = sourceCheckIns
            .Where(punch => punch.SourceRecordId.HasValue)
            .Select(punch => (Punch: punch, Error: ValidateSourcePunch(punch)))
            .Where(item => item.Error is not null)
            .ToDictionary(
                item => item.Punch.SourceRecordId!.Value,
                item => SourceProcessingOutcome.Failed(item.Punch.SourceRecordId!.Value, item.Error!));

        foreach (var item in validCheckIns.OrderBy(x => x.CheckTimeUtc))
        {
            var sourceUserId = item.WorkerUserId;
            var identityResolution = TryResolveWorkerId(
                sourceUserId,
                attendanceUserMap,
                badgeMap,
                badgeBySourceUserId,
                out var workerId);
            if (identityResolution != WorkerIdentityResolution.Resolved)
            {
                unmatchedSourceUsers.Add(sourceUserId);
                if (item.SourceRecordId.HasValue)
                {
                    processingOutcomes[item.SourceRecordId.Value] = identityResolution == WorkerIdentityResolution.Ambiguous
                        ? SourceProcessingOutcome.Failed(item.SourceRecordId.Value, "WorkerIdentityAmbiguous")
                        : SourceProcessingOutcome.Retry(item.SourceRecordId.Value, "WorkerIdentityNotResolved");
                }
                continue;
            }

            var worker = workersById[workerId];
            if (!worker.IsActive || worker.EmploymentStatus != EmploymentStatus.Active)
            {
                if (item.SourceRecordId.HasValue)
                {
                    processingOutcomes[item.SourceRecordId.Value] = SourceProcessingOutcome.Skipped(
                        item.SourceRecordId.Value,
                        "WorkerInactive");
                }
                continue;
            }

            if (worker.EmploymentEndDate.HasValue &&
                DateOnly.FromDateTime(item.CheckTimeLocal) > DateOnly.FromDateTime(worker.EmploymentEndDate.Value))
            {
                if (item.SourceRecordId.HasValue)
                {
                    processingOutcomes[item.SourceRecordId.Value] = SourceProcessingOutcome.Skipped(
                        item.SourceRecordId.Value,
                        "AttendanceAfterEmploymentEnd");
                }
                continue;
            }

            if (item.SourceRecordId.HasValue)
            {
                processingOutcomes[item.SourceRecordId.Value] = SourceProcessingOutcome.Processed(item.SourceRecordId.Value);
            }

            if (!matchedByWorker.TryGetValue(workerId, out var window))
                matchedByWorker[workerId] = (item.CheckTimeUtc, item.CheckTimeUtc, item.RawSourceIdentifier);
            else if (item.CheckTimeUtc > window.LastOut)
                matchedByWorker[workerId] = (window.FirstIn, item.CheckTimeUtc, window.SourceRawId);
        }

        var matchedWorkersCount = matchedByWorker.Count;
        var unmatchedSourceUsersCount = unmatchedSourceUsers.Count;

        var workerIds = mappedWorkers.Select(x => x.Id).ToArray();
        var existingRecords = await _appDbContext.AttendanceRecords
            .Where(x => workerIds.Contains(x.WorkerId) && x.AttendanceTimeUtc >= startUtc && x.AttendanceTimeUtc < endUtc)
            .ToListAsync(cancellationToken);

        var existingByWorker = existingRecords
            .GroupBy(x => x.WorkerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.AttendanceTimeUtc).First());

        var insertCount = 0;
        var updateCount = 0;
        var unchangedCount = 0;
        var syncRunAt = DateTime.UtcNow;

        foreach (var worker in mappedWorkers)
        {
            var statusTime = endUtc.AddTicks(-1);
            var status = AttendanceStatus.Absent;
            var sourceRawId = SyncAbsentStatus;
            string? sourcePayload = null;

            if (matchedByWorker.TryGetValue(worker.Id, out var match))
            {
                statusTime = match.FirstIn;
                status = CalculateStatus(match.FirstIn, startLocal);
                sourceRawId = match.SourceRawId;
                sourcePayload = JsonSerializer.Serialize(new
                {
                    FirstInUtc = match.FirstIn,
                    LastOutUtc = match.LastOut > match.FirstIn ? match.LastOut : (DateTime?)null
                });
            }

            if (!existingByWorker.TryGetValue(worker.Id, out var existing))
            {
                var record = new AttendanceRecord(
                    id: Guid.NewGuid(),
                    workerId: worker.Id,
                    attendanceTimeUtc: statusTime,
                    attendanceStatus: status,
                    source: _sourceOptions.SourceName,
                    sourcePayload: sourcePayload,
                    sourceRawId: sourceRawId,
                    attendanceUserId: worker.AttendanceUserId,
                    badgeNumber: worker.BadgeNumber,
                    createdAtUtc: syncRunAt);

                _appDbContext.AttendanceRecords.Add(record);
                insertCount++;
                continue;
            }

            if (existing.AttendanceStatus == status && existing.AttendanceTimeUtc == statusTime && existing.SourceRawId == sourceRawId && existing.SourcePayload == sourcePayload)
            {
                unchangedCount++;
                continue;
            }

            existing.UpdateAttendanceStatus(
                statusTime,
                status,
                _sourceOptions.SourceName,
                sourcePayload,
                sourceRawId,
                worker.AttendanceUserId,
                worker.BadgeNumber,
                syncRunAt);
            updateCount++;
        }

        await _appDbContext.SaveChangesAsync(cancellationToken);

        var claimedInboxIds = sourceCheckIns
            .Where(punch => punch.SourceRecordId.HasValue)
            .Select(punch => punch.SourceRecordId!.Value)
            .ToHashSet();
        var unresolvedClaimedInboxIds = claimedInboxIds
            .Except(processingOutcomes.Keys)
            .ToArray();
        foreach (var inboxId in unresolvedClaimedInboxIds)
        {
            processingOutcomes[inboxId] = SourceProcessingOutcome.Failed(inboxId, "AttendanceResolutionMissing");
        }
        if (unresolvedClaimedInboxIds.Length > 0)
        {
            _logger.LogError(
                "Attendance staging resolution did not produce an explicit outcome for every claimed row. unresolvedClaimedCount={UnresolvedClaimedCount}",
                unresolvedClaimedInboxIds.Length);
        }

        var acknowledgement = await _attendanceSource.CompleteAsync(
            sourceBatch,
            processingOutcomes.Values.ToArray(),
            cancellationToken);
        if (acknowledgement.IsFailure)
        {
            await RetryAttendanceBatchAsync(sourceBatch, "AttendanceAcknowledgementFailed");
            return Result<AttendanceSyncResultDto>.Failure(acknowledgement.Error!);
        }

        var workersWithoutAttendanceCount = mappedWorkers.Count(worker => !matchedByWorker.ContainsKey(worker.Id));

        if (insertCount == 0)
        {
            if (sourceCheckInsCount == 0)
            {
                _logger.LogWarning(
                    "Attendance sync returned zero inserted records because no valid source check-ins were found. date={SyncDate}, sourceUsers={SourceUsersCount}, sourceCheckIns={SourceCheckInsCount}",
                    productionDate,
                    sourceUsersCount,
                    sourceCheckInsCount);
            }
            else if (matchedWorkersCount == 0)
            {
                _logger.LogWarning(
                    "Attendance sync returned zero inserted records because no source users could be matched to active workers. date={SyncDate}, unmatchedSourceUsers={UnmatchedSourceUsersCount}",
                    productionDate,
                    unmatchedSourceUsersCount);
            }
            else if (updateCount > 0)
            {
                _logger.LogInformation(
                    "Attendance sync returned zero inserted records because matched workers were updated rather than inserted. date={SyncDate}, updatedRecords={UpdatedRecords}, skippedRecords={SkippedRecords}",
                    productionDate,
                    updateCount,
                    unchangedCount);
            }
            else if (sourceCheckInsCount > 0 && updateCount == 0 && unchangedCount > 0)
            {
                _logger.LogInformation(
                    "Attendance sync returned zero inserted records because all matched source users resolved only to unchanged existing attendance rows. date={SyncDate}, skippedRecords={SkippedRecords}, workersWithoutAttendance={WorkersWithoutAttendanceCount}",
                    productionDate,
                    unchangedCount,
                    workersWithoutAttendanceCount);
            }
        }

        _logger.LogInformation(
            "Attendance sync completed. sourceUsers={SourceUsersCount}, sourceCheckIns={SourceCheckInsCount}, matchedWorkers={MatchedWorkersCount}, unmatchedSourceUsers={UnmatchedSourceUsersCount}, workersWithoutAttendance={WorkersWithoutAttendanceCount}, inserted={InsertedRecords}, updated={UpdatedRecords}, skipped={SkippedRecords}, date={SyncDate}",
            sourceUsersCount,
            sourceCheckInsCount,
            matchedWorkersCount,
            unmatchedSourceUsersCount,
            workersWithoutAttendanceCount,
            insertCount,
            updateCount,
            unchangedCount,
            productionDate);

        return Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto
        {
            CorrelationId = context.CorrelationId,
            TriggerType = context.TriggerType,
            SyncDateUtc = startUtc,
            SourceUsersCount = sourceUsersCount,
            SourceCheckInsCount = sourceCheckInsCount,
            MatchedWorkersCount = matchedWorkersCount,
            UnmatchedSourceUsersCount = unmatchedSourceUsersCount,
            WorkersWithoutAttendanceCount = workersWithoutAttendanceCount,
            InsertedRecords = insertCount,
            UpdatedRecords = updateCount,
            SkippedRecords = unchangedCount
        });
        }
        catch (OperationCanceledException)
        {
            await RetryAttendanceBatchAsync(sourceBatch, "AttendanceProcessingCancelled");
            throw;
        }
        catch (Exception exception)
        {
            await RetryAttendanceBatchAsync(sourceBatch, "AttendanceProcessingFailed");
            _logger.LogError(
                exception,
                "Attendance staging processing failed after rows were claimed. claimedCount={ClaimedCount}, leaseId={LeaseId}",
                sourceCheckIns.Count(punch => punch.SourceRecordId.HasValue),
                sourceBatch.LeaseId);
            return Result<AttendanceSyncResultDto>.Failure(new Error(
                "AttendanceProcessingFailed",
                "Attendance staging rows were released for retry after processing failed."));
        }
    }

    private async Task RetryAttendanceBatchAsync(AttendanceSourceBatch batch, string errorCode)
    {
        var outcomes = batch.Punches
            .Where(punch => punch.SourceRecordId.HasValue)
            .Select(punch => SourceProcessingOutcome.Retry(punch.SourceRecordId!.Value, errorCode))
            .ToArray();
        var result = await _attendanceSource.CompleteAsync(batch, outcomes, CancellationToken.None);
        if (result.IsFailure)
        {
            _logger.LogWarning("Attendance staging rows could not be released for retry after processing failed.");
        }
    }

    private static string? NormalizeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? NormalizeSourceIdentity(int? value)
    {
        return NormalizeIdentity(value?.ToString());
    }

    private DateTime GetDateOnly(DateTime? dateUtc)
    {
        var utc = dateUtc ?? DateTime.UtcNow;
        var cairo = TimeZoneInfo.ConvertTimeFromUtc(utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime(), _cairoTimeZoneProvider.TimeZone);
        return GetEgyptDayBounds(DateOnly.FromDateTime(cairo)).StartUtc;
    }

    private static Dictionary<string, List<Guid>> BuildIdentityLookup(IEnumerable<Worker> workers, Func<Worker, string?> selector)
    {
        var result = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var worker in workers)
        {
            var key = NormalizeIdentity(selector(worker));
            if (key is null)
            {
                continue;
            }

            if (!result.TryGetValue(key, out var ids))
            {
                ids = [];
                result[key] = ids;
            }

            ids.Add(worker.Id);
        }

        return result;
    }

    private static WorkerIdentityResolution TryResolveWorkerId(
        string sourceUserId,
        Dictionary<string, List<Guid>> attendanceUserLookup,
        Dictionary<string, List<Guid>> badgeLookup,
        Dictionary<string, string?> sourceBadgeByUserId,
        out Guid workerId)
    {
        workerId = Guid.Empty;

        attendanceUserLookup.TryGetValue(sourceUserId, out var byAttendanceUserId);
        var sourceBadge = sourceBadgeByUserId.TryGetValue(sourceUserId, out var badge)
            ? badge
            : null;
        var byBadge = !string.IsNullOrWhiteSpace(sourceBadge) && badgeLookup.TryGetValue(sourceBadge, out var badgeMatches)
            ? badgeMatches
            : null;

        if (byAttendanceUserId?.Count > 1 || byBadge?.Count > 1)
        {
            return WorkerIdentityResolution.Ambiguous;
        }

        if (byAttendanceUserId?.Count == 1)
        {
            if (byBadge?.Count == 1 && byBadge[0] != byAttendanceUserId[0])
            {
                return WorkerIdentityResolution.Ambiguous;
            }

            workerId = byAttendanceUserId[0];
            return WorkerIdentityResolution.Resolved;
        }

        if (byBadge?.Count == 1)
        {
            workerId = byBadge[0];
            return WorkerIdentityResolution.Resolved;
        }

        return WorkerIdentityResolution.NotResolved;
    }

    private static string? ValidateSourcePunch(AttendanceSourcePunch punch)
    {
        if (punch.UserId is null)
        {
            return "MissingSourceUserId";
        }

        if (punch.CheckTimeLocal == default)
        {
            return "MissingCheckTime";
        }

        if (string.IsNullOrWhiteSpace(punch.CheckType))
        {
            return "InvalidCheckType";
        }

        return string.IsNullOrWhiteSpace(punch.SourceRawId) ? "InvalidSourcePayload" : null;
    }

    private enum WorkerIdentityResolution
    {
        NotResolved,
        Resolved,
        Ambiguous
    }

    private AttendanceStatus CalculateStatus(DateTime checkTimeUtc, DateTime productionStartLocal)
    {
        var shiftStart = productionStartLocal.Add(_sourceOptions.DayStartTime);
        var lateThreshold = shiftStart.AddMinutes(_sourceOptions.LateThresholdMinutes);
        var localCheckTime = TimeZoneInfo.ConvertTimeFromUtc(checkTimeUtc, _cairoTimeZoneProvider.TimeZone);
        return localCheckTime <= lateThreshold ? AttendanceStatus.Present : AttendanceStatus.Late;
    }

    private async Task<Dictionary<Guid, AttendanceRecord>> GetLatestAttendanceByWorkerForDateAsync(
        IEnumerable<Guid> workerIds,
        DateTime forDate,
        CancellationToken cancellationToken)
    {
        var workerIdArray = workerIds.Distinct().ToArray();
        if (workerIdArray.Length == 0)
        {
            return new Dictionary<Guid, AttendanceRecord>();
        }

        var localDate = TimeZoneInfo.ConvertTimeFromUtc(forDate, _cairoTimeZoneProvider.TimeZone);
        var endUtc = GetEgyptDayBounds(DateOnly.FromDateTime(localDate)).EndUtc;
        var records = await _appDbContext.AttendanceRecords
            .AsNoTracking()
            .Where(x => workerIdArray.Contains(x.WorkerId) && x.AttendanceTimeUtc >= forDate && x.AttendanceTimeUtc < endUtc)
            .OrderBy(x => x.WorkerId)
            .ThenByDescending(x => x.AttendanceTimeUtc)
            .ToListAsync(cancellationToken);

        return records
            .GroupBy(x => x.WorkerId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    private (DateTime StartUtc, DateTime EndUtc, DateTime StartLocal, DateTime EndLocal) GetEgyptDayBounds(DateOnly date)
    {
        var startLocal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocal = startLocal.AddDays(1);
        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, _cairoTimeZoneProvider.TimeZone),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, _cairoTimeZoneProvider.TimeZone),
            startLocal,
            endLocal);
    }

    private DateTime ToUtcFromEgyptSourceTime(DateTime sourceTime)
    {
        if (sourceTime.Kind == DateTimeKind.Utc) return sourceTime;
        var local = DateTime.SpecifyKind(sourceTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, _cairoTimeZoneProvider.TimeZone);
    }

    private async Task<HashSet<Guid>> GetVisibleWorkerIdsForScopeAsync(
        DateTime dateUtc,
        Guid? factoryId,
        Guid? lineId,
        CancellationToken cancellationToken)
    {
        var allActiveWorkerIds = await _appDbContext.Workers
            .AsNoTracking()
            .Where(x => x.IsActive && x.EmploymentStatus == EmploymentStatus.Active)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (factoryId is null && lineId is null)
        {
            return allActiveWorkerIds.ToHashSet();
        }

        var scopeSubStageIds = await GetSubStageIdsInScopeAsync(factoryId, lineId, cancellationToken);
        if (scopeSubStageIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var assignments = await ResolveCurrentAssignmentsAsync(allActiveWorkerIds, dateUtc, cancellationToken);

        return assignments
            .Where(x => x.Value.EffectiveSubStageId is not null && scopeSubStageIds.Contains(x.Value.EffectiveSubStageId.Value))
            .Select(x => x.Key)
            .ToHashSet();
    }

    private async Task<HashSet<Guid>> GetSubStageIdsInScopeAsync(Guid? factoryId, Guid? lineId, CancellationToken cancellationToken)
    {
        var subStageIds = await (from subStage in _appDbContext.SubStages.AsNoTracking()
                                join main in _appDbContext.MainStages.AsNoTracking() on subStage.MainStageId equals main.Id
                                join line in _appDbContext.ProductionLines.AsNoTracking() on main.ProductionLineId equals line.Id
                                where subStage.IsActive && main.IsActive && line.IsActive
                                select new
                                {
                                    subStage.Id,
                                    lineId = line.Id,
                                    factoryId = (Guid?)line.FactoryId
                                })
            .Where(x => (!lineId.HasValue || x.lineId == lineId.Value) && (!factoryId.HasValue || x.factoryId == factoryId.Value))
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        return subStageIds.ToHashSet();
    }

    private async Task<Dictionary<Guid, AssignmentState>> ResolveCurrentAssignmentsAsync(
        IEnumerable<Guid> workerIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var uniqueWorkerIds = workerIds.Distinct().ToArray();
        if (uniqueWorkerIds.Length == 0)
        {
            return new Dictionary<Guid, AssignmentState>();
        }

        var defaultAssignments = await _appDbContext.WorkerDefaultAssignments
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

        var activeTemporaryAssignments = await _appDbContext.WorkerTemporaryAssignments
            .AsNoTracking()
            .Where(x => uniqueWorkerIds.Contains(x.WorkerId)
                        && x.StartAtUtc <= asOfUtc
                        && x.EndAtUtc > asOfUtc
                        && (x.Status == TempStatusActive || x.Status == TempStatusScheduled))
            .Select(x => new
            {
                x.WorkerId,
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
                    .ThenByDescending(x => x.EndAtUtc)
                    .First());

        var results = new Dictionary<Guid, AssignmentState>();
        foreach (var workerId in uniqueWorkerIds)
        {
            if (temporaryByWorker.TryGetValue(workerId, out var tempAssignment))
            {
                results[workerId] = new AssignmentState(
                    workerId,
                    AssignmentType.Temporary,
                    tempAssignment.StartAtUtc,
                    tempAssignment.EndAtUtc,
                    tempAssignment.ToSubStageId,
                    tempAssignment.FromSubStageId,
                    tempAssignment.ToSubStageId,
                    tempAssignment.ReplacementForWorkerId);
                continue;
            }

            if (currentDefaultsByWorker.TryGetValue(workerId, out var defaultAssignment))
            {
                results[workerId] = new AssignmentState(
                    workerId,
                    AssignmentType.Default,
                    defaultAssignment.AssignedAt,
                    null,
                    defaultAssignment.SubStageId,
                    null,
                    null,
                    null);
                continue;
            }

            results[workerId] = new AssignmentState(
                workerId,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        return results;
    }

    private sealed record AssignmentState(
        Guid WorkerId,
        AssignmentType? AssignmentType,
        DateTime? StartsAtUtc,
        DateTime? EndsAtUtc,
        Guid? EffectiveSubStageId,
        Guid? FromSubStageId,
        Guid? ToSubStageId,
        Guid? ReplacementForWorkerId);
}
