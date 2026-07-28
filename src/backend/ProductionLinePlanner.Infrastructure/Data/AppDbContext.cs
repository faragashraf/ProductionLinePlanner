using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Factory> Factories => Set<Factory>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();
    public DbSet<MainStage> MainStages => Set<MainStage>();
    public DbSet<SubStage> SubStages => Set<SubStage>();
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<WorkerDefaultAssignment> WorkerDefaultAssignments => Set<WorkerDefaultAssignment>();
    public DbSet<WorkerTemporaryAssignment> WorkerTemporaryAssignments => Set<WorkerTemporaryAssignment>();
    public DbSet<AssignmentTimelineEntry> AssignmentTimelineEntries => Set<AssignmentTimelineEntry>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AttendanceNotificationEvent> AttendanceNotificationEvents => Set<AttendanceNotificationEvent>();
    public DbSet<StageReadinessSnapshot> StageReadinessSnapshots => Set<StageReadinessSnapshot>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPolicy> NotificationPolicies => Set<NotificationPolicy>();
    public DbSet<NotificationPolicyRecipientRule> NotificationPolicyRecipientRules => Set<NotificationPolicyRecipientRule>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppRole> AppRoles => Set<AppRole>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<ProductModel> ProductModels => Set<ProductModel>();
    public DbSet<ProductModelStage> ProductModelStages => Set<ProductModelStage>();
    public DbSet<WorkerSalaryHistory> WorkerSalaryHistories => Set<WorkerSalaryHistory>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ProductionDayStageResolution> ProductionDayStageResolutions => Set<ProductionDayStageResolution>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<StageProductionRecord> StageProductionRecords => Set<StageProductionRecord>();
    public DbSet<StageProductionWorkerAllocation> StageProductionWorkerAllocations => Set<StageProductionWorkerAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (Database.IsRelational()) EnsureSubStageProductionLineLinks();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (Database.IsRelational()) await EnsureSubStageProductionLineLinksAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Compatibility safeguard for code paths that create a SubStage from its
    // MainStage only. New API flows always set ProductionLineId explicitly;
    // this resolves the same immutable parent relation before EF writes it.
    private void EnsureSubStageProductionLineLinks()
    {
        var pending = PendingSubStages();
        if (pending.Length == 0) return;

        var lineByMainStage = TrackedLineByMainStage();
        var missingIds = pending.Select(entry => entry.Entity.MainStageId)
            .Where(id => !lineByMainStage.ContainsKey(id))
            .Distinct()
            .ToArray();
        if (missingIds.Length > 0)
        {
            foreach (var item in MainStages.AsNoTracking().Where(stage => missingIds.Contains(stage.Id)).Select(stage => new { stage.Id, stage.ProductionLineId }))
                lineByMainStage[item.Id] = item.ProductionLineId;
        }

        BindPendingSubStages(pending, lineByMainStage);
    }

    private async Task EnsureSubStageProductionLineLinksAsync(CancellationToken cancellationToken)
    {
        var pending = PendingSubStages();
        if (pending.Length == 0) return;

        var lineByMainStage = TrackedLineByMainStage();
        var missingIds = pending.Select(entry => entry.Entity.MainStageId)
            .Where(id => !lineByMainStage.ContainsKey(id))
            .Distinct()
            .ToArray();
        if (missingIds.Length > 0)
        {
            var persisted = await MainStages.AsNoTracking()
                .Where(stage => missingIds.Contains(stage.Id))
                .Select(stage => new { stage.Id, stage.ProductionLineId })
                .ToArrayAsync(cancellationToken);
            foreach (var item in persisted)
                lineByMainStage[item.Id] = item.ProductionLineId;
        }

        BindPendingSubStages(pending, lineByMainStage);
    }

    private Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<SubStage>[] PendingSubStages() =>
        ChangeTracker.Entries<SubStage>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified && entry.Entity.ProductionLineId == Guid.Empty)
            .ToArray();

    private Dictionary<Guid, Guid> TrackedLineByMainStage() => ChangeTracker.Entries<MainStage>()
        .Where(entry => entry.State != EntityState.Deleted)
        .GroupBy(entry => entry.Entity.Id)
        .ToDictionary(group => group.Key, group => group.First().Entity.ProductionLineId);

    private static void BindPendingSubStages(
        IReadOnlyCollection<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<SubStage>> pending,
        IReadOnlyDictionary<Guid, Guid> lineByMainStage)
    {
        foreach (var entry in pending)
        {
            if (!lineByMainStage.TryGetValue(entry.Entity.MainStageId, out var productionLineId) || productionLineId == Guid.Empty)
                throw new InvalidOperationException($"Cannot resolve the production line for SubStage {entry.Entity.Id}.");

            entry.Entity.SetProductionLine(productionLineId);
        }
    }
}
