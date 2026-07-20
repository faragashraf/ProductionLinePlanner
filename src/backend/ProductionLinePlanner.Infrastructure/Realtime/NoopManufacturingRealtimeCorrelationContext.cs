using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Infrastructure.Realtime;

public sealed class NoopManufacturingRealtimeCorrelationContext : IManufacturingRealtimeCorrelationContext
{
    public string? CorrelationId => null;
}
