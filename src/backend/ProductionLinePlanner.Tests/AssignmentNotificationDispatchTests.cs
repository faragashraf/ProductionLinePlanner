using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Notifications;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Notifications;
using ProductionLinePlanner.Infrastructure.Realtime;

namespace ProductionLinePlanner.Tests;

public sealed class AssignmentNotificationDispatchTests
{
    [Fact]
    public async Task Permanent_assignment_persists_one_assignment_notification_for_actor_and_one_explicit_recipient()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true, includeActor: true, includeOtherRecipient: true);

        var result = await fixture.Engine.CreateOrUpdateDefaultAssignmentAsync(
            new CreateDefaultAssignmentRequest { WorkerId = fixture.Worker.Id, SubStageId = fixture.Stage.Id },
            fixture.Actor.Id);

        Assert.True(result.IsSuccess);
        var notifications = await fixture.Db.Notifications.OrderBy(item => item.RecipientUserId).ToArrayAsync();
        Assert.Equal(2, notifications.Length);
        Assert.Equal(new[] { fixture.Actor.Id, fixture.OtherRecipient.Id }.OrderBy(id => id), notifications.Select(item => item.RecipientUserId).OrderBy(id => id));
        Assert.All(notifications, notification =>
        {
            Assert.Equal(NotificationEventKeys.AssignmentChanged, notification.EventKey);
            Assert.Equal(NotificationSeverity.Warning, notification.Severity);
            Assert.Equal(fixture.Actor.Id, notification.SenderUserId);
            Assert.Equal(fixture.Worker.Id, notification.RelatedWorkerId);
        });
        Assert.Equal(2, fixture.LiveDispatcher.UserNotifications.Count);
    }

    [Fact]
    public async Task Temporary_assignment_uses_the_same_policy_and_keeps_actor_when_creator_rule_is_enabled()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true, includeActor: true, includeOtherRecipient: false);
        var start = DateTime.UtcNow.AddMinutes(1);

        var result = await fixture.Engine.CreateTemporaryAssignmentAsync(
            new CreateTemporaryAssignmentRequest
            {
                WorkerId = fixture.Worker.Id,
                ToSubStageId = fixture.Stage.Id,
                StartAtUtc = start,
                EndAtUtc = start.AddHours(1),
                Reason = "Temporary support",
                ParticipationMode = TemporaryAssignmentMode.AdditionalParticipation
            },
            fixture.Actor.Id);

        Assert.True(result.IsSuccess);
        var notification = Assert.Single(await fixture.Db.Notifications.ToArrayAsync());
        Assert.Equal(fixture.Actor.Id, notification.RecipientUserId);
        Assert.Equal(NotificationEventKeys.AssignmentChanged, notification.EventKey);
    }

    [Fact]
    public async Task Disabled_assignment_policy_does_not_create_a_notification()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: false, includeActor: true, includeOtherRecipient: false);

        var result = await fixture.Engine.CreateOrUpdateDefaultAssignmentAsync(
            new CreateDefaultAssignmentRequest { WorkerId = fixture.Worker.Id, SubStageId = fixture.Stage.Id },
            fixture.Actor.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(await fixture.Db.Notifications.ToArrayAsync());
        Assert.Empty(fixture.LiveDispatcher.UserNotifications);
    }

    [Fact]
    public async Task Policy_without_creator_rule_does_not_send_to_actor()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true, includeActor: false, includeOtherRecipient: true);

        var result = await fixture.Engine.CreateOrUpdateDefaultAssignmentAsync(
            new CreateDefaultAssignmentRequest { WorkerId = fixture.Worker.Id, SubStageId = fixture.Stage.Id },
            fixture.Actor.Id);

        Assert.True(result.IsSuccess);
        var notification = Assert.Single(await fixture.Db.Notifications.ToArrayAsync());
        Assert.Equal(fixture.OtherRecipient.Id, notification.RecipientUserId);
        Assert.NotEqual(fixture.Actor.Id, notification.RecipientUserId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db, AssignmentEngine engine, RecordingLiveDispatcher liveDispatcher, AppUser actor, AppUser otherRecipient, Worker worker, SubStage stage)
        {
            Db = db;
            Engine = engine;
            LiveDispatcher = liveDispatcher;
            Actor = actor;
            OtherRecipient = otherRecipient;
            Worker = worker;
            Stage = stage;
        }

        public AppDbContext Db { get; }
        public AssignmentEngine Engine { get; }
        public RecordingLiveDispatcher LiveDispatcher { get; }
        public AppUser Actor { get; }
        public AppUser OtherRecipient { get; }
        public Worker Worker { get; }
        public SubStage Stage { get; }

        public static async Task<Fixture> CreateAsync(bool enabled, bool includeActor, bool includeOtherRecipient)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            var actor = new AppUser(Guid.NewGuid(), "Assignment actor", "assignment-actor@test.local", "hash");
            var otherRecipient = new AppUser(Guid.NewGuid(), "Other recipient", "assignment-recipient@test.local", "hash");
            var worker = new Worker(Guid.NewGuid(), "W-1", "Worker One");
            var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
            var group = new MainStage(Guid.NewGuid(), line.Id, "Legacy", 1);
            var stage = new SubStage(Guid.NewGuid(), group.Id, "Assembly", "STG001", 1, 1, productionLineId: line.Id);
            var policy = new NotificationPolicy(Guid.NewGuid(), NotificationEventKeys.AssignmentChanged, enabled, NotificationSeverity.Warning, true, true, false, null, "تسكين العامل", "تم تسكين {WorkerName} في {LineName} بواسطة {ActorName}.");
            var sortOrder = 1;
            if (includeActor)
                policy.RecipientRules.Add(new NotificationPolicyRecipientRule(Guid.NewGuid(), policy.Id, NotificationRecipientKind.Creator, null, null, null, null, false, sortOrder++));
            if (includeOtherRecipient)
                policy.RecipientRules.Add(new NotificationPolicyRecipientRule(Guid.NewGuid(), policy.Id, NotificationRecipientKind.User, otherRecipient.Id, null, null, null, false, sortOrder));
            db.AddRange(actor, otherRecipient, worker, factory, line, group, stage, policy);
            await db.SaveChangesAsync();

            var liveDispatcher = new RecordingLiveDispatcher();
            var publisher = new NotificationPublisher(db, liveDispatcher, NullLogger<NotificationPublisher>.Instance);
            var recipientResolver = new NotificationRecipientResolver(db, new NoopPermissionService());
            var policyEngine = new NotificationPolicyEngine(new CodeNotificationEventCatalog(), new NotificationTemplateResolver(), recipientResolver);
            var dispatcher = new AssignmentNotificationDispatcher(db, policyEngine, publisher, NullLogger<AssignmentNotificationDispatcher>.Instance);
            return new Fixture(db, new AssignmentEngine(db, new AuditEngine(db), dispatcher), liveDispatcher, actor, otherRecipient, worker, stage);
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class NoopPermissionService : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<PermissionCatalogItemDto>());
    }

    private sealed class RecordingLiveDispatcher : INotificationLiveDispatcher
    {
        public List<(Guid RecipientUserId, NotificationSummaryDto Notification)> UserNotifications { get; } = [];
        public Task SendToUserAsync(Guid recipientUserId, NotificationSummaryDto notification, CancellationToken cancellationToken = default)
        {
            UserNotifications.Add((recipientUserId, notification));
            return Task.CompletedTask;
        }

        public Task SendToCapabilityAsync(string permission, NotificationSummaryDto notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
