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
        EnsureSubStageDepartmentLinks();
        EnsureProductModelStageDepartmentLinks();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await EnsureSubStageDepartmentLinksAsync(cancellationToken);
        await EnsureProductModelStageDepartmentLinksAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // The duplicated department key exists only for enforceable SQL integrity.
    // Resolve it from the owning MainStage before writes that provide only MainStageId.
    private void EnsureSubStageDepartmentLinks()
    {
        var pending = PendingSubStages();
        if (pending.Length == 0) return;

        var departmentByMainStage = TrackedDepartmentByMainStage();
        var missingIds = pending.Select(entry => entry.Entity.MainStageId)
            .Where(id => !departmentByMainStage.ContainsKey(id))
            .Distinct()
            .ToArray();
        if (missingIds.Length > 0)
        {
            foreach (var item in MainStages.AsNoTracking().Where(stage => missingIds.Contains(stage.Id)).Select(stage => new { stage.Id, stage.DepartmentId }))
                departmentByMainStage[item.Id] = item.DepartmentId;
        }

        BindPendingSubStages(pending, departmentByMainStage);
    }

    private async Task EnsureSubStageDepartmentLinksAsync(CancellationToken cancellationToken)
    {
        var pending = PendingSubStages();
        if (pending.Length == 0) return;

        var departmentByMainStage = TrackedDepartmentByMainStage();
        var missingIds = pending.Select(entry => entry.Entity.MainStageId)
            .Where(id => !departmentByMainStage.ContainsKey(id))
            .Distinct()
            .ToArray();
        if (missingIds.Length > 0)
        {
            var persisted = await MainStages.AsNoTracking()
                .Where(stage => missingIds.Contains(stage.Id))
                .Select(stage => new { stage.Id, stage.DepartmentId })
                .ToArrayAsync(cancellationToken);
            foreach (var item in persisted)
                departmentByMainStage[item.Id] = item.DepartmentId;
        }

        BindPendingSubStages(pending, departmentByMainStage);
    }

    private Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<SubStage>[] PendingSubStages() =>
        ChangeTracker.Entries<SubStage>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified && entry.Entity.DepartmentId == Guid.Empty)
            .ToArray();

    private Dictionary<Guid, Guid> TrackedDepartmentByMainStage() => ChangeTracker.Entries<MainStage>()
        .Where(entry => entry.State != EntityState.Deleted)
        .GroupBy(entry => entry.Entity.Id)
        .ToDictionary(group => group.Key, group => group.First().Entity.DepartmentId);

    private static void BindPendingSubStages(
        IReadOnlyCollection<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<SubStage>> pending,
        IReadOnlyDictionary<Guid, Guid> departmentByMainStage)
    {
        foreach (var entry in pending)
        {
            if (!departmentByMainStage.TryGetValue(entry.Entity.MainStageId, out var departmentId) || departmentId == Guid.Empty)
                throw new InvalidOperationException($"Cannot resolve the department for SubStage {entry.Entity.Id}.");

            entry.Entity.SetDepartment(departmentId);
        }
    }

    private void EnsureProductModelStageDepartmentLinks()
    {
        var pending = PendingProductModelStages();
        if (pending.Length == 0) return;

        var lineDepartments = TrackedDepartmentByProductionLine();
        var stageDepartments = TrackedDepartmentBySubStage();
        var lineIds = pending.Select(entry => entry.Entity.ProductionLineId)
            .Where(id => !lineDepartments.ContainsKey(id))
            .Distinct()
            .ToArray();
        var stageIds = pending.Select(entry => entry.Entity.SubStageId)
            .Where(id => !stageDepartments.ContainsKey(id))
            .Distinct()
            .ToArray();
        foreach (var line in ProductionLines.AsNoTracking()
            .Where(line => lineIds.Contains(line.Id))
            .Select(line => new { line.Id, line.DepartmentId })
            .ToArray())
            lineDepartments[line.Id] = line.DepartmentId;
        foreach (var stage in SubStages.AsNoTracking()
            .Where(stage => stageIds.Contains(stage.Id))
            .Select(stage => new { stage.Id, stage.DepartmentId })
            .ToArray())
            stageDepartments[stage.Id] = stage.DepartmentId;

        ValidateProductModelStageDepartments(pending, lineDepartments, stageDepartments);
    }

    private async Task EnsureProductModelStageDepartmentLinksAsync(CancellationToken cancellationToken)
    {
        var pending = PendingProductModelStages();
        if (pending.Length == 0) return;

        var lineDepartments = TrackedDepartmentByProductionLine();
        var stageDepartments = TrackedDepartmentBySubStage();
        var lineIds = pending.Select(entry => entry.Entity.ProductionLineId)
            .Where(id => !lineDepartments.ContainsKey(id))
            .Distinct()
            .ToArray();
        var stageIds = pending.Select(entry => entry.Entity.SubStageId)
            .Where(id => !stageDepartments.ContainsKey(id))
            .Distinct()
            .ToArray();
        var persistedLines = await ProductionLines.AsNoTracking()
            .Where(line => lineIds.Contains(line.Id))
            .Select(line => new { line.Id, line.DepartmentId })
            .ToArrayAsync(cancellationToken);
        foreach (var line in persistedLines)
            lineDepartments[line.Id] = line.DepartmentId;
        var persistedStages = await SubStages.AsNoTracking()
            .Where(stage => stageIds.Contains(stage.Id))
            .Select(stage => new { stage.Id, stage.DepartmentId })
            .ToArrayAsync(cancellationToken);
        foreach (var stage in persistedStages)
            stageDepartments[stage.Id] = stage.DepartmentId;

        ValidateProductModelStageDepartments(pending, lineDepartments, stageDepartments);
    }

    private Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ProductModelStage>[] PendingProductModelStages() =>
        ChangeTracker.Entries<ProductModelStage>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToArray();

    private Dictionary<Guid, Guid?> TrackedDepartmentByProductionLine() => ChangeTracker.Entries<ProductionLine>()
        .Where(entry => entry.State != EntityState.Deleted)
        .GroupBy(entry => entry.Entity.Id)
        .ToDictionary(group => group.Key, group => group.First().Entity.DepartmentId);

    private Dictionary<Guid, Guid> TrackedDepartmentBySubStage() => ChangeTracker.Entries<SubStage>()
        .Where(entry => entry.State != EntityState.Deleted)
        .GroupBy(entry => entry.Entity.Id)
        .ToDictionary(group => group.Key, group => group.First().Entity.DepartmentId);

    private static void ValidateProductModelStageDepartments(
        IReadOnlyCollection<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ProductModelStage>> pending,
        IReadOnlyDictionary<Guid, Guid?> lineDepartments,
        IReadOnlyDictionary<Guid, Guid> stageDepartments)
    {
        foreach (var entry in pending)
        {
            if (!lineDepartments.TryGetValue(entry.Entity.ProductionLineId, out var lineDepartmentId) || !lineDepartmentId.HasValue)
                throw new InvalidOperationException($"Cannot resolve an owning department for ProductionLine {entry.Entity.ProductionLineId}.");
            if (!stageDepartments.TryGetValue(entry.Entity.SubStageId, out var stageDepartmentId))
                throw new InvalidOperationException($"Cannot resolve the department for SubStage {entry.Entity.SubStageId}.");
            if (lineDepartmentId.Value != stageDepartmentId)
                throw new InvalidOperationException($"ProductModelStage {entry.Entity.Id} cannot link a line and stage from different departments.");
        }
    }
}
