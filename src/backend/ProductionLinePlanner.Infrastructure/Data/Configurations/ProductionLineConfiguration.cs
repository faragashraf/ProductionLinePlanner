using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class ProductionLineConfiguration : IEntityTypeConfiguration<ProductionLine>
{
    public void Configure(EntityTypeBuilder<ProductionLine> builder)
    {
        builder.ToTable("ProductionLines", table => table.HasTrigger("TR_ProductionLines_ProductModelStageDepartmentGuard"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FactoryId).IsRequired();
        builder.Property(x => x.DepartmentId).IsRequired(false);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.LineCode).HasMaxLength(80);
        builder.Property(x => x.SequenceOrder).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.FactoryId, x.LineCode })
            .IsUnique()
            .HasFilter("[LineCode] IS NOT NULL");
        builder.HasIndex(x => x.DepartmentId);

        builder.HasOne(x => x.Factory)
            .WithMany(x => x.ProductionLines)
            .HasForeignKey(x => x.FactoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Department)
            .WithMany(x => x.ProductionLines)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
