using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;
public sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> b)
    {
        b.ToTable("ProductionOrders"); b.HasKey(x => x.Id); b.Property(x => x.ConcurrencyToken).IsConcurrencyToken(); b.Property(x => x.OrderNumber).HasMaxLength(80).IsRequired(); b.HasIndex(x => x.OrderNumber).IsUnique(); b.Property(x => x.PlannedQuantity).HasPrecision(18, 3); b.Property(x => x.Notes).HasMaxLength(1000); b.HasIndex(x => new { x.ProductionDate, x.Status });
        b.HasOne(x => x.ProductModel).WithMany().HasForeignKey(x => x.ProductModelId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ProductionLine).WithMany().HasForeignKey(x => x.ProductionLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
