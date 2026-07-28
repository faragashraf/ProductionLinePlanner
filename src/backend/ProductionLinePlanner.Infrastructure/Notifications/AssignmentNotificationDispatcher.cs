using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Notifications;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Notifications;

public sealed class AssignmentNotificationDispatcher(
    AppDbContext dbContext,
    INotificationPolicyEngine policyEngine,
    INotificationPublisher notificationPublisher,
    ILogger<AssignmentNotificationDispatcher> logger) : IAssignmentNotificationDispatcher
{
    public async Task DispatchAsync(
        AssignmentNotificationDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var policy = await dbContext.NotificationPolicies
                .AsNoTracking()
                .Include(item => item.RecipientRules)
                .SingleOrDefaultAsync(item => item.EventKey == NotificationEventKeys.AssignmentChanged, cancellationToken);
            if (policy is null || !policy.IsEnabled)
            {
                return;
            }

            var stageId = request.ToSubStageId ?? request.FromSubStageId;
            if (stageId is not Guid validStageId || validStageId == Guid.Empty)
            {
                return;
            }

            var context = await (from worker in dbContext.Workers.AsNoTracking()
                                 join stage in dbContext.SubStages.AsNoTracking() on validStageId equals stage.Id
                                 join line in dbContext.ProductionLines.AsNoTracking() on stage.ProductionLineId equals line.Id
                                 join factory in dbContext.Factories.AsNoTracking() on line.FactoryId equals factory.Id
                                 join actor in dbContext.AppUsers.AsNoTracking() on request.ActorUserId equals actor.Id into actors
                                 from actor in actors.DefaultIfEmpty()
                                 where worker.Id == request.WorkerId
                                 select new
                                 {
                                     WorkerName = worker.FullName,
                                     LineName = line.Name,
                                     FactoryName = factory.Name,
                                     ActorName = actor == null ? "المستخدم الحالي" : actor.FullName
                                 })
                .SingleOrDefaultAsync(cancellationToken);
            if (context is null)
            {
                logger.LogWarning("Assignment notification context could not be resolved for assignment {AssignmentId}.", request.AssignmentId);
                return;
            }

            var definition = new NotificationPolicyDefinition(
                policy.EventKey,
                policy.IsEnabled,
                policy.Severity,
                new NotificationSoundPolicy(policy.IsSoundEnabled),
                new NotificationToastPolicy(policy.IsToastEnabled),
                new NotificationInboxPolicy(policy.IsInboxEnabled),
                new NotificationBrowserPolicy(policy.IsBrowserEnabled),
                policy.TitleTemplateAr,
                policy.MessageTemplateAr,
                policy.RecipientRules
                    .Where(rule => rule.IsActive)
                    .OrderBy(rule => rule.SortOrder)
                    .Select(ToRecipientRule)
                    .ToArray());
            var decision = await policyEngine.EvaluateAsync(
                definition,
                new NotificationEventContext(
                    NotificationEventKeys.AssignmentChanged,
                    new Dictionary<string, string>
                    {
                        ["WorkerName"] = context.WorkerName,
                        ["ActorName"] = context.ActorName,
                        ["LineName"] = context.LineName,
                        ["FactoryName"] = context.FactoryName
                    },
                    ActorUserId: request.ActorUserId,
                    CreatorUserId: request.ActorUserId),
                cancellationToken);
            if (decision.IsFailure)
            {
                logger.LogWarning("Assignment notification policy evaluation failed for assignment {AssignmentId}: {Code}.", request.AssignmentId, decision.Error?.Code);
                return;
            }

            var evaluated = decision.Value!;
            if (!evaluated.ShouldDispatch)
            {
                return;
            }

            foreach (var recipientUserId in evaluated.RecipientUserIds)
            {
                var published = await notificationPublisher.PublishToUserAsync(new PublishUserNotificationCommand(
                    Guid.NewGuid(),
                    recipientUserId,
                    evaluated.Title!,
                    evaluated.Message!,
                    SenderUserId: request.ActorUserId,
                    RelatedWorkerId: request.WorkerId,
                    RelatedEntityType: "WorkerAssignment",
                    RelatedEntityId: request.AssignmentId,
                    EventKey: evaluated.EventKey,
                    Severity: evaluated.Severity,
                    IsToastEnabled: evaluated.Toast.Enabled,
                    IsSoundEnabled: evaluated.Sound.Enabled,
                    IsBrowserEnabled: evaluated.Browser.Enabled), cancellationToken);
                if (published.IsFailure)
                {
                    logger.LogWarning("Assignment notification persistence failed for assignment {AssignmentId} and recipient {RecipientUserId}: {Code}.", request.AssignmentId, recipientUserId, published.Error?.Code);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Assignment notification dispatch failed after assignment {AssignmentId} was committed.", request.AssignmentId);
        }
    }

    private static NotificationRecipientRule ToRecipientRule(NotificationPolicyRecipientRule rule) => rule.RecipientKind switch
    {
        NotificationRecipientKind.User => new(NotificationRecipientKind.User, rule.UserId),
        NotificationRecipientKind.Role => new(NotificationRecipientKind.Role, rule.RoleId),
        NotificationRecipientKind.Permission => new(NotificationRecipientKind.Permission, Value: rule.PermissionKey),
        NotificationRecipientKind.CapabilityGroup => new(NotificationRecipientKind.CapabilityGroup, Value: rule.CapabilityKey),
        NotificationRecipientKind.Creator => new(NotificationRecipientKind.Creator),
        NotificationRecipientKind.ExcludeActor => new(NotificationRecipientKind.ExcludeActor),
        NotificationRecipientKind.AllActiveUsers => new(NotificationRecipientKind.AllActiveUsers),
        _ => throw new InvalidOperationException("Unsupported notification recipient rule.")
    };
}
