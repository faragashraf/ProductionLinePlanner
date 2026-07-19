namespace ProductionLinePlanner.Domain.Notifications;

public enum NotificationSeverity
{
    Information,
    Success,
    Warning,
    Critical
}

public enum NotificationRecipientKind
{
    User,
    Role,
    Permission,
    CapabilityGroup,
    Creator,
    ExcludeActor
}

public sealed record NotificationSoundPolicy(bool Enabled);

public sealed record NotificationToastPolicy(bool Enabled);

public sealed record NotificationInboxPolicy(bool Enabled);

public sealed record NotificationRecipientRule(
    NotificationRecipientKind Kind,
    Guid? SubjectId = null,
    string? Value = null);

public sealed record NotificationPolicyDefinition(
    string EventKey,
    bool IsEnabled,
    NotificationSeverity Severity,
    NotificationSoundPolicy Sound,
    NotificationToastPolicy Toast,
    NotificationInboxPolicy Inbox,
    string TitleTemplate,
    string MessageTemplate,
    IReadOnlyCollection<NotificationRecipientRule> RecipientRules);

public sealed record NotificationEventDefinition(
    string Key,
    string DisplayName,
    IReadOnlyCollection<string> AllowedTokens,
    NotificationPolicyDefinition DefaultPolicy);
