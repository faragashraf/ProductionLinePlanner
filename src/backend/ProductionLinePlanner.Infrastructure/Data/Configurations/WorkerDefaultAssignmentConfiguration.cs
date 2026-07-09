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
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.WorkerId).IsUnique().HasFilter("[IsActive] = 1");

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
