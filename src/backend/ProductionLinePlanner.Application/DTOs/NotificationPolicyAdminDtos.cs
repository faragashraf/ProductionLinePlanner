namespace ProductionLinePlanner.Application.DTOs;

public sealed record NotificationPolicyStudioFoundationDto
{
    public required IReadOnlyCollection<NotificationPolicyCatalogItemDto> Events { get; init; }
    public required IReadOnlyCollection<string> Severities { get; init; }
    public required IReadOnlyCollection<string> RecipientRuleKinds { get; init; }
    public required NotificationSoundFoundationDto Sound { get; init; }
    public required IReadOnlyCollection<string> DeferredChannels { get; init; }
    public bool CanCreateEvents => false;
    public bool IsPersistenceAvailable => true;
}

public sealed record NotificationPolicyCatalogItemDto
{
    public required string EventKey { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyCollection<string> AllowedTokens { get; init; }
    public required NotificationPolicyDraftDto DefaultPolicy { get; init; }
}

public sealed record NotificationPolicyDraftDto
{
    public required bool IsEnabled { get; init; }
    public required string Severity { get; init; }
    public required bool SoundEnabled { get; init; }
    public required bool ToastEnabled { get; init; }
    public required bool InboxEnabled { get; init; }
    public bool BrowserEnabled { get; init; }
    public required string TitleTemplate { get; init; }
    public required string MessageTemplate { get; init; }
    public required IReadOnlyCollection<NotificationRecipientRuleDto> RecipientRules { get; init; }
}

public sealed record NotificationRecipientRuleDto
{
    public required string Kind { get; init; }
    public Guid? SubjectId { get; init; }
    public string? Value { get; init; }
}

public sealed record NotificationSoundFoundationDto
{
    public string SoundKey => "default";
    public bool SupportsEnableDisable => true;
    public bool SupportsMultipleSounds => false;
    public bool SupportsVolume => false;
    public bool SupportsUserPreferences => false;
}
