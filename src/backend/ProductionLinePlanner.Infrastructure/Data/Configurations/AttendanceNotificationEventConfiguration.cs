using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class AttendanceNotificationEventConfiguration : IEntityTypeConfiguration<AttendanceNotificationEvent>
{
    public void Configure(EntityTypeBuilder<AttendanceNotificationEvent> builder)
    {
        builder.ToTable("AttendanceNotificationEvents");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.WorkerName).IsRequired().HasMaxLength(200);
        builder.Property(item => item.EmployeeCode).IsRequired().HasMaxLength(120);
        builder.Property(item => item.AttendanceType).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.Source).IsRequired().HasMaxLength(60);
        builder.Property(item => item.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(item => item.LastErrorCode).HasMaxLength(100);
        builder.Property(item => item.CreatedAtUtc).IsRequired();

        builder.HasIndex(item => item.IdempotencyKey).IsUnique();
        builder.HasIndex(item => new { item.ProcessedAtUtc, item.CreatedAtUtc });
        builder.HasOne(item => item.AttendanceRecord)
            .WithMany()
            .HasForeignKey(item => item.AttendanceRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
