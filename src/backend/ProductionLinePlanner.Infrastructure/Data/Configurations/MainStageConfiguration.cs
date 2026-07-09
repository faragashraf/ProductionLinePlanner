using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class MainStageConfiguration : IEntityTypeConfiguration<MainStage>
{
    public void Configure(EntityTypeBuilder<MainStage> builder)
    {
        builder.ToTable("MainStages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ProductionLineId).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.SequenceOrder).IsRequired();
        builder.Property(x => x.IsCritical).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.ProductionLineId, x.SequenceOrder }).IsUnique();

        builder.HasOne(x => x.ProductionLine)
            .WithMany(x => x.MainStages)
            .HasForeignKey(x => x.ProductionLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.SubStages)
            .WithOne(x => x.MainStage)
            .HasForeignKey(x => x.MainStageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
