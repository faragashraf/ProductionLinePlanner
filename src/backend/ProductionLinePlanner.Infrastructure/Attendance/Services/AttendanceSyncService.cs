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
    private readonly IAttendanceWorkdayPolicy _attendanceWorkdayPolicy;
    private readonly IWorkerInitialSyncService _workerSyncService;
    private readonly IManufacturingRealtimeChangeContext? _realtimeChangeContext;

    public AttendanceSyncService(
        AppDbContext appDbContext,
        IAttendanceSource attendanceSource,
        IOptions<AttendanceSourceOptions> sourceOptions,
        ILogger<AttendanceSyncService> logger,
        ICairoTimeZoneProvider cairoTimeZoneProvider,
        IWorkerInitialSyncService workerSyncService,
        IManufacturingRealtimeChangeContext? realtimeChangeContext = null,
        IAttendanceWorkdayPolicy? attendanceWorkdayPolicy = null)
    {
        _appDbContext = appDbContext;
        _attendanceSource = attendanceSource;
        _sourceOptions = sourceOptions.Value;
        _logger = logger;
        _cairoTimeZoneProvider = cairoTimeZoneProvider;
        _workerSyncService = workerSyncService;
        _realtimeChangeContext = realtimeChangeContext;
        _attendanceWorkdayPolicy = attendanceWorkdayPolicy ?? new AttendanceWorkdayPolicy(sourceOptions, cairoTimeZoneProvider);
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
        return SyncForProductionDateAsync(_attendanceWorkdayPolicy.GetOperationalDate(DateTime.UtcNow), cancellationToken);
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
            return await CompleteRunAsync(context, result);
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
            return await CompleteRunAsync(context, Result<AttendanceSyncResultDto>.Failure(new Error(AttendanceSyncFailureClassifier.ClientCancelled, "Attendance synchronization request was cancelled by the client.")));
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
            return await CompleteRunAsync(context, Result<AttendanceSyncResultDto>.Failure(new Error(AttendanceSyncFailureClassifier.InternalTimeout, "Attendance synchronization exceeded its bounded source-read timeout.")));
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
            return await CompleteRunAsync(context, Result<AttendanceSyncResultDto>.Failure(new Error(AttendanceSyncFailureClassifier.Cancelled, "Attendance synchronization was cancelled before completion.")));
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
            return await CompleteRunAsync(context, Result<AttendanceSyncResultDto>.Failure(new Error(AttendanceSyncFailureClassifier.SourceTimeout, "Attendance source query timed out.")));
        }
    }

    private async Task<Result<AttendanceSyncResultDto>> CompleteRunAsync(
        AttendanceSyncExecutionContext context,
        Result<AttendanceSyncResultDto> result)
    {
        try
        {
            var state = await _appDbContext.AttendanceSyncStates
                .SingleOrDefaultAsync(item => item.SourceName == _sourceOptions.SourceName
                    && item.OperationalDate == context.ProductionDate, CancellationToken.None);
            state ??= new AttendanceSyncState(Guid.NewGuid(), _sourceOptions.SourceName, context.ProductionDate);
            if (_appDbContext.Entry(state).State == EntityState.Detached)
            {
                _appDbContext.AttendanceSyncStates.Add(state);
            }

            if (result.IsSuccess) state.RecordSuccess(DateTime.UtcNow);
            else state.RecordFailure(DateTime.UtcNow, result.Error?.Code);
            await _appDbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Attendance rows may already be committed. Freshness remains
            // untrusted rather than turning a completed source synchronization
            // into a false application failure.
            _logger.LogError(
                exception,
                "Attendance sync freshness state could not be persisted. correlationId={CorrelationId}, date={SyncDate}",
                context.CorrelationId,
                context.ProductionDate);
        }

        return result;
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

        var identityResolver = new AttendanceWorkerIdentityResolver(workers);
        var workersById = workers.ToDictionary(worker => worker.Id);

        var validCheckIns = sourceCheckIns
            .Where(x => ValidateSourcePunch(x) is null)
            .Select(x => new
            {
                WorkerUserId = NormalizeSourceIdentity(x.UserId)!,
                x.CheckTimeLocal,
                CheckTimeUtc = ToUtcFromEgyptSourceTime(x.CheckTimeLocal),
                CheckType = GetPunchType(x.CheckType)!.Value,
                RawSourceIdentifier = x.SourceRawId,
                BadgeNumber = NormalizeIdentity(x.BadgeNumber),
                x.SourceRecordId
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.WorkerUserId))
            .ToList();

        var matchedByWorker = new Dictionary<Guid, AttendanceWindow>();
        var resolvedPunches = new List<ResolvedPunch>();
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
            var identityResolution = identityResolver.Resolve(
                sourceUserId,
                item.BadgeNumber,
                out var workerId);
            if (identityResolution != AttendanceWorkerIdentityResolution.Resolved)
            {
                unmatchedSourceUsers.Add(sourceUserId);
                if (item.SourceRecordId.HasValue)
                {
                    processingOutcomes[item.SourceRecordId.Value] = identityResolution == AttendanceWorkerIdentityResolution.Ambiguous
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

            resolvedPunches.Add(new ResolvedPunch(
                item.SourceRecordId,
                workerId,
                item.CheckTimeUtc,
                item.CheckType,
                item.RawSourceIdentifier));

            if (item.CheckType == PunchType.In)
            {
                if (!matchedByWorker.TryGetValue(workerId, out var window))
                {
                    matchedByWorker[workerId] = new AttendanceWindow(item.CheckTimeUtc, null, item.RawSourceIdentifier);
                }
                else if (item.CheckTimeUtc < window.FirstInUtc)
                {
                    matchedByWorker[workerId] = window with
                    {
                        FirstInUtc = item.CheckTimeUtc,
                        SourceRawId = item.RawSourceIdentifier,
                        LastOutUtc = window.LastOutUtc is { } lastOut && lastOut > item.CheckTimeUtc ? lastOut : null
                    };
                }
            }
            else if (matchedByWorker.TryGetValue(workerId, out var window) && item.CheckTimeUtc > window.FirstInUtc)
            {
                if (!window.LastOutUtc.HasValue || item.CheckTimeUtc > window.LastOutUtc.Value)
                {
                    matchedByWorker[workerId] = window with { LastOutUtc = item.CheckTimeUtc };
                }
            }
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

        // Preserve a persisted check-in fallback if a staging provider supplies only the newly
        // claimed checkout rather than full-day context. This still refuses an out-only punch
        // when no committed check-in exists.
        foreach (var checkoutGroup in resolvedPunches
                     .Where(punch => punch.CheckType == PunchType.Out && !matchedByWorker.ContainsKey(punch.WorkerId))
                     .GroupBy(punch => punch.WorkerId))
        {
            if (!existingByWorker.TryGetValue(checkoutGroup.Key, out var existingRecord)) continue;
            var existingWindow = AttendancePunchEvidenceMatcher.ReadWindow(existingRecord.SourcePayload);
            if (existingWindow.FirstInUtc is not { } firstInUtc) continue;
            var lastOutUtc = checkoutGroup
                .Select(punch => punch.CheckTimeUtc)
                .Where(checkTimeUtc => checkTimeUtc > firstInUtc)
                .Cast<DateTime?>()
                .Max();
            if (lastOutUtc is null) continue;
            matchedByWorker[checkoutGroup.Key] = new AttendanceWindow(
                firstInUtc,
                lastOutUtc,
                existingRecord.SourceRawId ?? SyncAbsentStatus);
        }

        var alreadyImportedInboxIds = resolvedPunches
            .Where(punch => punch.SourceRecordId.HasValue
                && existingByWorker.TryGetValue(punch.WorkerId, out var record)
                && IsExactPunchEvidence(record, punch))
            .Select(punch => punch.SourceRecordId!.Value)
            .ToHashSet();
        var existingRecordIds = existingRecords.Select(record => record.Id).ToArray();
        var queuedNotificationKeys = existingRecordIds.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _appDbContext.AttendanceNotificationEvents
                .AsNoTracking()
                .Where(item => existingRecordIds.Contains(item.AttendanceRecordId))
                .Select(item => item.IdempotencyKey)
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

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
                statusTime = match.FirstInUtc;
                status = CalculateStatus(match.FirstInUtc, productionDate);
                sourceRawId = match.SourceRawId;
                sourcePayload = JsonSerializer.Serialize(new
                {
                    FirstInUtc = match.FirstInUtc,
                    LastOutUtc = match.LastOutUtc
                });
            }

            if (!existingByWorker.TryGetValue(worker.Id, out var existing))
            {
                var attendanceRecordId = Guid.NewGuid();
                var record = new AttendanceRecord(
                    id: attendanceRecordId,
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
                existingByWorker[worker.Id] = record;
                if (matchedByWorker.TryGetValue(worker.Id, out var insertedMatch))
                {
                    QueueAttendanceNotification(record, worker, WorkerAttendanceNotificationType.CheckIn, insertedMatch.FirstInUtc, queuedNotificationKeys);
                    if (insertedMatch.LastOutUtc.HasValue)
                    {
                        QueueAttendanceNotification(record, worker, WorkerAttendanceNotificationType.CheckOut, insertedMatch.LastOutUtc.Value, queuedNotificationKeys);
                    }
                }
                insertCount++;
                continue;
            }

            if (existing.AttendanceStatus == status && existing.AttendanceTimeUtc == statusTime && existing.SourceRawId == sourceRawId && existing.SourcePayload == sourcePayload)
            {
                unchangedCount++;
                continue;
            }

            var previousWindow = AttendancePunchEvidenceMatcher.ReadWindow(existing.SourcePayload);
            existing.UpdateAttendanceStatus(
                statusTime,
                status,
                _sourceOptions.SourceName,
                sourcePayload,
                sourceRawId,
                worker.AttendanceUserId,
                worker.BadgeNumber,
                syncRunAt);
            if (matchedByWorker.TryGetValue(worker.Id, out var updatedMatch))
            {
                if (previousWindow.FirstInUtc is null)
                {
                    QueueAttendanceNotification(existing, worker, WorkerAttendanceNotificationType.CheckIn, updatedMatch.FirstInUtc, queuedNotificationKeys);
                }
                if (updatedMatch.LastOutUtc.HasValue && previousWindow.LastOutUtc is null)
                {
                    QueueAttendanceNotification(existing, worker, WorkerAttendanceNotificationType.CheckOut, updatedMatch.LastOutUtc.Value, queuedNotificationKeys);
                }
            }
            updateCount++;
        }

        await _appDbContext.SaveChangesAsync(cancellationToken);

        foreach (var punch in resolvedPunches.Where(item => item.SourceRecordId.HasValue))
        {
            var inboxId = punch.SourceRecordId!.Value;
            if (existingByWorker.TryGetValue(punch.WorkerId, out var persistedRecord) &&
                IsExactPunchEvidence(persistedRecord, punch))
            {
                var alreadyImported = alreadyImportedInboxIds.Contains(inboxId);
                processingOutcomes[inboxId] = SourceProcessingOutcome.Processed(
                    inboxId,
                    alreadyImported ? "AlreadyImported" : "Imported",
                    alreadyImported
                        ? $"Exact {punch.CheckType} evidence already exists in AttendanceRecord {persistedRecord.Id:D}."
                        : $"Exact {punch.CheckType} evidence was committed in AttendanceRecord {persistedRecord.Id:D}.");
                continue;
            }

            if (!matchedByWorker.TryGetValue(punch.WorkerId, out var canonicalWindow))
            {
                processingOutcomes[inboxId] = SourceProcessingOutcome.Retry(
                    inboxId,
                    "CheckInRequired",
                    "The checkout cannot be applied until a valid check-in exists in the operational-day window.");
                continue;
            }

            if (punch.CheckType == PunchType.In && punch.CheckTimeUtc != canonicalWindow.FirstInUtc)
            {
                processingOutcomes[inboxId] = SourceProcessingOutcome.Skipped(
                    inboxId,
                    "NonCanonicalCheckIn",
                    $"The daily summary preserves the earlier check-in at {canonicalWindow.FirstInUtc:O}.");
                continue;
            }

            if (punch.CheckType == PunchType.Out && punch.CheckTimeUtc != canonicalWindow.LastOutUtc)
            {
                processingOutcomes[inboxId] = SourceProcessingOutcome.Skipped(
                    inboxId,
                    "NonCanonicalCheckOut",
                    canonicalWindow.LastOutUtc.HasValue
                        ? $"The daily summary preserves the later checkout at {canonicalWindow.LastOutUtc.Value:O}."
                        : "No checkout is represented by the persisted daily summary.");
                continue;
            }

            processingOutcomes[inboxId] = SourceProcessingOutcome.Failed(
                inboxId,
                "AttendancePersistenceNotProven",
                "The processor could not prove exact persisted attendance evidence for this source punch.");
        }

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
            var errorDetails = GetProcessingErrorDetails(exception);
            foreach (var punch in sourceCheckIns.Where(item => item.SourceRecordId.HasValue))
            {
                var matchingWorkers = workers
                    .Where(worker =>
                        string.Equals(NormalizeIdentity(worker.AttendanceUserId), NormalizeSourceIdentity(punch.UserId), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(NormalizeIdentity(worker.BadgeNumber), NormalizeIdentity(punch.BadgeNumber), StringComparison.OrdinalIgnoreCase))
                    .Select(worker => worker.Id)
                    .Distinct()
                    .Take(2)
                    .ToArray();
                var workerId = matchingWorkers.Length == 1 ? matchingWorkers[0] : (Guid?)null;

                _logger.LogError(
                    exception,
                    "Attendance staging row processing failed. inboxId={InboxId}, sourceUserId={SourceUserId}, badgeNumber={BadgeNumber}, sourceCheckTimeLocal={SourceCheckTimeLocal}, sourceCheckType={SourceCheckType}, workerId={WorkerId}",
                    punch.SourceRecordId,
                    punch.UserId,
                    punch.BadgeNumber,
                    punch.CheckTimeLocal,
                    punch.CheckType,
                    workerId);
            }

            await RetryAttendanceBatchAsync(sourceBatch, "AttendanceProcessingFailed", errorDetails);
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

    private async Task RetryAttendanceBatchAsync(AttendanceSourceBatch batch, string errorCode, string? errorDetails = null)
    {
        var outcomes = batch.Punches
            .Where(punch => punch.SourceRecordId.HasValue)
            .Select(punch => SourceProcessingOutcome.Retry(punch.SourceRecordId!.Value, errorCode, errorDetails))
            .ToArray();
        var result = await _attendanceSource.CompleteAsync(batch, outcomes, CancellationToken.None);
        if (result.IsFailure)
        {
            _logger.LogWarning("Attendance staging rows could not be released for retry after processing failed.");
        }
    }

    private static string GetProcessingErrorDetails(Exception exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        message = message.Trim();
        return message.Length <= 1000 ? message : message[..1000];
    }

    private void QueueAttendanceNotification(
        AttendanceRecord attendanceRecord,
        Worker worker,
        WorkerAttendanceNotificationType attendanceType,
        DateTime attendanceTimeUtc,
        ISet<string> queuedNotificationKeys)
    {
        var idempotencyKey = $"attendance:{attendanceRecord.Id:D}:{attendanceType}";
        if (!queuedNotificationKeys.Add(idempotencyKey)) return;

        _appDbContext.AttendanceNotificationEvents.Add(new AttendanceNotificationEvent(
            Guid.NewGuid(),
            attendanceRecord.Id,
            worker.Id,
            worker.FullName,
            worker.EmployeeCode ?? worker.BadgeNumber ?? worker.AttendanceUserId ?? worker.Id.ToString("D"),
            attendanceType,
            DateTime.SpecifyKind(attendanceTimeUtc, DateTimeKind.Utc),
            _sourceOptions.SourceName,
            idempotencyKey));
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
        var operationalDate = _attendanceWorkdayPolicy.GetOperationalDate(
            utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime());
        return _attendanceWorkdayPolicy.GetWindow(operationalDate).StartUtc;
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

        if (GetPunchType(punch.CheckType) is null)
        {
            return "UnsupportedCheckType";
        }

        return string.IsNullOrWhiteSpace(punch.SourceRawId) ? "InvalidSourcePayload" : null;
    }

    private enum PunchType
    {
        In,
        Out
    }

    private sealed record AttendanceWindow(DateTime FirstInUtc, DateTime? LastOutUtc, string SourceRawId);

    private sealed record ResolvedPunch(
        long? SourceRecordId,
        Guid WorkerId,
        DateTime CheckTimeUtc,
        PunchType CheckType,
        string SourceRawId);

    private bool IsExactPunchEvidence(AttendanceRecord record, ResolvedPunch punch)
    {
        if (record.WorkerId != punch.WorkerId ||
            !string.Equals(record.Source, _sourceOptions.SourceName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AttendancePunchEvidenceMatcher.IsExact(
            record,
            punch.WorkerId,
            _sourceOptions.SourceName,
            punch.CheckTimeUtc,
            punch.CheckType == PunchType.In,
            punch.SourceRawId);
    }

    private AttendanceStatus CalculateStatus(DateTime checkTimeUtc, DateOnly productionDate)
    {
        var shiftStart = _attendanceWorkdayPolicy.GetShiftStartLocal(productionDate);
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
        var window = _attendanceWorkdayPolicy.GetWindow(date);
        return (window.StartUtc, window.EndUtc, window.StartLocal, window.EndLocal);
    }

    private static PunchType? GetPunchType(string? checkType) => checkType?.Trim() switch
    {
        var value when string.Equals(value, "I", StringComparison.OrdinalIgnoreCase) => PunchType.In,
        var value when string.Equals(value, "O", StringComparison.OrdinalIgnoreCase) => PunchType.Out,
        _ => null
    };

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

        return (await _appDbContext.WorkerDefaultAssignments.AsNoTracking()
                .Where(assignment => assignment.IsActive
                    && allActiveWorkerIds.Contains(assignment.WorkerId)
                    && scopeSubStageIds.Contains(assignment.SubStageId)
                    && (!lineId.HasValue || assignment.ProductionLineId == lineId.Value))
                .Select(assignment => assignment.WorkerId)
                .Distinct()
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
    }

    private async Task<HashSet<Guid>> GetSubStageIdsInScopeAsync(Guid? factoryId, Guid? lineId, CancellationToken cancellationToken)
    {
        var subStageIds = await (from subStage in _appDbContext.SubStages.AsNoTracking()
                                join main in _appDbContext.MainStages.AsNoTracking() on subStage.MainStageId equals main.Id
                                join line in _appDbContext.ProductionLines.AsNoTracking() on main.DepartmentId equals line.DepartmentId
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
