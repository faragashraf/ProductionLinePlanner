using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class StageReadinessSnapshotConfiguration : IEntityTypeConfiguration<StageReadinessSnapshot>
{
    public void Configure(EntityTypeBuilder<StageReadinessSnapshot> builder)
    {
        builder.ToTable("StageReadinessSnapshots", table =>
        {
            table.HasCheckConstraint("CK_StageReadinessSnapshot_RequiredNonNegative", "[RequiredWorkers] >= 0");
            table.HasCheckConstraint("CK_StageReadinessSnapshot_PresentNonNegative", "[PresentWorkers] >= 0");
            table.HasCheckConstraint("CK_StageReadinessSnapshot_LateNonNegative", "[LateWorkers] >= 0");
            table.HasCheckConstraint("CK_StageReadinessSnapshot_AbsentNonNegative", "[AbsentWorkers] >= 0");
            table.HasCheckConstraint("CK_StageReadinessSnapshot_UnassignedNonNegative", "[UnassignedWorkers] >= 0");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ScopeType).IsRequired().HasMaxLength(60);
        builder.Property(x => x.ScopeEntityId).IsRequired();
        builder.Property(x => x.CalculatedAtUtc).IsRequired();
        builder.Property(x => x.RequiredWorkers).IsRequired();
        builder.Property(x => x.PresentWorkers).IsRequired();
        builder.Property(x => x.LateWorkers).IsRequired();
        builder.Property(x => x.AbsentWorkers).IsRequired();
        builder.Property(x => x.UnassignedWorkers).IsRequired();
        builder.Property(x => x.ReadinessPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.ReadinessStatus)
            .HasConversion<string>()
            .HasMaxLength(40)
            .HasDefaultValue(ReadinessStatus.Unknown);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.ScopeType, x.ScopeEntityId, x.CalculatedAtUtc });
    }
}
