using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Data.Configurations;

public sealed class NotificationPolicyRecipientRuleConfiguration : IEntityTypeConfiguration<NotificationPolicyRecipientRule>
{
    public void Configure(EntityTypeBuilder<NotificationPolicyRecipientRule> builder)
    {
        builder.ToTable("NotificationPolicyRecipientRules", table =>
        {
            table.HasCheckConstraint(
                "CK_NotificationPolicyRecipientRules_Target",
                "([RecipientKind] = 0 AND [UserId] IS NOT NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR " +
                "([RecipientKind] = 1 AND [UserId] IS NULL AND [RoleId] IS NOT NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR " +
                "([RecipientKind] = 2 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NOT NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR " +
                "([RecipientKind] = 3 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NOT NULL AND [IsExcludeActor] = 0) OR " +
                "([RecipientKind] = 4 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR " +
                "([RecipientKind] = 5 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 1) OR " +
                "([RecipientKind] = 6 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0)");
        });
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).ValueGeneratedNever();
        builder.Property(rule => rule.PermissionKey).HasMaxLength(NotificationPolicyRecipientRule.MaxKeyLength);
        builder.Property(rule => rule.CapabilityKey).HasMaxLength(NotificationPolicyRecipientRule.MaxKeyLength);
        builder.Property(rule => rule.CreatedAtUtc).IsRequired();
        builder.Property(rule => rule.UpdatedAtUtc).IsRequired();

        builder.HasIndex(rule => new { rule.NotificationPolicyId, rule.SortOrder }).IsUnique();
        builder.HasIndex(rule => rule.UserId);
        builder.HasIndex(rule => rule.RoleId);

        builder.HasOne(rule => rule.NotificationPolicy)
            .WithMany(policy => policy.RecipientRules)
            .HasForeignKey(rule => rule.NotificationPolicyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(rule => rule.User)
            .WithMany()
            .HasForeignKey(rule => rule.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.Role)
            .WithMany()
            .HasForeignKey(rule => rule.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
