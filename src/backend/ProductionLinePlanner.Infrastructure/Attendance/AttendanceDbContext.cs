using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Infrastructure.Attendance.Entities;

namespace ProductionLinePlanner.Infrastructure.Attendance;

public sealed class AttendanceDbContext : DbContext
{
    private readonly AttendanceSourceOptions _options;

    public AttendanceDbContext(DbContextOptions<AttendanceDbContext> options, IOptions<AttendanceSourceOptions> sourceOptions)
        : base(options)
    {
        _options = sourceOptions.Value;
    }

    public DbSet<AttendanceSourceUserInfo> UserInfos => Set<AttendanceSourceUserInfo>();
    public DbSet<AttendanceSourceCheckInOut> CheckInOuts => Set<AttendanceSourceCheckInOut>();
    public DbSet<AttendanceSourceDepartment> Departments => Set<AttendanceSourceDepartment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUserInfo(modelBuilder);
        ConfigureCheckInOut(modelBuilder);
        ConfigureDepartment(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    private void ConfigureUserInfo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AttendanceSourceUserInfo>(entity =>
        {
            entity.HasNoKey();
            entity.ToTable(_options.UserInfoTable);

            entity.Property(x => x.UserId).HasColumnName("USERID");
            entity.Property(x => x.BadgeNumber).HasColumnName("BADGENUMBER").HasMaxLength(120);
            entity.Property(x => x.Name).HasColumnName("Name").HasMaxLength(200);
            entity.Property(x => x.DefaultDeptId).HasColumnName("DEFAULTDEPTID");
        });
    }

    private void ConfigureCheckInOut(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AttendanceSourceCheckInOut>(entity =>
        {
            entity.HasNoKey();
            entity.ToTable(_options.CheckInOutTable);

            entity.Property(x => x.UserId).HasColumnName("USERID");
            entity.Property(x => x.CheckTime).HasColumnName("CHECKTIME");
            entity.Property(x => x.CheckType).HasColumnName("CHECKTYPE").HasMaxLength(20);
        });
    }

    private void ConfigureDepartment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AttendanceSourceDepartment>(entity =>
        {
            entity.HasNoKey();
            entity.ToTable(_options.DepartmentsTable ?? "DEPARTMENTS");

            entity.Property(x => x.DepartmentId).HasColumnName("DEPTID");
            entity.Property(x => x.Name).HasColumnName("DEPTNAME").HasMaxLength(200);
        });
    }
}
