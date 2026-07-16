using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class WorkerTemporaryAssignmentConfiguration : IEntityTypeConfiguration<WorkerTemporaryAssignment>
{
    public void Configure(EntityTypeBuilder<WorkerTemporaryAssignment> builder)
    {
        builder.ToTable("WorkerTemporaryAssignments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkerId).IsRequired();
        builder.Property(x => x.FromSubStageId);
        builder.Property(x => x.ToSubStageId).IsRequired();
        builder.Property(x => x.StartAtUtc).IsRequired();
        builder.Property(x => x.EndAtUtc).IsRequired();
        builder.Property(x => x.AssignedByUserId).IsRequired();
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(300);
        builder.Property(x => x.ReplacementForWorkerId);
        // Existing rows were created under the original move-only behavior.
        // The database default therefore preserves that behavior during the
        // non-destructive migration, while new requests choose explicitly.
        builder.Property(x => x.ParticipationMode).HasConversion<string>().IsRequired().HasMaxLength(40)
            .HasDefaultValue(TemporaryAssignmentMode.TemporaryMove);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(50);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired().IsConcurrencyToken();

        builder.HasIndex(x => x.WorkerId);
        // Supports the serializable overlap predicate with a worker-specific key range.
        builder.HasIndex(x => new { x.WorkerId, x.Status, x.StartAtUtc, x.EndAtUtc });
        builder.HasIndex(x => new { x.FromSubStageId, x.ToSubStageId });

        builder.HasOne(x => x.Worker)
            .WithMany(x => x.TemporaryAssignments)
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FromSubStage)
            .WithMany()
            .HasForeignKey(x => x.FromSubStageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ToSubStage)
            .WithMany()
            .HasForeignKey(x => x.ToSubStageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
