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
    ICairoTimeZoneProvider cairoTimeZoneProvider,
    ILogger<ZkStagingProcessingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(sourceOptions.Value.StagingProcessorIntervalSeconds);
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

            await Task.Delay(interval, stoppingToken);
        }
    }

    internal async Task ProcessCycleAsync(CancellationToken cancellationToken)
    {
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

        var cairoNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoTimeZoneProvider.TimeZone);
        var today = DateOnly.FromDateTime(cairoNow);
        var dates = pendingDates.Value!.Length == 0
            ? [today]
            : pendingDates.Value.Distinct().OrderBy(date => date).ToArray();

        foreach (var productionDate in dates)
        {
            var result = await attendanceSyncService.SyncForProductionDateAsync(productionDate, cancellationToken);
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
