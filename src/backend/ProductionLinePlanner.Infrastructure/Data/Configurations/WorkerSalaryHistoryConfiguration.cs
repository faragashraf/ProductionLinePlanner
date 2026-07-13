using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class WorkerSalaryHistoryConfiguration : IEntityTypeConfiguration<WorkerSalaryHistory>
{
    public void Configure(EntityTypeBuilder<WorkerSalaryHistory> builder)
    {
        builder.ToTable("WorkerSalaryHistories");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkerId).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).HasDefaultValue("EGP");
        builder.Property(x => x.EffectiveFrom).IsRequired();
        builder.Property(x => x.EffectiveTo);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy);
        builder.Property(x => x.UpdatedBy);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.WorkerId, x.EffectiveFrom });
        builder.HasIndex(x => new { x.WorkerId, x.EffectiveTo });
        builder.HasIndex(x => new { x.WorkerId, x.EffectiveTo })
            .HasFilter("[EffectiveTo] IS NULL")
            .HasDatabaseName("IX_WorkerSalaryHistories_Current");

        builder.HasCheckConstraint("CK_WorkerSalaryHistory_Amount_NonNegative", "[Amount] >= 0");
        builder.HasCheckConstraint("CK_WorkerSalaryHistory_EffectiveRange",
            "[EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");

        builder.HasOne(x => x.Worker)
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
