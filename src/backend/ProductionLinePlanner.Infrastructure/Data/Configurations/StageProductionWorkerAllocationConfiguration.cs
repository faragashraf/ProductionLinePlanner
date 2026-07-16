using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;
public sealed class StageProductionWorkerAllocationConfiguration : IEntityTypeConfiguration<StageProductionWorkerAllocation>
{
    public void Configure(EntityTypeBuilder<StageProductionWorkerAllocation> b)
    {
        b.ToTable("StageProductionWorkerAllocations"); b.HasKey(x => x.Id); b.Property(x => x.Percentage).HasPrecision(9, 4); b.Property(x => x.FixedAmount).HasPrecision(18, 4); b.Property(x => x.InputQuantity).HasPrecision(18, 3); b.Property(x => x.EquivalentQuantity).HasPrecision(18, 3); b.Property(x => x.CalculatedEarning).HasPrecision(18, 4); b.Property(x => x.SnapshotWorkerCode).HasMaxLength(80).IsRequired(); b.Property(x => x.SnapshotWorkerName).HasMaxLength(200).IsRequired(); b.Property(x => x.Notes).HasMaxLength(500); b.Property(x => x.ManualOverrideReason).HasMaxLength(500); b.HasIndex(x => new { x.StageProductionRecordId, x.WorkerId }).IsUnique(); b.HasIndex(x => x.WorkerId); b.HasOne(x => x.StageProductionRecord).WithMany(x => x.WorkerAllocations).HasForeignKey(x => x.StageProductionRecordId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Worker).WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
    }
}
