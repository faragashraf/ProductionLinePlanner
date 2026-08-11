using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> builder)
    {
        builder.ToTable("AppRoles");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Role)
            // Legacy databases may contain a retired catalog value (for example
            // "Hr"). Treat it as a custom role rather than failing every query
            // that includes the user's roles.
            .HasConversion(new ValueConverter<UserRole?, string?>(
                role => role.HasValue ? role.Value.ToString() : null,
                value => ParseKnownSystemRole(value)))
            .HasMaxLength(60)
            .IsRequired(false);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.IsSystemRole).HasDefaultValue(false);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.Role).IsUnique();
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasMany(x => x.Permissions)
            .WithOne(x => x.AppRole)
            .HasForeignKey(x => x.AppRoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static UserRole? ParseKnownSystemRole(string? value) =>
        Enum.TryParse<UserRole>(value, ignoreCase: true, out var role) ? role : null;
}
