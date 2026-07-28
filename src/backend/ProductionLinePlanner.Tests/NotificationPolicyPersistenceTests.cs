using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Notifications;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Data.Migrations;
using ProductionLinePlanner.Infrastructure.Notifications;

namespace ProductionLinePlanner.Tests;

public sealed class NotificationPolicyPersistenceTests
{
    [Fact]
    public async Task Catalog_reconciliation_creates_each_static_event_with_declared_defaults_and_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Equal(NotificationEventCatalog.All.Count, await fixture.Db.NotificationPolicies.CountAsync());
        var policies = await fixture.Db.NotificationPolicies.Include(policy => policy.RecipientRules).ToListAsync();
        Assert.All(policies.Where(policy => policy.EventKey is not NotificationEventKeys.WorkerCheckedIn and not NotificationEventKeys.WorkerCheckedOut), policy => Assert.False(policy.IsEnabled));
        Assert.All(policies.Where(policy => policy.EventKey is NotificationEventKeys.WorkerCheckedIn or NotificationEventKeys.WorkerCheckedOut), policy =>
        {
            Assert.True(policy.IsEnabled);
            Assert.Contains(policy.RecipientRules, rule => rule.RecipientKind == NotificationRecipientKind.AllActiveUsers);
        });

        var repeat = await fixture.Reconciler.EnsureDefaultsAsync();

        Assert.True(repeat.IsSuccess);
        Assert.Equal(0, repeat.Value);
        Assert.Equal(NotificationEventCatalog.All.Count, await fixture.Db.NotificationPolicies.CountAsync());
    }

    [Fact]
    public async Task Policy_update_persists_valid_recipient_rules_and_audits_without_template_contents()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = new AppUser(Guid.NewGuid(), "Policy recipient", "policy-recipient@test.local", "hash");
        var role = new AppRole(Guid.NewGuid(), "Policy recipient role");
        fixture.Db.AddRange(user, role);
        await fixture.Db.SaveChangesAsync();

        var current = await fixture.GetPolicyAsync(NotificationEventKeys.WorkerCreated);
        var result = await fixture.Service.UpdatePolicyAsync(
            NotificationEventKeys.WorkerCreated,
            new NotificationPolicyUpdateRequest
            {
                IsEnabled = true,
                Severity = NotificationSeverity.Warning.ToString(),
                IsToastEnabled = true,
                IsInboxEnabled = true,
                IsSoundEnabled = true,
                SoundKey = "default",
                TitleTemplateAr = "عامل {WorkerName}",
                MessageTemplateAr = "أنشأ {ActorName} العامل {WorkerName} في {FactoryName}",
                RowVersion = current.RowVersion,
                RecipientRules =
                [
                    Rule("User", 0, userId: user.Id),
                    Rule("Role", 1, roleId: role.Id),
                    Rule("Permission", 2, permissionKey: "workers.view"),
                    Rule("CapabilityGroup", 3, capabilityKey: "workers"),
                    Rule("Creator", 4),
                    Rule("ExcludeActor", 5, isExcludeActor: true)
                ]
            },
            fixture.ActorId,
            "notification-policy-test");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsEnabled);
        Assert.Equal(NotificationSeverity.Warning.ToString(), result.Value.Severity);
        Assert.Equal(6, result.Value.RecipientRules.Count);
        Assert.Single(fixture.Audit.Calls);
        Assert.Equal(nameof(NotificationPolicy), fixture.Audit.Calls[0].EntityType);
        Assert.DoesNotContain("عامل {WorkerName}", fixture.Audit.Calls[0].After?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Policy_update_rejects_unknown_event_unknown_token_and_invalid_recipient_target()
    {
        await using var fixture = await Fixture.CreateAsync();
        var current = await fixture.GetPolicyAsync(NotificationEventKeys.WorkerCreated);

        var unknownEvent = await fixture.Service.UpdatePolicyAsync("NotAnEvent", CreateUpdate(current), fixture.ActorId);
        var unknownToken = await fixture.Service.UpdatePolicyAsync(
            NotificationEventKeys.WorkerCreated,
            CreateUpdate(current, message: "عنصر {UnknownToken}"),
            fixture.ActorId);
        var invalidRule = await fixture.Service.UpdatePolicyAsync(
            NotificationEventKeys.WorkerCreated,
            CreateUpdate(current, rules: [Rule("User", 0)]),
            fixture.ActorId);

        Assert.True(unknownEvent.IsFailure);
        Assert.Equal("UnknownNotificationEvent", unknownEvent.Error!.Code);
        Assert.True(unknownToken.IsFailure);
        Assert.Equal("UnknownTemplateToken", unknownToken.Error!.Code);
        Assert.True(invalidRule.IsFailure);
        Assert.Equal("InvalidRecipientRule", invalidRule.Error!.Code);
    }

    [Fact]
    public async Task Policy_update_rejects_stale_row_version_without_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var current = await fixture.GetPolicyAsync(NotificationEventKeys.WorkerCreated);
        var stale = await fixture.Service.UpdatePolicyAsync(
            NotificationEventKeys.WorkerCreated,
            CreateUpdate(current, rowVersion: Convert.ToBase64String([9, 9, 9, 9])),
            fixture.ActorId);

        Assert.True(stale.IsFailure);
        Assert.Equal("ConcurrencyConflict", stale.Error!.Code);
        Assert.Empty(fixture.Audit.Calls);
    }

    [Fact]
    public async Task Existing_notification_rows_with_null_event_and_severity_remain_readable_as_information()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);
        var recipient = new AppUser(Guid.NewGuid(), "Inbox recipient", "inbox-recipient@test.local", "hash");
        var notification = new Notification(Guid.NewGuid(), recipient.Id, "قديم", "صف قديم");
        db.AddRange(recipient, notification);
        await db.SaveChangesAsync();
        var engine = new NotificationEngine(db, new RecordingAuditEngine());

        var result = await engine.GetNotificationsAsync(recipient.Id, null);

        Assert.True(result.IsSuccess);
        var dto = Assert.Single(result.Value!.Items);
        Assert.Null(dto.EventKey);
        Assert.Equal(NotificationSeverity.Information, dto.Severity);
    }

    [Fact]
    public async Task Model_keeps_existing_notification_columns_backward_compatible()
    {
        await using var fixture = await Fixture.CreateAsync();
        var notification = fixture.Db.Model.FindEntityType(typeof(Notification))!;
        var eventKey = notification.FindProperty(nameof(Notification.EventKey))!;
        var severity = notification.FindProperty(nameof(Notification.Severity))!;

        Assert.True(eventKey.IsNullable);
        Assert.True(severity.IsNullable);
        Assert.Equal(NotificationSeverity.Information, severity.GetDefaultValue());
        Assert.True(fixture.Db.Model.FindEntityType(typeof(NotificationPolicy))!.FindProperty(nameof(NotificationPolicy.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Migration_up_is_additive_and_has_safe_existing_notification_column_shapes()
    {
        var migration = new AddNotificationPolicyPlatform();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(Migration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        Assert.Contains(builder.Operations.OfType<CreateTableOperation>(), operation => operation.Name == "NotificationPolicies");
        Assert.Contains(builder.Operations.OfType<CreateTableOperation>(), operation => operation.Name == "NotificationPolicyRecipientRules");
        var eventKey = Assert.Single(
            builder.Operations.OfType<AddColumnOperation>(),
            operation => operation.Table == "Notifications" && operation.Name == "EventKey");
        var severity = Assert.Single(
            builder.Operations.OfType<AddColumnOperation>(),
            operation => operation.Table == "Notifications" && operation.Name == "Severity");
        Assert.True(eventKey.IsNullable);
        Assert.True(severity.IsNullable);
        Assert.Equal((int)NotificationSeverity.Information, severity.DefaultValue);
        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation or DropIndexOperation or DeleteDataOperation or UpdateDataOperation or AlterColumnOperation);
    }

    [Fact]
    public void Attendance_notification_migration_is_additive_and_enforces_idempotency_indexes()
    {
        var migration = new AddAttendanceRealtimeNotifications();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(Migration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        var table = Assert.Single(
            builder.Operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "AttendanceNotificationEvents");
        Assert.Contains(table.ForeignKeys, foreignKey =>
            foreignKey.PrincipalTable == "AttendanceRecords" && foreignKey.OnDelete == ReferentialAction.Restrict);
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "AttendanceNotificationEvents" && index.Name == "IX_AttendanceNotificationEvents_IdempotencyKey" && index.IsUnique);
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "Notifications" && index.Name == "IX_Notifications_RecipientUserId_CorrelationKey" && index.IsUnique && index.Filter is not null);
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "AttendanceRecords" && index.Name == "IX_AttendanceRecords_WorkerId_AttendanceTimeUtc" && index.IsUnique);
        Assert.DoesNotContain(builder.Operations, operation =>
            operation is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation or AlterColumnOperation);
    }

    private static NotificationPolicyRecipientRuleUpdateRequest Rule(
        string kind,
        int sortOrder,
        Guid? userId = null,
        Guid? roleId = null,
        string? permissionKey = null,
        string? capabilityKey = null,
        bool isExcludeActor = false) => new()
        {
            RecipientKind = kind,
            UserId = userId?.ToString(),
            RoleId = roleId?.ToString(),
            PermissionKey = permissionKey,
            CapabilityKey = capabilityKey,
            IsExcludeActor = isExcludeActor,
            SortOrder = sortOrder,
            IsActive = true
        };

    private static NotificationPolicyUpdateRequest CreateUpdate(
        NotificationPolicyDetailsDto current,
        string? message = null,
        IReadOnlyCollection<NotificationPolicyRecipientRuleUpdateRequest>? rules = null,
        string? rowVersion = null) => new()
        {
            IsEnabled = true,
            Severity = NotificationSeverity.Information.ToString(),
            IsToastEnabled = true,
            IsInboxEnabled = true,
            IsSoundEnabled = false,
            SoundKey = null,
            TitleTemplateAr = "عامل {WorkerName}",
            MessageTemplateAr = message ?? "أنشأ {ActorName} العامل {WorkerName} في {FactoryName}",
            RowVersion = rowVersion ?? current.RowVersion,
            RecipientRules = rules ?? []
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db, NotificationPolicyCatalogReconciler reconciler, NotificationPolicyAdminService service, RecordingAuditEngine audit)
        {
            Db = db;
            Reconciler = reconciler;
            Service = service;
            Audit = audit;
        }

        public Guid ActorId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; }
        public NotificationPolicyCatalogReconciler Reconciler { get; }
        public NotificationPolicyAdminService Service { get; }
        public RecordingAuditEngine Audit { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            var catalog = new CodeNotificationEventCatalog();
            var reconciler = new NotificationPolicyCatalogReconciler(db, catalog);
            var audit = new RecordingAuditEngine();
            var service = new NotificationPolicyAdminService(db, catalog, new NotificationTemplateResolver(), reconciler, audit);
            var seeded = await reconciler.EnsureDefaultsAsync();
            Assert.True(seeded.IsSuccess);

            foreach (var policy in await db.NotificationPolicies.ToListAsync())
            {
                if (policy.RowVersion.Length == 0)
                {
                    db.Entry(policy).Property(nameof(NotificationPolicy.RowVersion)).CurrentValue = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                }
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(db, reconciler, service, audit);
        }

        public async Task<NotificationPolicyDetailsDto> GetPolicyAsync(string eventKey)
        {
            var result = await Service.GetPolicyAsync(eventKey);
            Assert.True(result.IsSuccess);
            Assert.NotEmpty(result.Value!.RowVersion);
            return result.Value;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
