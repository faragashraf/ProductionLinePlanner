using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("AttendanceRecords");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkerId).IsRequired();
        builder.Property(x => x.AttendanceTimeUtc).IsRequired();
        builder.Property(x => x.AttendanceStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(AttendanceStatus.Unassigned);
        builder.Property(x => x.Source).HasMaxLength(60);
        builder.Property(x => x.SourceRawId).HasMaxLength(120);
        builder.Property(x => x.AttendanceUserId).HasMaxLength(120);
        builder.Property(x => x.BadgeNumber).HasMaxLength(120);
        builder.Property(x => x.SourcePayload).HasMaxLength(4000);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.WorkerId, x.AttendanceTimeUtc });

        builder.HasOne(x => x.Worker)
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
