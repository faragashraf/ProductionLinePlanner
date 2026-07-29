using ProductionLinePlanner.Application.Notifications;

namespace ProductionLinePlanner.Api.HostedServices;

public sealed class AttendanceNotificationOutboxBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<AttendanceNotificationOutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IAttendanceNotificationOutboxProcessor>();
                await processor.ProcessPendingAsync(cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Attendance notification outbox cycle failed; durable events remain pending for retry.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
