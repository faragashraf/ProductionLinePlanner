using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;
using System.Collections.Generic;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUsers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Email).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PasswordHash).IsRequired().HasMaxLength(500);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.PreferredLanguage).IsRequired().HasMaxLength(10);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.Email).IsUnique();

        builder.HasMany(x => x.Roles)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "UserRoles",
                right => right
                    .HasOne<AppRole>()
                    .WithMany()
                    .HasForeignKey("AppRoleId")
                    .OnDelete(DeleteBehavior.Restrict),
                left => left
                    .HasOne<AppUser>()
                    .WithMany()
                    .HasForeignKey("AppUserId")
                    .OnDelete(DeleteBehavior.Restrict),
                join =>
                {
                    join.ToTable("UserRoles");
                    join.HasKey("AppUserId", "AppRoleId");
                    join.HasIndex("AppUserId");
                    join.HasIndex("AppRoleId");
                });

        builder.HasMany(x => x.PermissionOverrides)
            .WithOne(x => x.AppUser)
            .HasForeignKey(x => x.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
