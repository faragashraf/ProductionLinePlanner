using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Notifications;

public sealed class NotificationPolicyCatalogReconciler(
    AppDbContext dbContext,
    INotificationEventCatalog eventCatalog) : INotificationPolicyCatalogReconciler
{
    private static readonly SemaphoreSlim ReconciliationGate = new(1, 1);

    public async Task<Result<int>> EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await ReconciliationGate.WaitAsync(cancellationToken);
        try
        {
            var catalogEntries = eventCatalog.GetAll();
            var knownKeys = new HashSet<string>(catalogEntries.Select(entry => entry.Key), StringComparer.OrdinalIgnoreCase);
            var persistedKeys = await dbContext.NotificationPolicies
                .AsNoTracking()
                .Where(policy => knownKeys.Contains(policy.EventKey))
                .Select(policy => policy.EventKey)
                .ToArrayAsync(cancellationToken);
            var persistedKeySet = new HashSet<string>(persistedKeys, StringComparer.OrdinalIgnoreCase);
            var missingEntries = catalogEntries
                .Where(entry => !persistedKeySet.Contains(entry.Key))
                .ToArray();

            foreach (var entry in missingEntries)
            {
                var defaults = entry.DefaultPolicy;
                var policyId = Guid.NewGuid();
                var policy = new NotificationPolicy(
                    policyId,
                    entry.Key,
                    defaults.IsEnabled,
                    defaults.Severity,
                    defaults.Toast.Enabled,
                    defaults.Inbox.Enabled,
                    defaults.Sound.Enabled,
                    defaults.Browser.Enabled,
                    soundKey: defaults.Sound.Enabled ? "default" : null,
                    defaults.TitleTemplate,
                    defaults.MessageTemplate);
                dbContext.NotificationPolicies.Add(policy);
                var sortOrder = 0;
                foreach (var rule in defaults.RecipientRules)
                {
                    policy.RecipientRules.Add(new NotificationPolicyRecipientRule(
                        Guid.NewGuid(),
                        policyId,
                        rule.Kind,
                        rule.Kind == Domain.Notifications.NotificationRecipientKind.User ? rule.SubjectId : null,
                        rule.Kind == Domain.Notifications.NotificationRecipientKind.Role ? rule.SubjectId : null,
                        rule.Kind == Domain.Notifications.NotificationRecipientKind.Permission ? rule.Value : null,
                        rule.Kind == Domain.Notifications.NotificationRecipientKind.CapabilityGroup ? rule.Value : null,
                        rule.Kind == Domain.Notifications.NotificationRecipientKind.ExcludeActor,
                        sortOrder++));
                }
            }

            if (missingEntries.Length > 0)
            {
                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // A second application instance may have reconciled the same catalog entry after this
                    // instance read it. Treat only a fully reconciled post-condition as a safe idempotent race.
                    dbContext.ChangeTracker.Clear();
                    var reconciledKeys = await dbContext.NotificationPolicies
                        .AsNoTracking()
                        .Where(policy => knownKeys.Contains(policy.EventKey))
                        .Select(policy => policy.EventKey)
                        .ToArrayAsync(cancellationToken);
                    if (knownKeys.SetEquals(reconciledKeys))
                    {
                        return Result<int>.Success(0);
                    }

                    return Result<int>.Failure(new Error(
                        "NotificationPolicyCatalogReconciliationFailed",
                        "Notification policy catalog defaults could not be reconciled safely."));
                }
            }

            return Result<int>.Success(missingEntries.Length);
        }
        finally
        {
            ReconciliationGate.Release();
        }
    }
}
