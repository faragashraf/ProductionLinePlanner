using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionLinePlanner.Api.Realtime;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Realtime;

namespace ProductionLinePlanner.Tests;

public sealed class RealtimeNotificationFoundationTests
{
    [Fact]
    public void Authenticated_user_id_is_resolved_only_from_server_claims()
    {
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "test"));

        Assert.Equal(userId.ToString("D"), AuthenticatedUserIdProvider.ResolveUserId(principal));
        Assert.Equal(
            userId.ToString("D"),
            AuthenticatedUserIdProvider.ResolveUserId(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", userId.ToString())],
                "test"))));
        Assert.Null(AuthenticatedUserIdProvider.ResolveUserId(new ClaimsPrincipal()));
        Assert.Null(AuthenticatedUserIdProvider.ResolveUserId(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "not-a-guid"), new Claim(ClaimTypes.Email, "user@example.test")],
            "test"))));
    }

    [Fact]
    public void SignalR_query_token_is_accepted_only_for_the_notification_hub_path()
    {
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["access_token"] = "sensitive-token"
        });

        Assert.Equal("sensitive-token", RealtimeAccessTokenResolver.Resolve("/hubs/notifications", query));
        Assert.Equal("sensitive-token", RealtimeAccessTokenResolver.Resolve("/hubs/notifications/negotiate", query));
        Assert.Null(RealtimeAccessTokenResolver.Resolve("/api/notifications", query));
        Assert.Null(RealtimeAccessTokenResolver.Resolve("/hubs/other", query));
        Assert.Null(RealtimeAccessTokenResolver.Resolve("/hubs/notifications-other", query));
    }

    [Fact]
    public async Task Capability_groups_are_derived_from_effective_known_permissions_only()
    {
        var resolver = new CapabilityGroupResolver(new PermissionServiceStub(
            ["attendance.view", "ATTENDANCE.VIEW", "not-a-permission"]));

        var groups = await resolver.ResolveGroupsAsync(Guid.NewGuid());

        Assert.Equal(["capability:attendance.view"], groups);
        Assert.Throws<ArgumentException>(() => resolver.GetGroupName("client-controlled"));
    }

    [Fact]
    public async Task User_publish_persists_before_live_dispatch_and_is_idempotent()
    {
        await using var db = CreateDbContext();
        var recipient = new AppUser(Guid.NewGuid(), "Recipient", "recipient@example.test", "hash");
        db.AppUsers.Add(recipient);
        await db.SaveChangesAsync();
        var notificationId = Guid.NewGuid();
        var dispatcher = new RecordingDispatcher(() => db.Notifications.Any(x => x.Id == notificationId));
        var publisher = new NotificationPublisher(db, dispatcher, NullLogger<NotificationPublisher>.Instance);
        var command = new PublishUserNotificationCommand(
            notificationId, recipient.Id, "Production alert", "A stage needs review.");

        var first = await publisher.PublishToUserAsync(command);
        var duplicate = await publisher.PublishToUserAsync(command);

        Assert.True(first.IsSuccess);
        Assert.True(first.Value!.Created);
        Assert.True(first.Value.LiveDispatched);
        Assert.True(duplicate.IsSuccess);
        Assert.False(duplicate.Value!.Created);
        Assert.False(duplicate.Value.LiveDispatched);
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Single(dispatcher.UserDeliveries);
        Assert.True(dispatcher.NotificationWasPersistedWhenDispatched);
    }

    [Fact]
    public async Task Concurrent_primary_key_winner_is_resolved_without_a_second_live_dispatch()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ConcurrentInsertRaceDbContext(options);
        var recipient = new AppUser(Guid.NewGuid(), "Recipient", "race-recipient@example.test", "hash");
        db.AppUsers.Add(recipient);
        await db.SaveChangesAsync();
        db.SimulateConcurrentNotificationInsert = true;
        var dispatcher = new RecordingDispatcher();
        var publisher = new NotificationPublisher(db, dispatcher, NullLogger<NotificationPublisher>.Instance);
        var notificationId = Guid.NewGuid();

        var result = await publisher.PublishToUserAsync(new(
            notificationId, recipient.Id, "Concurrent", "Only the durable winner is retained."));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Created);
        Assert.False(result.Value.LiveDispatched);
        Assert.Empty(dispatcher.UserDeliveries);
        Assert.Single(await db.Notifications.Where(x => x.Id == notificationId).ToListAsync());
    }

    [Fact]
    public async Task Reusing_a_notification_id_with_a_different_payload_is_rejected()
    {
        await using var db = CreateDbContext();
        var recipient = new AppUser(Guid.NewGuid(), "Recipient", "conflict-recipient@example.test", "hash");
        db.AppUsers.Add(recipient);
        await db.SaveChangesAsync();
        var dispatcher = new RecordingDispatcher();
        var publisher = new NotificationPublisher(db, dispatcher, NullLogger<NotificationPublisher>.Instance);
        var notificationId = Guid.NewGuid();

        var first = await publisher.PublishToUserAsync(new(
            notificationId, recipient.Id, "Original", "Original payload."));
        var conflict = await publisher.PublishToUserAsync(new(
            notificationId, recipient.Id, "Changed", "Different payload."));

        Assert.True(first.IsSuccess);
        Assert.True(conflict.IsFailure);
        Assert.Equal("NotificationIdConflict", conflict.Error!.Code);
        Assert.Single(dispatcher.UserDeliveries);
        Assert.Single(await db.Notifications.Where(x => x.Id == notificationId).ToListAsync());
    }

    [Fact]
    public async Task Live_failure_does_not_rollback_the_persisted_notification()
    {
        await using var db = CreateDbContext();
        var recipient = new AppUser(Guid.NewGuid(), "Recipient", "recipient2@example.test", "hash");
        db.AppUsers.Add(recipient);
        await db.SaveChangesAsync();
        var publisher = new NotificationPublisher(
            db,
            new ThrowingDispatcher(),
            NullLogger<NotificationPublisher>.Instance);
        var notificationId = Guid.NewGuid();

        var result = await publisher.PublishToUserAsync(new(
            notificationId, recipient.Id, "Persisted", "Live delivery can be retried by loading the inbox."));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Created);
        Assert.False(result.Value.LiveDispatched);
        Assert.True(await db.Notifications.AnyAsync(x => x.Id == notificationId));
    }

    [Fact]
    public async Task Storage_failure_is_not_reported_or_dispatched_as_success()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new FailingSaveDbContext(options);
        var recipient = new AppUser(Guid.NewGuid(), "Recipient", "storage-failure@example.test", "hash");
        db.AppUsers.Add(recipient);
        await db.SaveChangesAsync();
        db.FailOnSave = true;
        var dispatcher = new RecordingDispatcher();
        var publisher = new NotificationPublisher(db, dispatcher, NullLogger<NotificationPublisher>.Instance);

        await Assert.ThrowsAsync<DbUpdateException>(() => publisher.PublishToUserAsync(new(
            Guid.NewGuid(), recipient.Id, "Not persisted", "This must never be live-dispatched.")));

        Assert.Empty(dispatcher.UserDeliveries);
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Notification_reads_and_mark_read_are_isolated_to_the_owner()
    {
        await using var db = CreateDbContext();
        var owner = new AppUser(Guid.NewGuid(), "Owner", "owner@example.test", "hash");
        var other = new AppUser(Guid.NewGuid(), "Other", "other@example.test", "hash");
        var notification = new Notification(Guid.NewGuid(), owner.Id, "Private", "Owner only");
        db.AddRange(owner, other, notification);
        await db.SaveChangesAsync();
        var engine = new NotificationEngine(db, new AuditEngineStub());

        var otherList = await engine.GetNotificationsAsync(other.Id, null);
        var forbiddenMark = await engine.MarkNotificationReadAsync(other.Id, notification.Id);

        Assert.Empty(otherList.Value!.Items);
        Assert.True(forbiddenMark.IsFailure);
        Assert.False((await db.Notifications.SingleAsync()).IsRead);
    }

    [Fact]
    public async Task Notification_list_and_unread_count_do_not_write_to_the_database()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new SaveCountingDbContext(options);
        var owner = new AppUser(Guid.NewGuid(), "Owner", "read-owner@example.test", "hash");
        db.AddRange(owner, new Notification(Guid.NewGuid(), owner.Id, "Unread", "Read-only query"));
        await db.SaveChangesAsync();
        db.SaveChangesCalls = 0;
        var engine = new NotificationEngine(db, new AuditEngineStub());

        var list = await engine.GetNotificationsAsync(owner.Id, null);
        var unread = await engine.GetUnreadCountAsync(owner.Id);

        Assert.True(list.IsSuccess);
        Assert.True(unread.IsSuccess);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public void Hub_declares_no_client_callable_business_methods()
    {
        var declaredPublicMethods = typeof(NotificationsHub)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal([nameof(NotificationsHub.OnConnectedAsync), nameof(NotificationsHub.OnDisconnectedAsync)], declaredPublicMethods);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class PermissionServiceStub(IReadOnlyCollection<string> permissions) : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permissions);

        public Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<PermissionCatalogItemDto>());
    }

    private sealed class RecordingDispatcher(Func<bool>? persistenceProbe = null) : INotificationLiveDispatcher
    {
        public List<NotificationSummaryDto> UserDeliveries { get; } = [];
        public bool NotificationWasPersistedWhenDispatched { get; private set; }

        public Task SendToUserAsync(Guid recipientUserId, NotificationSummaryDto notification, CancellationToken cancellationToken = default)
        {
            UserDeliveries.Add(notification);
            NotificationWasPersistedWhenDispatched = persistenceProbe?.Invoke() ?? true;
            return Task.CompletedTask;
        }

        public Task SendToCapabilityAsync(string permission, NotificationSummaryDto notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingDispatcher : INotificationLiveDispatcher
    {
        public Task SendToUserAsync(Guid recipientUserId, NotificationSummaryDto notification, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SignalR unavailable");

        public Task SendToCapabilityAsync(string permission, NotificationSummaryDto notification, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SignalR unavailable");
    }

    private sealed class AuditEngineStub : IAuditEngine
    {
        public Task<Result> RecordAsync(Guid actorUserId, AuditActionType actionType, string entityType, string entityId, object? before = null, object? after = null, string? requestMeta = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class ConcurrentInsertRaceDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public bool SimulateConcurrentNotificationInsert { get; set; }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (SimulateConcurrentNotificationInsert)
            {
                SimulateConcurrentNotificationInsert = false;
                var attempted = ChangeTracker.Entries<Notification>()
                    .Single(entry => entry.State == EntityState.Added)
                    .Entity;
                Entry(attempted).State = EntityState.Detached;
                Notifications.Add(new Notification(
                    attempted.Id,
                    attempted.RecipientUserId,
                    attempted.Title,
                    attempted.Message,
                    attempted.SenderUserId,
                    attempted.RelatedWorkerId,
                    attempted.RelatedEntityType,
                    attempted.RelatedEntityId,
                    attempted.Status,
                    attempted.CreatedAtUtc));
                await base.SaveChangesAsync(cancellationToken);
                throw new DbUpdateException("Simulated concurrent primary-key winner.");
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class SaveCountingDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public int SaveChangesCalls { get; set; }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class FailingSaveDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public bool FailOnSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            FailOnSave
                ? throw new DbUpdateException("Simulated notification storage failure.")
                : base.SaveChangesAsync(cancellationToken);
    }
}
