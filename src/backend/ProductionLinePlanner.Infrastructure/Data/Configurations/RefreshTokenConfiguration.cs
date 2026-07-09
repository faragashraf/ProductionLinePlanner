using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AppUserId).IsRequired();
        builder.Property(x => x.TokenHash)
            .IsRequired()
            .HasMaxLength(128);
        builder.Property(x => x.IsRevoked).HasDefaultValue(false);
        builder.Property(x => x.RevokedAtUtc);
        builder.Property(x => x.RevokedReason).HasMaxLength(200);
        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.LastUsedAtUtc);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.AppUserId, x.IsRevoked, x.ExpiresAtUtc });

        builder.HasOne(x => x.AppUser)
            .WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
