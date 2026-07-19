using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Notifications;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RecipientUserId).IsRequired();
        builder.Property(x => x.SenderUserId);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Message).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.EventKey).HasMaxLength(NotificationPolicy.MaxEventKeyLength);
        builder.Property(x => x.Severity).HasDefaultValue(NotificationSeverity.Information);
        builder.Property(x => x.IsRead).HasDefaultValue(false);
        builder.Property(x => x.ReadAtUtc);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.RecipientUserId);
        builder.HasIndex(x => x.RelatedEntityId);
        builder.HasIndex(x => x.EventKey);

        builder.HasOne(x => x.RecipientUser)
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SenderUser)
            .WithMany()
            .HasForeignKey(x => x.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
