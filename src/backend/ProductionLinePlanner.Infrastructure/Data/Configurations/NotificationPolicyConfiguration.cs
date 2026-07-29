using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class NotificationPolicyConfiguration : IEntityTypeConfiguration<NotificationPolicy>
{
    public void Configure(EntityTypeBuilder<NotificationPolicy> builder)
    {
        builder.ToTable("NotificationPolicies", table =>
        {
            table.HasCheckConstraint(
                "CK_NotificationPolicies_SoundKey",
                "([IsSoundEnabled] = 0 AND [SoundKey] IS NULL) OR ([IsSoundEnabled] = 1 AND [SoundKey] = 'default')");
        });
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).ValueGeneratedNever();
        builder.Property(policy => policy.EventKey).IsRequired().HasMaxLength(NotificationPolicy.MaxEventKeyLength);
        builder.Property(policy => policy.Severity).IsRequired();
        builder.Property(policy => policy.SoundKey).HasMaxLength(NotificationPolicy.MaxSoundKeyLength);
        builder.Property(policy => policy.TitleTemplateAr).IsRequired().HasMaxLength(NotificationPolicy.MaxTitleTemplateLength);
        builder.Property(policy => policy.MessageTemplateAr).IsRequired().HasMaxLength(NotificationPolicy.MaxMessageTemplateLength);
        builder.Property(policy => policy.CreatedAtUtc).IsRequired();
        builder.Property(policy => policy.UpdatedAtUtc).IsRequired();
        builder.Property(policy => policy.RowVersion).IsRowVersion();

        builder.HasIndex(policy => policy.EventKey).IsUnique();
        builder.HasIndex(policy => policy.UpdatedAtUtc);

        builder.HasOne(policy => policy.CreatedByUser)
            .WithMany()
            .HasForeignKey(policy => policy.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(policy => policy.UpdatedByUser)
            .WithMany()
            .HasForeignKey(policy => policy.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
