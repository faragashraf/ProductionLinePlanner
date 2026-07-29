using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Domain.Notifications;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class NotificationPolicyEngine(
    INotificationEventCatalog eventCatalog,
    INotificationTemplateResolver templateResolver,
    INotificationRecipientResolver recipientResolver) : INotificationPolicyEngine
{
    public async Task<Result<NotificationPolicyDecision>> EvaluateAsync(
        NotificationPolicyDefinition policy,
        NotificationEventContext context,
        CancellationToken cancellationToken = default)
    {
        if (policy is null || context is null)
        {
            return Failure("NotificationPolicyRequired", "A notification policy and event context are required.");
        }

        var eventDefinition = eventCatalog.Find(policy.EventKey);
        if (eventDefinition is null)
        {
            return Failure("UnknownNotificationEvent", "The notification event is not in the product catalog.");
        }

        if (!eventDefinition.Key.Equals(context.EventKey?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Failure("NotificationEventMismatch", "The policy and event context must reference the same event.");
        }

        if (!Enum.IsDefined(policy.Severity))
        {
            return Failure("InvalidNotificationSeverity", "The notification severity is not supported.");
        }

        if (policy.Sound is null || policy.Toast is null || policy.Inbox is null || policy.Browser is null || policy.RecipientRules is null)
        {
            return Failure("InvalidNotificationPolicy", "Channel policies and recipient rules are required.");
        }

        if (!policy.IsEnabled)
        {
            return Result<NotificationPolicyDecision>.Success(new NotificationPolicyDecision(
                eventDefinition.Key,
                IsEnabled: false,
                ShouldDispatch: false,
                policy.Severity,
                policy.Sound,
                policy.Toast,
                policy.Inbox,
                policy.Browser,
                Title: null,
                Message: null,
                RecipientUserIds: []));
        }

        var titleResult = templateResolver.Resolve(
            policy.TitleTemplate,
            eventDefinition.AllowedTokens,
            context.TokenValues);
        if (titleResult.IsFailure)
        {
            return Result<NotificationPolicyDecision>.Failure(titleResult.Error!);
        }

        var messageResult = templateResolver.Resolve(
            policy.MessageTemplate,
            eventDefinition.AllowedTokens,
            context.TokenValues);
        if (messageResult.IsFailure)
        {
            return Result<NotificationPolicyDecision>.Failure(messageResult.Error!);
        }

        if (titleResult.Value!.Length > 200 || messageResult.Value!.Length > 2000)
        {
            return Failure(
                "RenderedNotificationTooLong",
                "Rendered notification titles cannot exceed 200 characters and messages cannot exceed 2000 characters.");
        }

        var recipientResult = await recipientResolver.ResolveAsync(
            policy.RecipientRules,
            new NotificationRecipientContext(context.ActorUserId, context.CreatorUserId),
            cancellationToken);
        if (recipientResult.IsFailure)
        {
            return Result<NotificationPolicyDecision>.Failure(recipientResult.Error!);
        }

        var hasDeliveryChannel = policy.Sound.Enabled || policy.Toast.Enabled || policy.Inbox.Enabled || policy.Browser.Enabled;
        var recipients = recipientResult.Value!;
        return Result<NotificationPolicyDecision>.Success(new NotificationPolicyDecision(
            eventDefinition.Key,
            IsEnabled: true,
            ShouldDispatch: hasDeliveryChannel && recipients.Count > 0,
            policy.Severity,
            policy.Sound,
            policy.Toast,
            policy.Inbox,
            policy.Browser,
            titleResult.Value,
            messageResult.Value,
            recipients));
    }

    private static Result<NotificationPolicyDecision> Failure(string code, string message) =>
        Result<NotificationPolicyDecision>.Failure(new Error(code, message));
}
