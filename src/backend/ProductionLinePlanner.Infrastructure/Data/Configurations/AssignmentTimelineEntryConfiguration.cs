using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class AssignmentTimelineEntryConfiguration : IEntityTypeConfiguration<AssignmentTimelineEntry>
{
    public void Configure(EntityTypeBuilder<AssignmentTimelineEntry> builder)
    {
        builder.ToTable("AssignmentTimelineEntries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkerId).IsRequired();
        builder.Property(x => x.FromSubStageId);
        builder.Property(x => x.ToSubStageId);
        builder.Property(x => x.AssignmentType).IsRequired().HasMaxLength(40);
        builder.Property(x => x.ActionType).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.StartAtUtc).IsRequired();
        builder.Property(x => x.EndAtUtc);
        builder.Property(x => x.PerformedByUserId).IsRequired();
        builder.Property(x => x.IsAutomatic).HasDefaultValue(false);
        builder.Property(x => x.RelatedTemporaryAssignmentId);
        builder.Property(x => x.ReplacementForWorkerId);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.WorkerId);
        builder.HasIndex(x => x.PerformedByUserId);
        builder.HasIndex(x => x.StartAtUtc);
        builder.HasIndex(x => new { x.WorkerId, x.StartAtUtc });

        builder.HasOne(x => x.Worker)
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.PerformedByUser)
            .WithMany()
            .HasForeignKey(x => x.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

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
