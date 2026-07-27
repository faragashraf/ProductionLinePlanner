using ProductionLinePlanner.Infrastructure.Attendance.Services;

namespace ProductionLinePlanner.Api.HostedServices;

/// <summary>
/// Fails API startup with an actionable configuration error when Staging mode is selected before
/// its database contract is installed. Program registers this service only for Staging mode.
/// </summary>
public sealed class ZkStagingSchemaValidationHostedService(
    IZkStagingSchemaValidator validator,
    ILogger<ZkStagingSchemaValidationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await validator.ValidateAsync(cancellationToken);
        logger.LogInformation("ZKTime staging schema version {SchemaVersion} is ready.", ZkTimeStagingSchema.CurrentVersion);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
