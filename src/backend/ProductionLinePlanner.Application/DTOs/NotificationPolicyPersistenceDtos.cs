namespace ProductionLinePlanner.Application.DTOs;

public sealed record NotificationPolicyListItemDto
{
    public required string EventKey { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsEnabled { get; init; }
    public required string Severity { get; init; }
    public required bool IsToastEnabled { get; init; }
    public required bool IsInboxEnabled { get; init; }
    public required bool IsSoundEnabled { get; init; }
    public required string UpdatedAtUtc { get; init; }
}

public sealed record NotificationPolicyDetailsDto
{
    public required string EventKey { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyCollection<string> AllowedTokens { get; init; }
    public required bool IsEnabled { get; init; }
    public required string Severity { get; init; }
    public required bool IsToastEnabled { get; init; }
    public required bool IsInboxEnabled { get; init; }
    public required bool IsSoundEnabled { get; init; }
    public string? SoundKey { get; init; }
    public required string TitleTemplateAr { get; init; }
    public required string MessageTemplateAr { get; init; }
    public required string RowVersion { get; init; }
    public required IReadOnlyCollection<NotificationPolicyRecipientRuleDto> RecipientRules { get; init; }
    public required string UpdatedAtUtc { get; init; }
}

public sealed record NotificationPolicyRecipientRuleDto
{
    public required string Id { get; init; }
    public required string RecipientKind { get; init; }
    public string? UserId { get; init; }
    public string? RoleId { get; init; }
    public string? PermissionKey { get; init; }
    public string? CapabilityKey { get; init; }
    public required bool IsExcludeActor { get; init; }
    public required int SortOrder { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record NotificationPolicyUpdateRequest
{
    public required bool IsEnabled { get; init; }
    public required string Severity { get; init; }
    public required bool IsToastEnabled { get; init; }
    public required bool IsInboxEnabled { get; init; }
    public required bool IsSoundEnabled { get; init; }
    public string? SoundKey { get; init; }
    public required string TitleTemplateAr { get; init; }
    public required string MessageTemplateAr { get; init; }
    public required string RowVersion { get; init; }
    public required IReadOnlyCollection<NotificationPolicyRecipientRuleUpdateRequest> RecipientRules { get; init; }
}

public sealed record NotificationPolicyRecipientRuleUpdateRequest
{
    public required string RecipientKind { get; init; }
    public string? UserId { get; init; }
    public string? RoleId { get; init; }
    public string? PermissionKey { get; init; }
    public string? CapabilityKey { get; init; }
    public required bool IsExcludeActor { get; init; }
    public required int SortOrder { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record NotificationPolicyRecipientRulesReplaceRequest
{
    public required string RowVersion { get; init; }
    public required IReadOnlyCollection<NotificationPolicyRecipientRuleUpdateRequest> RecipientRules { get; init; }
}

public sealed record NotificationPolicyRecipientOptionsDto
{
    public required IReadOnlyCollection<NotificationPolicyRecipientUserOptionDto> Users { get; init; }
    public required IReadOnlyCollection<NotificationPolicyRecipientRoleOptionDto> Roles { get; init; }
    public required IReadOnlyCollection<NotificationPolicyRecipientPermissionOptionDto> Permissions { get; init; }
    public required IReadOnlyCollection<string> CapabilityGroups { get; init; }
}

public sealed record NotificationPolicyRecipientUserOptionDto(string Id, string FullName, string Email);

public sealed record NotificationPolicyRecipientRoleOptionDto(string Id, string Name);

public sealed record NotificationPolicyRecipientPermissionOptionDto(string Name, string Capability, string? DescriptionAr);
