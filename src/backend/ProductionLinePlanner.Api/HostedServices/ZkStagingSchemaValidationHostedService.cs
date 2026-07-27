using ProductionLinePlanner.Infrastructure.Attendance.Services;

namespace ProductionLinePlanner.Api.HostedServices;

/// <summary>
/// Fails API startup with an actionable configuration error when Staging mode is selected before
/// its database contract is installed. Program registers this service only for Staging mode.
/// </summary>
public sealed class ZkStagingSchemaValidationHostedService(
    IZkStagingSchemaValidator validator,
    ILogger<ZkStagingSchemaValidationHostedService> logger) : IHostedService, IZkStagingSchemaReadiness
{
    private readonly TaskCompletionSource schemaReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await validator.ValidateAsync(cancellationToken);
            schemaReady.TrySetResult();
            logger.LogInformation("ZKTime staging schema version {SchemaVersion} is ready.", ZkTimeStagingSchema.CurrentVersion);
        }
        catch (Exception exception)
        {
            schemaReady.TrySetException(exception);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => schemaReady.Task.WaitAsync(cancellationToken);
}

public interface IZkStagingSchemaReadiness
{
    Task WaitUntilReadyAsync(CancellationToken cancellationToken);
}
