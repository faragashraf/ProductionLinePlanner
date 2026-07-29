using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class AttendanceSyncStateConfiguration : IEntityTypeConfiguration<AttendanceSyncState>
{
    public void Configure(EntityTypeBuilder<AttendanceSyncState> builder)
    {
        builder.ToTable("AttendanceSyncStates");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.SourceName).IsRequired().HasMaxLength(60);
        builder.Property(item => item.OperationalDate).HasColumnType("date").IsRequired();
        builder.Property(item => item.LastAttemptAtUtc).IsRequired();
        builder.Property(item => item.LastErrorCode).HasMaxLength(100);
        builder.HasIndex(item => new { item.SourceName, item.OperationalDate }).IsUnique();
    }
}
