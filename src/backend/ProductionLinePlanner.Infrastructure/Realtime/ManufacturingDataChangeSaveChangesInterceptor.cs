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
    ILogger<ManufacturingDataChangeSaveChangesInterceptor> logger,
    IManufacturingRealtimeChangeContext? changeContext = null) : SaveChangesInterceptor
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
        var changes = CoalesceBatchInvalidations(context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => CreateChange(entry, actorUserId))
            .Where(change => change is not null)
            .Cast<ManufacturingDataChanged>()
            .ToArray());

        if (changes.Length > 0)
        {
            pendingChanges[context.ContextId.InstanceId] = new PendingChanges(
                changes,
                context.Database.CurrentTransaction?.GetDbTransaction());
        }
    }

    private static ManufacturingDataChanged[] CoalesceBatchInvalidations(IReadOnlyCollection<ManufacturingDataChanged> changes)
    {
        var batchTypes = new[]
        {
            ManufacturingEntityType.Worker,
            ManufacturingEntityType.AttendanceRecord
        };
        var result = changes.Where(change => !batchTypes.Contains(change.EntityType)).ToList();

        foreach (var entityType in batchTypes)
        {
            var batch = changes.Where(change => change.EntityType == entityType).ToArray();
            if (batch.Length == 0) continue;
            if (batch.Length == 1)
            {
                result.Add(batch[0]);
                continue;
            }

            var representative = batch[0];
            var dates = batch.SelectMany(change => change.AffectedAttendanceDates ?? [])
                .AppendIfNotNull(batch.Select(change => change.ProductionDate))
                .Distinct()
                .OrderBy(date => date)
                .ToArray();
            var workerIds = batch.SelectMany(change => change.WorkerIds ?? [])
                .AppendIfNotNull(batch.Select(change => change.WorkerId))
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
            var departmentIds = batch.SelectMany(change => change.DepartmentIds ?? [])
                .AppendIfNotNull(batch.Select(change => change.DepartmentId))
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
            result.Add(new ManufacturingDataChanged(
                Guid.NewGuid(),
                entityType,
                ManufacturingChangeType.Updated,
                Guid.Empty,
                batch.Max(change => change.OccurredAtUtc),
                representative.ActorUserId,
                representative.CorrelationId,
                DepartmentId: departmentIds.Length == 1 ? departmentIds[0] : null,
                ProductionDate: dates.Length == 1 ? dates[0] : null,
                WorkerId: workerIds.Length == 1 ? workerIds[0] : null,
                Source: batch.Select(change => change.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                    ? representative.Source
                    : "Application",
                AffectedAttendanceDates: dates,
                WorkerIds: workerIds,
                DepartmentIds: departmentIds,
                AddedAttendanceCount: batch.Sum(change => change.AddedAttendanceCount),
                UpdatedAttendanceCount: batch.Sum(change => change.UpdatedAttendanceCount),
                WorkerChangeKinds: batch.SelectMany(change => change.WorkerChangeKinds ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                AttendanceChangeKinds: batch.SelectMany(change => change.AttendanceChangeKinds ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray()));
        }

        return result.ToArray();
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
            MainStage entity => Create(entry, ManufacturingEntityType.MainStage, entity.Id, actorUserId, departmentId: entity.DepartmentId, mainStageId: entity.Id),
            SubStage entity => Create(entry, ManufacturingEntityType.SubStage, entity.Id, actorUserId, departmentId: entity.DepartmentId, mainStageId: entity.MainStageId, subStageId: entity.Id),
            ProductModel entity => Create(entry, ManufacturingEntityType.ProductModel, entity.Id, actorUserId, productModelId: entity.Id),
            ProductModelStage entity => Create(entry, ManufacturingEntityType.ProductModelStage, entity.Id, actorUserId, productionLineId: entity.ProductionLineId, productModelId: entity.ProductModelId, subStageId: entity.SubStageId),
            ProductionOrder entity => Create(
                entry,
                ManufacturingEntityType.ProductionOrder,
                entity.Id,
                actorUserId,
                factoryId: entity.ProductionLine?.FactoryId ?? ResolveFactoryId(entry.Context, entity.ProductionLineId),
                productionLineId: entity.ProductionLineId,
                productModelId: entity.ProductModelId,
                productionDate: entity.ProductionDate),
            StageProductionRecord entity => Create(
                entry,
                ManufacturingEntityType.StageProductionRecord,
                entity.Id,
                actorUserId,
                productionDate: entity.ProductionDate),
            AttendanceRecord entity => Create(
                entry,
                ManufacturingEntityType.AttendanceRecord,
                entity.Id,
                actorUserId,
                productionDate: changeContext?.ProductionDate ?? DateOnly.FromDateTime(entity.AttendanceTimeUtc),
                workerId: entity.WorkerId,
                affectedAttendanceDates: [changeContext?.ProductionDate ?? DateOnly.FromDateTime(entity.AttendanceTimeUtc)],
                workerIds: [entity.WorkerId],
                addedAttendanceCount: entry.State == EntityState.Added ? 1 : 0,
                updatedAttendanceCount: entry.State == EntityState.Modified ? 1 : 0,
                attendanceChangeKinds: [entry.State == EntityState.Added ? "created" : "updated"]),
            AttendanceSyncState entity => Create(
                entry,
                ManufacturingEntityType.AttendanceSyncState,
                entity.Id,
                actorUserId,
                productionDate: entity.OperationalDate,
                affectedAttendanceDates: [entity.OperationalDate]),
            Worker entity => CreateWorkerChange(entry, entity, actorUserId),
            WorkerDefaultAssignment entity => CreateDefaultAssignmentChange(entry, entity, actorUserId),
            _ => null
        };
    }

    private ManufacturingDataChanged CreateWorkerChange(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        Worker entity,
        Guid? actorUserId)
    {
        var departmentIds = entry.Properties
            .Where(property => property.Metadata.Name == nameof(Worker.OrganizationalDepartmentId))
            .SelectMany(property => new[] { property.OriginalValue as Guid?, property.CurrentValue as Guid? })
            .Where(value => value.HasValue && value.Value != Guid.Empty)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();

        return Create(
            entry,
            ManufacturingEntityType.Worker,
            entity.Id,
            actorUserId,
            departmentId: entity.OrganizationalDepartmentId,
            workerId: entity.Id,
            workerIds: [entity.Id],
            departmentIds: departmentIds,
            workerChangeKinds: WorkerChangeKinds(entry));
    }

    private ManufacturingDataChanged CreateDefaultAssignmentChange(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        WorkerDefaultAssignment entity,
        Guid? actorUserId)
    {
        var location = ResolveDefaultAssignmentLocation(entry.Context, entity.ProductionLineId, entity.SubStageId);
        return Create(
            entry,
            ManufacturingEntityType.WorkerDefaultAssignment,
            entity.Id,
            actorUserId,
            factoryId: location.FactoryId,
            departmentId: location.DepartmentId,
            productionLineId: location.ProductionLineId,
            mainStageId: location.MainStageId,
            subStageId: entity.SubStageId,
            workerId: entity.WorkerId);
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
        Guid? subStageId = null,
        DateOnly? productionDate = null,
        Guid? workerId = null,
        IReadOnlyList<DateOnly>? affectedAttendanceDates = null,
        IReadOnlyList<Guid>? workerIds = null,
        IReadOnlyList<Guid>? departmentIds = null,
        int addedAttendanceCount = 0,
        int updatedAttendanceCount = 0,
        IReadOnlyList<string>? workerChangeKinds = null,
        IReadOnlyList<string>? attendanceChangeKinds = null) =>
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
            subStageId,
            productionDate,
            workerId,
            changeContext?.Source ?? "Application",
            affectedAttendanceDates,
            workerIds,
            departmentIds,
            addedAttendanceCount,
            updatedAttendanceCount,
            workerChangeKinds,
            attendanceChangeKinds);

    private static DefaultAssignmentLocation ResolveDefaultAssignmentLocation(DbContext? context, Guid productionLineId, Guid subStageId)
    {
        if (context is null) return new DefaultAssignmentLocation(null, null, null, null);

        var trackedStage = context.ChangeTracker.Entries<SubStage>()
            .Where(entry => entry.State != EntityState.Deleted && entry.Entity.Id == subStageId)
            .Select(entry => entry.Entity)
            .FirstOrDefault();
        var mainStageId = trackedStage?.MainStageId;
        var departmentId = trackedStage?.DepartmentId;

        if (!mainStageId.HasValue || mainStageId == Guid.Empty || !departmentId.HasValue || departmentId == Guid.Empty)
        {
            var persistedStage = context.Set<SubStage>()
                .AsNoTracking()
                .Where(stage => stage.Id == subStageId)
                .Select(stage => new { stage.DepartmentId, stage.MainStageId })
                .SingleOrDefault();
            departmentId = persistedStage?.DepartmentId;
            mainStageId = persistedStage?.MainStageId;
        }

        if (productionLineId == Guid.Empty)
            return new DefaultAssignmentLocation(null, departmentId, null, mainStageId);

        var factoryId = context.ChangeTracker.Entries<ProductionLine>()
            .Where(entry => entry.State != EntityState.Deleted && entry.Entity.Id == productionLineId)
            .Select(entry => (Guid?)entry.Entity.FactoryId)
            .FirstOrDefault()
            ?? context.Set<ProductionLine>()
                .AsNoTracking()
                .Where(line => line.Id == productionLineId)
                .Select(line => (Guid?)line.FactoryId)
                .SingleOrDefault();

        return new DefaultAssignmentLocation(factoryId, departmentId, productionLineId, mainStageId);
    }

    private static Guid? ResolveFactoryId(DbContext? context, Guid? productionLineId)
    {
        if (context is null || productionLineId is null)
        {
            return null;
        }

        return context.ChangeTracker.Entries<ProductionLine>()
            .Where(entry => entry.Entity.Id == productionLineId)
            .Select(entry => (Guid?)entry.Entity.FactoryId)
            .FirstOrDefault()
            ?? context.Set<ProductionLine>().Local.FirstOrDefault(line => line.Id == productionLineId)?.FactoryId;
    }

    private static ManufacturingChangeType ResolveChangeType(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, ManufacturingEntityType entityType)
    {
        if (entityType == ManufacturingEntityType.WorkerDefaultAssignment)
        {
            if (entry.State == EntityState.Added) return ManufacturingChangeType.PermanentAssignmentCreated;
            if (entry.State == EntityState.Deleted) return ManufacturingChangeType.PermanentAssignmentCancelled;

            var assignmentActive = entry.Properties.FirstOrDefault(property => property.Metadata.Name == "IsActive");
            if (assignmentActive?.IsModified == true && assignmentActive.OriginalValue is bool assignmentWasActive && assignmentActive.CurrentValue is bool assignmentIsActive && assignmentWasActive && !assignmentIsActive)
                return ManufacturingChangeType.PermanentAssignmentCancelled;

            return ManufacturingChangeType.PermanentAssignmentUpdated;
        }

        if (entry.State == EntityState.Added) return ManufacturingChangeType.Created;
        if (entry.State == EntityState.Deleted) return ManufacturingChangeType.Deleted;

        var active = entry.Properties.FirstOrDefault(property => property.Metadata.Name == "IsActive");
        if (active?.IsModified == true && active.OriginalValue is bool wasActive && active.CurrentValue is bool isActive)
        {
            return isActive && !wasActive ? ManufacturingChangeType.Activated : !isActive && wasActive ? ManufacturingChangeType.Deactivated : ManufacturingChangeType.Updated;
        }

        if ((entityType == ManufacturingEntityType.ProductModelStage && entry.Properties.Any(property => property.IsModified && property.Metadata.Name is "ProductModelId" or "SubStageId")) ||
            (entityType == ManufacturingEntityType.ProductionLine && entry.Properties.Any(property => property.IsModified && property.Metadata.Name == "DepartmentId")) ||
            (entityType == ManufacturingEntityType.Worker && entry.Properties.Any(property => property.IsModified && property.Metadata.Name == nameof(Worker.OrganizationalDepartmentId))))
        {
            return ManufacturingChangeType.RelationshipChanged;
        }

        return entry.Properties.Any(property => property.IsModified && property.Metadata.Name is "SequenceOrder" or "DefaultOrder" or "StageOrder")
            ? ManufacturingChangeType.Reordered
            : ManufacturingChangeType.Updated;
    }

    private static IReadOnlyList<string> WorkerChangeKinds(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        if (entry.State == EntityState.Added) return ["created"];
        if (entry.State == EntityState.Deleted) return ["deleted"];

        var names = entry.Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name).ToHashSet();
        var kinds = new List<string>();
        if (names.Overlaps([nameof(Worker.IsActive), nameof(Worker.EmploymentStatus), nameof(Worker.EmploymentEndDate)])) kinds.Add("employment-status");
        if (names.Contains(nameof(Worker.OrganizationalDepartmentId))) kinds.Add("department-assignment");
        if (names.Overlaps([nameof(Worker.AttendanceUserId), nameof(Worker.BadgeNumber), nameof(Worker.LastExternalSyncAt)])) kinds.Add("attendance-identity");
        if (kinds.Count == 0) kinds.Add("profile");
        return kinds;
    }

    private string? CorrelationId() => changeContext?.CorrelationId ?? correlationContext.CorrelationId;

    private sealed record DefaultAssignmentLocation(Guid? FactoryId, Guid? DepartmentId, Guid? ProductionLineId, Guid? MainStageId);

    private sealed record PendingChanges(IReadOnlyList<ManufacturingDataChanged> Changes, DbTransaction? Transaction);
}

internal static class ManufacturingRealtimeEnumerableExtensions
{
    public static IEnumerable<T> AppendIfNotNull<T>(this IEnumerable<T> source, IEnumerable<T?> values) where T : struct =>
        source.Concat(values.Where(value => value.HasValue).Select(value => value!.Value));
}
