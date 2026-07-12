using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> builder)
    {
        builder.ToTable("UserPermissionOverrides");
        builder.HasKey(x => new { x.AppUserId, x.PermissionId });

        builder.Property(x => x.AppUserId).IsRequired();
        builder.Property(x => x.PermissionId).IsRequired();
        builder.Property(x => x.Effect)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.CreatedByUserId);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.HasIndex(x => x.Effect);
    }
}
