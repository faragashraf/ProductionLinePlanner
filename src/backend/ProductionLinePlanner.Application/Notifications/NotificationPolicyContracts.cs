using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Domain.Notifications;

namespace ProductionLinePlanner.Application.Notifications;

public sealed record NotificationEventContext(
    string EventKey,
    IReadOnlyDictionary<string, string> TokenValues,
    Guid? ActorUserId = null,
    Guid? CreatorUserId = null);

public sealed record NotificationRecipientContext(
    Guid? ActorUserId = null,
    Guid? CreatorUserId = null);

public sealed record NotificationPolicyDecision(
    string EventKey,
    bool IsEnabled,
    bool ShouldDispatch,
    NotificationSeverity Severity,
    NotificationSoundPolicy Sound,
    NotificationToastPolicy Toast,
    NotificationInboxPolicy Inbox,
    string? Title,
    string? Message,
    IReadOnlyCollection<Guid> RecipientUserIds);

public interface INotificationEventCatalog
{
    IReadOnlyList<NotificationEventDefinition> GetAll();

    NotificationEventDefinition? Find(string eventKey);
}

public interface INotificationTemplateResolver
{
    Result<IReadOnlyCollection<string>> ParseTokens(string template);

    Result<IReadOnlyCollection<string>> Validate(
        string template,
        IReadOnlyCollection<string> allowedTokens);

    Result<string> Resolve(
        string template,
        IReadOnlyCollection<string> allowedTokens,
        IReadOnlyDictionary<string, string> tokenValues);
}

public interface INotificationRecipientResolver
{
    Task<Result<IReadOnlyCollection<Guid>>> ResolveAsync(
        IReadOnlyCollection<NotificationRecipientRule> rules,
        NotificationRecipientContext context,
        CancellationToken cancellationToken = default);
}

public interface INotificationPolicyEngine
{
    Task<Result<NotificationPolicyDecision>> EvaluateAsync(
        NotificationPolicyDefinition policy,
        NotificationEventContext context,
        CancellationToken cancellationToken = default);
}

public interface INotificationPolicyAdminService
{
    NotificationPolicyStudioFoundationDto GetFoundation();

    Task<Result<IReadOnlyCollection<NotificationPolicyListItemDto>>> GetPoliciesAsync(
        CancellationToken cancellationToken = default);

    Task<Result<NotificationPolicyDetailsDto>> GetPolicyAsync(
        string eventKey,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationPolicyRecipientOptionsDto>> GetRecipientOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<Result<NotificationPolicyDetailsDto>> UpdatePolicyAsync(
        string eventKey,
        NotificationPolicyUpdateRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationPolicyDetailsDto>> ReplaceRecipientRulesAsync(
        string eventKey,
        NotificationPolicyRecipientRulesReplaceRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);
}

public interface INotificationPolicyCatalogReconciler
{
    Task<Result<int>> EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}
