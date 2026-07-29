using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ProductionLinePlanner.Infrastructure.Realtime;

/// <summary>Publishes only the changes whose explicit database transaction committed.</summary>
public sealed class ManufacturingDataChangeTransactionInterceptor(
    ManufacturingDataChangeTransactionCoordinator coordinator) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        coordinator.PublishCommittedAsync(transaction, CancellationToken.None).GetAwaiter().GetResult();
        base.TransactionCommitted(transaction, eventData);
    }

    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await coordinator.PublishCommittedAsync(transaction, cancellationToken);
        await base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        coordinator.Discard(transaction);
        base.TransactionRolledBack(transaction, eventData);
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        coordinator.Discard(transaction);
        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        coordinator.Discard(transaction);
        base.TransactionFailed(transaction, eventData);
    }

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        coordinator.Discard(transaction);
        return base.TransactionFailedAsync(transaction, eventData, cancellationToken);
    }
}
