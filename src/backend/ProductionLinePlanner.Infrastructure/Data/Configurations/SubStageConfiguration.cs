using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class SubStageConfiguration : IEntityTypeConfiguration<SubStage>
{
    public void Configure(EntityTypeBuilder<SubStage> builder)
    {
        builder.ToTable("SubStages", table =>
        {
            table.HasCheckConstraint("CK_SubStage_Capacity_NonNegative", "[Capacity] >= 0");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MainStageId).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Capacity).IsRequired();
        builder.Property(x => x.SequenceOrder).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.MainStageId, x.SequenceOrder }).IsUnique();

        builder.HasOne(x => x.MainStage)
            .WithMany(x => x.SubStages)
            .HasForeignKey(x => x.MainStageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.DefaultAssignments)
            .WithOne(x => x.SubStage)
            .HasForeignKey(x => x.SubStageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
