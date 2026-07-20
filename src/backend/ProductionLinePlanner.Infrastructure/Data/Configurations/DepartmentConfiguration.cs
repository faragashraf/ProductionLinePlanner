using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FactoryId).IsRequired();
        builder.Property(x => x.Code).IsRequired().HasMaxLength(80).UseCollation("SQL_Latin1_General_CP1_CI_AS");
        builder.Property(x => x.NameAr).IsRequired().HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.SequenceOrder).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.FactoryId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.FactoryId, x.SequenceOrder });

        builder.HasOne(x => x.Factory)
            .WithMany(x => x.Departments)
            .HasForeignKey(x => x.FactoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ProductionLines)
            .WithOne(x => x.Department)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
