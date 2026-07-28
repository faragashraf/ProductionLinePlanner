using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Infrastructure.Realtime;

/// <summary>Scoped ambient metadata for one logical application mutation batch.</summary>
public sealed class ManufacturingRealtimeChangeContext : IManufacturingRealtimeChangeContext
{
    private readonly AsyncLocal<State?> current = new();

    public string Source => current.Value?.Source ?? "Application";
    public string? CorrelationId => current.Value?.CorrelationId;
    public DateOnly? ProductionDate => current.Value?.ProductionDate;

    public IDisposable Begin(string source, string? correlationId = null, DateOnly? productionDate = null)
    {
        var previous = current.Value;
        current.Value = new State(
            string.IsNullOrWhiteSpace(source) ? previous?.Source ?? "Application" : source.Trim(),
            string.IsNullOrWhiteSpace(correlationId) ? previous?.CorrelationId : correlationId.Trim(),
            productionDate ?? previous?.ProductionDate);
        return new Scope(() => current.Value = previous);
    }

    private sealed record State(string Source, string? CorrelationId, DateOnly? ProductionDate);

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? disposeAction = dispose;
        public void Dispose() => Interlocked.Exchange(ref disposeAction, null)?.Invoke();
    }
}
