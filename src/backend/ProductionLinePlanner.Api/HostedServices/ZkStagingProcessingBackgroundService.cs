using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;

namespace ProductionLinePlanner.Api.HostedServices;

/// <summary>
/// Drains durable staging rows after the SQL Agent collector runs. It is registered only in
/// Staging mode; source leasing and the existing attendance coordinator make each cycle retry-safe.
/// </summary>
public sealed class ZkStagingProcessingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IAttendanceSyncService attendanceSyncService,
    IOptions<AttendanceSourceOptions> sourceOptions,
    IZkStagingSchemaReadiness schemaReadiness,
    ILogger<ZkStagingProcessingBackgroundService> logger) : BackgroundService
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
            logger.LogCritical(exception, "ZKTime staging processor could not read its startup options and will stop.");
            return;
        }

        var intervalSeconds = options.StagingProcessorIntervalSeconds;
        if (intervalSeconds <= 0)
        {
            intervalSeconds = 60;
            logger.LogWarning(
                "ZKTime staging processor received an invalid interval. configuredIntervalSeconds={ConfiguredIntervalSeconds}, fallbackIntervalSeconds={FallbackIntervalSeconds}",
                options.StagingProcessorIntervalSeconds,
                intervalSeconds);
        }

        logger.LogInformation(
            "ZKTime staging processor started. intervalSeconds={IntervalSeconds}, maxPendingDates={MaxPendingDates}, dayStartTime={DayStartTime}",
            intervalSeconds,
            options.MaxPendingProductionDates,
            options.DayStartTime);

        try
        {
            await schemaReadiness.WaitUntilReadyAsync(stoppingToken);
            var interval = TimeSpan.FromSeconds(intervalSeconds);
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
                    logger.LogError(exception, "Unexpected failure in the ZKTime staging processing cycle.");
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ZKTime staging processor stopped before it could begin processing.");
        }
        finally
        {
            logger.LogInformation("ZKTime staging processor stopped.");
        }
    }

    internal async Task ProcessCycleAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("ZKTime staging processing cycle started.");
        await using var scope = scopeFactory.CreateAsyncScope();
        var backlog = scope.ServiceProvider.GetRequiredService<IZkStagingBacklogReader>();
        var pendingDates = await backlog.GetPendingProductionDatesAsync(
            sourceOptions.Value.DayStartTime,
            sourceOptions.Value.MaxPendingProductionDates,
            cancellationToken);
        if (pendingDates.IsFailure)
        {
            logger.LogWarning("ZKTime staging backlog could not be inspected. errorCode={ErrorCode}", pendingDates.Error?.Code);
            return;
        }

        var dates = pendingDates.Value?.Distinct().OrderBy(date => date).ToArray() ?? [];
        logger.LogInformation(
            "ZKTime staging backlog inspected. pendingDateCount={PendingDateCount}",
            dates.Length);

        if (dates.Length == 0)
        {
            // This service drains only durable staging work. Falling back to today would run the
            // daily attendance engine without a staged punch and could create misleading absences.
            return;
        }

        foreach (var productionDate in dates)
        {
            logger.LogInformation(
                "ZKTime staging processing date started. productionDate={ProductionDate}",
                productionDate);
            var result = await attendanceSyncService.SyncForProductionDateAsync(productionDate, cancellationToken);
            logger.LogInformation(
                "ZKTime staging processing date completed. productionDate={ProductionDate}, succeeded={Succeeded}, errorCode={ErrorCode}",
                productionDate,
                result.IsSuccess,
                result.Error?.Code);
            if (result.IsFailure && result.Error?.Code != "AttendanceSyncInProgress")
            {
                logger.LogWarning(
                    "ZKTime staging processing failed. productionDate={ProductionDate}, errorCode={ErrorCode}",
                    productionDate,
                    result.Error?.Code);
            }
        }
    }
}
