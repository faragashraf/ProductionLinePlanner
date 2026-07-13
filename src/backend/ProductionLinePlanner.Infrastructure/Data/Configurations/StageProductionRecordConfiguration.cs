using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;
public sealed class StageProductionRecordConfiguration : IEntityTypeConfiguration<StageProductionRecord>
{
    public void Configure(EntityTypeBuilder<StageProductionRecord> b)
    {
        b.ToTable("StageProductionRecords"); b.HasKey(x => x.Id); b.Property(x => x.ConcurrencyToken).IsConcurrencyToken(); b.Property(x => x.ProducedQuantity).HasPrecision(18, 3); b.Property(x => x.AcceptedQuantity).HasPrecision(18, 3); b.Property(x => x.RejectedQuantity).HasPrecision(18, 3); b.Property(x => x.SnapshotPiecePrice).HasPrecision(18, 4); b.Property(x => x.SnapshotStandardSeconds).HasPrecision(18, 2); b.Property(x => x.TotalWorkerEarnings).HasPrecision(18, 4); b.Property(x => x.SnapshotStageCode).HasMaxLength(80).IsRequired(); b.Property(x => x.SnapshotStageName).HasMaxLength(200).IsRequired(); b.Property(x => x.SnapshotProductModelCode).HasMaxLength(80).IsRequired(); b.Property(x => x.SnapshotProductModelName).HasMaxLength(200).IsRequired(); b.Property(x => x.Notes).HasMaxLength(1000); b.HasIndex(x => new { x.ProductionDate, x.Status }); b.HasIndex(x => new { x.ProductionOrderId, x.ProductModelStageId }); b.HasIndex(x => new { x.ProductionOrderId, x.ClientRequestId }).IsUnique();
        b.HasOne(x => x.ProductionOrder).WithMany(x => x.StageProductionRecords).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ProductModelStage).WithMany().HasForeignKey(x => x.ProductModelStageId).OnDelete(DeleteBehavior.Restrict);
    }
}
