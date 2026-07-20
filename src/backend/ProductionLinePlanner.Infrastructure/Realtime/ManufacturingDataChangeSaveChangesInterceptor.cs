using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Realtime;

/// <summary>
/// Captures manufacturing catalog mutations at the persistence boundary, then
/// sends lightweight invalidation messages only after a successful save.
/// </summary>
public sealed class ManufacturingDataChangeSaveChangesInterceptor(
    IManufacturingDataChangePublisher publisher,
    ICurrentUserService currentUserService,
    IManufacturingRealtimeCorrelationContext correlationContext,
    ManufacturingDataChangeTransactionCoordinator transactionCoordinator,
    ILogger<ManufacturingDataChangeSaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    private readonly ConcurrentDictionary<Guid, PendingChanges> pendingChanges = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        PublishAfterSaveAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PublishAfterSaveAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        RemovePending(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        RemovePending(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null) return;

        var actorUserId = currentUserService.UserId;
        var changes = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => CreateChange(entry, actorUserId))
            .Where(change => change is not null)
            .Cast<ManufacturingDataChanged>()
            .ToArray();

        if (changes.Length > 0)
        {
            pendingChanges[context.ContextId.InstanceId] = new PendingChanges(
                changes,
                context.Database.CurrentTransaction?.GetDbTransaction());
        }
    }

    private async Task PublishAfterSaveAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || !pendingChanges.TryRemove(context.ContextId.InstanceId, out var pending)) return;
        if (pending.Transaction is not null)
        {
            transactionCoordinator.Enqueue(pending.Transaction, pending.Changes);
            return;
        }

        foreach (var change in pending.Changes)
        {
            try
            {
                await publisher.PublishAsync(change, cancellationToken);
            }
            catch (Exception exception)
            {
                // A committed master-data mutation must remain successful even
                // when the transient realtime transport is unavailable.
                logger.LogWarning(exception, "Manufacturing realtime notification failed after saving {EntityType} {EntityId}.", change.EntityType, change.EntityId);
            }
        }
    }

    private void RemovePending(DbContext? context)
    {
        if (context is not null)
        {
            pendingChanges.TryRemove(context.ContextId.InstanceId, out _);
        }
    }

    private ManufacturingDataChanged? CreateChange(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, Guid? actorUserId)
    {
        return entry.Entity switch
        {
            Factory entity => Create(entry, ManufacturingEntityType.Factory, entity.Id, actorUserId, factoryId: entity.Id),
            Department entity => Create(entry, ManufacturingEntityType.Department, entity.Id, actorUserId, factoryId: entity.FactoryId, departmentId: entity.Id),
            ProductionLine entity => Create(entry, ManufacturingEntityType.ProductionLine, entity.Id, actorUserId, factoryId: entity.FactoryId, departmentId: entity.DepartmentId, productionLineId: entity.Id),
            MainStage entity => Create(entry, ManufacturingEntityType.MainStage, entity.Id, actorUserId, productionLineId: entity.ProductionLineId, mainStageId: entity.Id),
            SubStage entity => Create(entry, ManufacturingEntityType.SubStage, entity.Id, actorUserId, productionLineId: entity.ProductionLineId, mainStageId: entity.MainStageId, subStageId: entity.Id),
            ProductModel entity => Create(entry, ManufacturingEntityType.ProductModel, entity.Id, actorUserId, productModelId: entity.Id),
            ProductModelStage entity => Create(entry, ManufacturingEntityType.ProductModelStage, entity.Id, actorUserId, productModelId: entity.ProductModelId, subStageId: entity.SubStageId),
            Worker entity => Create(entry, ManufacturingEntityType.Worker, entity.Id, actorUserId),
            _ => null
        };
    }

    private ManufacturingDataChanged Create(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        ManufacturingEntityType entityType,
        Guid entityId,
        Guid? actorUserId,
        Guid? factoryId = null,
        Guid? departmentId = null,
        Guid? productionLineId = null,
        Guid? mainStageId = null,
        Guid? productModelId = null,
        Guid? subStageId = null) =>
        new(
            Guid.NewGuid(),
            entityType,
            ResolveChangeType(entry, entityType),
            entityId,
            DateTime.UtcNow,
            actorUserId,
            CorrelationId(),
            factoryId,
            departmentId,
            productionLineId,
            mainStageId,
            productModelId,
            subStageId);

    private static ManufacturingChangeType ResolveChangeType(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, ManufacturingEntityType entityType)
    {
        if (entry.State == EntityState.Added) return ManufacturingChangeType.Created;
        if (entry.State == EntityState.Deleted) return ManufacturingChangeType.Deleted;

        var active = entry.Properties.FirstOrDefault(property => property.Metadata.Name == "IsActive");
        if (active?.IsModified == true && active.OriginalValue is bool wasActive && active.CurrentValue is bool isActive)
        {
            return isActive && !wasActive ? ManufacturingChangeType.Activated : !isActive && wasActive ? ManufacturingChangeType.Deactivated : ManufacturingChangeType.Updated;
        }

        if ((entityType == ManufacturingEntityType.ProductModelStage && entry.Properties.Any(property => property.IsModified && property.Metadata.Name is "ProductModelId" or "SubStageId")) ||
            (entityType == ManufacturingEntityType.ProductionLine && entry.Properties.Any(property => property.IsModified && property.Metadata.Name == "DepartmentId")))
        {
            return ManufacturingChangeType.RelationshipChanged;
        }

        return entry.Properties.Any(property => property.IsModified && property.Metadata.Name is "SequenceOrder" or "DefaultOrder" or "StageOrder")
            ? ManufacturingChangeType.Reordered
            : ManufacturingChangeType.Updated;
    }

    private string? CorrelationId() => correlationContext.CorrelationId;

    private sealed record PendingChanges(IReadOnlyList<ManufacturingDataChanged> Changes, DbTransaction? Transaction);
}
