using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Infrastructure.Realtime;

internal sealed class NoopManufacturingDataChangePublisher : IManufacturingDataChangePublisher
{
    public Task PublishAsync(ManufacturingDataChanged change, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
