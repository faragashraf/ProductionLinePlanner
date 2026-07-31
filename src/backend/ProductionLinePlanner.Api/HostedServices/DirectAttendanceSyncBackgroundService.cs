using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Infrastructure.Attendance;

namespace ProductionLinePlanner.Api.HostedServices;

/// <summary>
/// Keeps today's attendance authoritative while the application is running in Direct mode.
/// The singleton attendance coordinator serializes this cycle with any manual synchronization.
/// </summary>
public sealed class DirectAttendanceSyncBackgroundService(
    IAttendanceSyncService attendanceSyncService,
    IOptions<AttendanceSourceOptions> sourceOptions,
    ILogger<DirectAttendanceSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AttendanceSourceOptions options;
        try
        {
            options = sourceOptions.Value;
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Direct attendance sync could not read its startup options and will stop.");
            return;
        }

        var intervalSeconds = options.DirectSyncIntervalSeconds;
        if (intervalSeconds <= 0)
        {
            intervalSeconds = 60;
            logger.LogWarning(
                "Direct attendance sync received an invalid interval. configuredIntervalSeconds={ConfiguredIntervalSeconds}, fallbackIntervalSeconds={FallbackIntervalSeconds}",
                options.DirectSyncIntervalSeconds,
                intervalSeconds);
        }

        logger.LogInformation(
            "Direct attendance sync started. intervalSeconds={IntervalSeconds}",
            intervalSeconds);

        var interval = TimeSpan.FromSeconds(intervalSeconds);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Unexpected failure in the direct attendance synchronization cycle.");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            logger.LogInformation("Direct attendance sync stopped.");
        }
    }

    internal async Task ProcessCycleAsync(CancellationToken cancellationToken)
    {
        var result = await attendanceSyncService.SyncTodayAsync(cancellationToken);
        if (result.IsFailure && result.Error?.Code != "AttendanceSyncInProgress")
        {
            logger.LogWarning(
                "Direct attendance synchronization failed. errorCode={ErrorCode}",
                result.Error?.Code);
        }
    }
}
