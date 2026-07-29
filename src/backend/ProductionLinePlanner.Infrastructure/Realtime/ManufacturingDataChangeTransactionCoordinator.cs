using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Infrastructure.Realtime;

/// <summary>
/// Holds invalidation hints produced inside an explicit EF transaction until its
/// database commit has completed successfully.
/// </summary>
public sealed class ManufacturingDataChangeTransactionCoordinator(
    IManufacturingDataChangePublisher publisher,
    ILogger<ManufacturingDataChangeTransactionCoordinator> logger)
{
    private readonly ConcurrentDictionary<DbTransaction, ConcurrentQueue<ManufacturingDataChanged>> pending = new();

    public void Enqueue(DbTransaction transaction, IReadOnlyList<ManufacturingDataChanged> changes)
    {
        if (changes.Count == 0) return;
        var queue = pending.GetOrAdd(transaction, _ => new ConcurrentQueue<ManufacturingDataChanged>());
        foreach (var change in changes) queue.Enqueue(change);
    }

    public async Task PublishCommittedAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (!pending.TryRemove(transaction, out var changes)) return;
        while (changes.TryDequeue(out var change))
        {
            try
            {
                await publisher.PublishAsync(change, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Manufacturing realtime notification failed after committing {EntityType} {EntityId}.", change.EntityType, change.EntityId);
            }
        }
    }

    public void Discard(DbTransaction transaction) => pending.TryRemove(transaction, out _);
}
