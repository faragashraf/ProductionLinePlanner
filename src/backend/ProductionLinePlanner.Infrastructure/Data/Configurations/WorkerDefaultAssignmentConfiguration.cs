using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class WorkerDefaultAssignmentConfiguration : IEntityTypeConfiguration<WorkerDefaultAssignment>
{
    public void Configure(EntityTypeBuilder<WorkerDefaultAssignment> builder)
    {
        builder.ToTable("WorkerDefaultAssignments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkerId).IsRequired();
        builder.Property(x => x.SubStageId).IsRequired();
        builder.Property(x => x.AssignedAt).IsRequired();
        builder.Property(x => x.AssignedByUserId).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.Reason).HasMaxLength(250);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        // Current assignment is operational state.  Treat its update timestamp as
        // an optimistic concurrency token so two managers cannot silently
        // overwrite or remove the same active assignment.
        builder.Property(x => x.UpdatedAtUtc).IsRequired().IsConcurrencyToken();

        // A worker can participate in multiple stages.  The active uniqueness
        // boundary is one worker per stage, not one worker for all stages.
        builder.HasIndex(x => new { x.WorkerId, x.SubStageId }).IsUnique().HasFilter("[IsActive] = 1");

        builder.HasOne(x => x.Worker)
            .WithMany(x => x.DefaultAssignmentHistory)
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.SubStage)
            .WithMany(x => x.DefaultAssignments)
            .HasForeignKey(x => x.SubStageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
