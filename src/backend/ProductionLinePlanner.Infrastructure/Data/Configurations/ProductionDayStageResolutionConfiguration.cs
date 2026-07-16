using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class ProductionDayStageResolutionConfiguration : IEntityTypeConfiguration<ProductionDayStageResolution>
{
    public void Configure(EntityTypeBuilder<ProductionDayStageResolution> builder)
    {
        builder.ToTable("ProductionDayStageResolutions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.ResolvedBy).IsRequired();
        builder.Property(x => x.ResolvedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.ProductionOrderId, x.ProductModelStageId }).IsUnique();
        builder.HasOne(x => x.ProductionOrder).WithMany(x => x.StageResolutions).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ProductModelStage).WithMany().HasForeignKey(x => x.ProductModelStageId).OnDelete(DeleteBehavior.Restrict);
    }
}
