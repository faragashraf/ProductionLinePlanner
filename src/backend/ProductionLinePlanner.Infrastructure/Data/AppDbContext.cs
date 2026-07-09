using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Factory> Factories => Set<Factory>();
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
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppRole> AppRoles => Set<AppRole>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
