using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class WorkerTemporaryAssignmentConfiguration : IEntityTypeConfiguration<WorkerTemporaryAssignment>
{
    public void Configure(EntityTypeBuilder<WorkerTemporaryAssignment> builder)
    {
        builder.ToTable("WorkerTemporaryAssignments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkerId).IsRequired();
        builder.Property(x => x.FromSubStageId).IsRequired();
        builder.Property(x => x.ToSubStageId).IsRequired();
        builder.Property(x => x.StartAtUtc).IsRequired();
        builder.Property(x => x.EndAtUtc).IsRequired();
        builder.Property(x => x.AssignedByUserId).IsRequired();
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(300);
        builder.Property(x => x.ReplacementForWorkerId);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(50);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.WorkerId);
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
