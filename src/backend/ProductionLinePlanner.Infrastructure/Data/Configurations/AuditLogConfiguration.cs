using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ActorUserId).IsRequired();
        builder.Property(x => x.ActionType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(x => x.EntityType).IsRequired().HasMaxLength(200);
        builder.Property(x => x.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.EntityBeforeJson).HasMaxLength(4000);
        builder.Property(x => x.EntityAfterJson).HasMaxLength(4000);
        builder.Property(x => x.RequestMeta).HasMaxLength(4000);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.ActorUserId);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });

        builder.Property(x => x.ActorUserId).IsRequired();

        builder.HasOne(x => x.ActorUser)
            .WithMany()
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
