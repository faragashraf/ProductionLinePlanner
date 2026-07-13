using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class ProductModelStageConfiguration : IEntityTypeConfiguration<ProductModelStage>
{
    public void Configure(EntityTypeBuilder<ProductModelStage> builder)
    {
        builder.ToTable("ProductModelStages", table =>
        {
            table.HasCheckConstraint("CK_ProductModelStage_PiecePrice_NonNegative", "[PiecePrice] >= 0");
            table.HasCheckConstraint("CK_ProductModelStage_StageOrder_Positive", "[StageOrder] > 0");
            table.HasCheckConstraint("CK_ProductModelStage_StandardSeconds_Positive",
                "[StandardSeconds] IS NULL OR [StandardSeconds] > 0");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ProductModelId).IsRequired();
        builder.Property(x => x.SubStageId).IsRequired();
        builder.Property(x => x.StageOrder).IsRequired();
        builder.Property(x => x.PiecePrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.StandardSeconds).HasColumnType("decimal(18,4)");
        builder.Property(x => x.CompensationMode).IsRequired();
        builder.Property(x => x.IsRequired).HasDefaultValue(true);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.EffectiveFrom);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.ProductModelId, x.SubStageId }).IsUnique();
        builder.HasIndex(x => new { x.ProductModelId, x.StageOrder }).IsUnique();

        builder.HasOne(x => x.ProductModel)
            .WithMany(x => x.Stages)
            .HasForeignKey(x => x.ProductModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.SubStage)
            .WithMany()
            .HasForeignKey(x => x.SubStageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
