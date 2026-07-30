using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

/// <summary>
/// Serializes source synchronization per attendance source and production date.
/// The actual work uses an independent scoped executor so RequestAborted only
/// ends the caller's wait, never a partially-owned database operation.
/// </summary>
public sealed class AttendanceSyncCoordinator : IAttendanceSyncService
{
    private readonly ConcurrentDictionary<SyncKey, ActiveSync> _activeSyncs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AttendanceSourceOptions _sourceOptions;
    private readonly ILogger<AttendanceSyncCoordinator> _logger;
    private readonly IAttendanceWorkdayPolicy _attendanceWorkdayPolicy;
    private readonly Func<Task>? _afterFailedTryAddAsync;

    public AttendanceSyncCoordinator(
        IServiceScopeFactory scopeFactory,
        IOptions<AttendanceSourceOptions> sourceOptions,
        ILogger<AttendanceSyncCoordinator> logger,
        ICairoTimeZoneProvider cairoTimeZoneProvider,
        IAttendanceWorkdayPolicy? attendanceWorkdayPolicy = null)
        : this(scopeFactory, sourceOptions, logger, cairoTimeZoneProvider, null, attendanceWorkdayPolicy)
    {
    }

    internal AttendanceSyncCoordinator(
        IServiceScopeFactory scopeFactory,
        IOptions<AttendanceSourceOptions> sourceOptions,
        ILogger<AttendanceSyncCoordinator> logger,
        ICairoTimeZoneProvider cairoTimeZoneProvider,
        Func<Task>? afterFailedTryAddAsync,
        IAttendanceWorkdayPolicy? attendanceWorkdayPolicy = null)
    {
        _scopeFactory = scopeFactory;
        _sourceOptions = sourceOptions.Value;
        _logger = logger;
        _attendanceWorkdayPolicy = attendanceWorkdayPolicy ?? new AttendanceWorkdayPolicy(sourceOptions, cairoTimeZoneProvider);
        _afterFailedTryAddAsync = afterFailedTryAddAsync;
    }

    public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default)
    {
        return SyncForProductionDateAsync(_attendanceWorkdayPolicy.GetOperationalDate(DateTime.UtcNow), cancellationToken);
    }

    public async Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(
        DateOnly productionDate,
        CancellationToken cancellationToken = default)
    {
        if (productionDate == default)
        {
            return Result<AttendanceSyncResultDto>.Failure(new Error("ValidationError", "Production date is required."));
        }

        var key = new SyncKey(_sourceOptions.SourceName, productionDate);
        var correlationId = Guid.NewGuid().ToString("N");
        while (true)
        {
            var active = new ActiveSync(correlationId);
            if (!_activeSyncs.TryAdd(key, active))
            {
                if (_afterFailedTryAddAsync is not null)
                {
                    await _afterFailedTryAddAsync();
                }

                // The active operation can finish and remove itself after TryAdd
                // reports contention. Never index into the dictionary here: the
                // key may already be gone by the time this request observes it.
                if (_activeSyncs.TryGetValue(key, out var existing))
                {
                    _logger.LogInformation(
                        "Attendance sync request reused active operation. correlationId={CorrelationId}, activeCorrelationId={ActiveCorrelationId}, date={SyncDate}, trigger={TriggerType}",
                        correlationId,
                        existing.CorrelationId,
                        productionDate,
                        "manual");
                    return Result<AttendanceSyncResultDto>.Failure(new Error("AttendanceSyncInProgress", "Attendance synchronization is already running for this production date."));
                }

                // The operation that caused contention has already completed and
                // cleaned up. Retry acquisition through the normal atomic TryAdd
                // path; this is not a wait loop.
                continue;
            }

            active.Task = ExecuteAsync(key, active, productionDate);

            try
            {
                return await active.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Attendance sync caller cancelled its wait. correlationId={CorrelationId}, date={SyncDate}, cancellationSource={CancellationSource}",
                    active.CorrelationId,
                    productionDate,
                    "request-token");
                return Result<AttendanceSyncResultDto>.Failure(new Error("AttendanceSyncClientCancelled", "Attendance synchronization request was cancelled by the client."));
            }
        }
    }

    private async Task<Result<AttendanceSyncResultDto>> ExecuteAsync(SyncKey key, ActiveSync active, DateOnly productionDate)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IAttendanceSyncRunner>();
            return await runner.RunAsync(
                new AttendanceSyncExecutionContext(productionDate, active.CorrelationId, "manual"),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Attendance sync operation failed outside the executor. correlationId={CorrelationId}, date={SyncDate}",
                active.CorrelationId,
                productionDate);
            return Result<AttendanceSyncResultDto>.Failure(new Error("AttendanceSyncFailed", "Attendance synchronization could not be completed."));
        }
        finally
        {
            _activeSyncs.TryRemove(key, out _);
        }
    }

    private sealed class ActiveSync(string correlationId)
    {
        public string CorrelationId { get; } = correlationId;
        public Task<Result<AttendanceSyncResultDto>> Task { get; set; } = System.Threading.Tasks.Task.FromResult(
            Result<AttendanceSyncResultDto>.Failure(new Error("AttendanceSyncFailed", "Attendance synchronization did not start.")));
    }

    private readonly record struct SyncKey(string SourceName, DateOnly ProductionDate);
}
