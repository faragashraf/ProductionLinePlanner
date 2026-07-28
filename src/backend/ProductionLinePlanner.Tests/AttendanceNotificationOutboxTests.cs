using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Notifications;
using ProductionLinePlanner.Infrastructure.Authorization;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Notifications;
using ProductionLinePlanner.Infrastructure.Realtime;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class AttendanceNotificationOutboxTests
{
    [Theory]
    [InlineData(WorkerAttendanceNotificationType.CheckIn, NotificationEventKeys.WorkerCheckedIn, "الحضور")]
    [InlineData(WorkerAttendanceNotificationType.CheckOut, NotificationEventKeys.WorkerCheckedOut, "الانصراف")]
    public async Task Assigned_attendance_is_persisted_for_every_active_user_before_live_delivery(
        WorkerAttendanceNotificationType type,
        string eventKey,
        string expectedText)
    {
        await using var fixture = await Fixture.CreateAsync(type, enabled: true, assigned: true);

        var first = await fixture.Processor.ProcessPendingAsync();
        var second = await fixture.Processor.ProcessPendingAsync();

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value);
        Assert.Equal(0, second.Value);
        Assert.Equal(2, await fixture.Db.Notifications.CountAsync());
        Assert.Equal(2, fixture.Dispatcher.Deliveries.Count);
        Assert.All(fixture.Dispatcher.Deliveries, delivery => Assert.True(delivery.PersistedBeforeDispatch));
        var notification = await fixture.Db.Notifications.FirstAsync();
        Assert.Equal(eventKey, notification.EventKey);
        Assert.Contains(expectedText, notification.Message);
        Assert.Contains("مرحلة التجميع", notification.Message);
        Assert.True(notification.IsToastEnabled);
        Assert.True(notification.IsSoundEnabled);
        Assert.True(notification.IsBrowserEnabled);
        using var metadata = JsonDocument.Parse(notification.MetadataJson!);
        Assert.Equal("Assigned", metadata.RootElement.GetProperty("assignmentStatus").GetString());
    }

    [Fact]
    public async Task Unassigned_worker_uses_explicit_badge_and_never_reads_temporary_assignments()
    {
        await using var fixture = await Fixture.CreateAsync(WorkerAttendanceNotificationType.CheckIn, enabled: true, assigned: false);

        await fixture.Processor.ProcessPendingAsync();

        var notification = await fixture.Db.Notifications.FirstAsync();
        Assert.Contains("رقم 1024", notification.Message);
        Assert.Contains("غير مسكن", notification.Message);
        using var metadata = JsonDocument.Parse(notification.MetadataJson!);
        Assert.Equal("Unassigned", metadata.RootElement.GetProperty("assignmentStatus").GetString());
    }

    [Fact]
    public async Task Disabled_policy_consumes_event_without_creating_or_sending_notification()
    {
        await using var fixture = await Fixture.CreateAsync(WorkerAttendanceNotificationType.CheckOut, enabled: false, assigned: true);

        var result = await fixture.Processor.ProcessPendingAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.Empty(fixture.Db.Notifications);
        Assert.Empty(fixture.Dispatcher.Deliveries);
        Assert.NotNull((await fixture.Db.AttendanceNotificationEvents.SingleAsync()).ProcessedAtUtc);
    }

    [Fact]
    public async Task SignalR_failure_does_not_lose_persistent_notification_or_retry_toast()
    {
        await using var fixture = await Fixture.CreateAsync(WorkerAttendanceNotificationType.CheckIn, enabled: true, assigned: false, throwOnLiveDispatch: true);

        var result = await fixture.Processor.ProcessPendingAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await fixture.Db.Notifications.CountAsync());
        Assert.NotNull((await fixture.Db.AttendanceNotificationEvents.SingleAsync()).ProcessedAtUtc);
    }

    [Fact]
    public async Task Disabled_optional_channels_are_preserved_on_persistent_delivery()
    {
        await using var fixture = await Fixture.CreateAsync(
            WorkerAttendanceNotificationType.CheckIn,
            enabled: true,
            assigned: true,
            soundEnabled: false,
            browserEnabled: false);

        await fixture.Processor.ProcessPendingAsync();

        Assert.All(await fixture.Db.Notifications.ToListAsync(), notification =>
        {
            Assert.False(notification.IsSoundEnabled);
            Assert.False(notification.IsBrowserEnabled);
        });
    }

    [Fact]
    public async Task Multiple_permanent_assignments_choose_latest_without_blocking_notification()
    {
        await using var fixture = await Fixture.CreateAsync(
            WorkerAttendanceNotificationType.CheckIn,
            enabled: true,
            assigned: true,
            multipleAssignments: true);

        var result = await fixture.Processor.ProcessPendingAsync();

        Assert.True(result.IsSuccess);
        var notification = await fixture.Db.Notifications.FirstAsync();
        Assert.Contains("مرحلة التعبئة", notification.Message);
        using var metadata = JsonDocument.Parse(notification.MetadataJson!);
        Assert.Equal("مرحلة التعبئة", metadata.RootElement.GetProperty("stageName").GetString());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db, AttendanceNotificationOutboxProcessor processor, RecordingDispatcher dispatcher)
        {
            Db = db;
            Processor = processor;
            Dispatcher = dispatcher;
        }

        public AppDbContext Db { get; }
        public AttendanceNotificationOutboxProcessor Processor { get; }
        public RecordingDispatcher Dispatcher { get; }

        public static async Task<Fixture> CreateAsync(
            WorkerAttendanceNotificationType type,
            bool enabled,
            bool assigned,
            bool throwOnLiveDispatch = false,
            bool soundEnabled = true,
            bool browserEnabled = true,
            bool multipleAssignments = false)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            var db = new AppDbContext(options);
            var userOne = new AppUser(Guid.NewGuid(), "Online", "online@example.test", "hash");
            var userTwo = new AppUser(Guid.NewGuid(), "Offline", "offline@example.test", "hash");
            var worker = new Worker(Guid.NewGuid(), "1024", "أحمد محمد", badgeNumber: "1024");
            var attendanceTime = new DateTime(2026, 7, 28, type == WorkerAttendanceNotificationType.CheckIn ? 4 : 16, 44, 0, DateTimeKind.Utc);
            var record = new AttendanceRecord(Guid.NewGuid(), worker.Id, attendanceTime, AttendanceStatus.Present, "test");
            var eventKey = type == WorkerAttendanceNotificationType.CheckIn ? NotificationEventKeys.WorkerCheckedIn : NotificationEventKeys.WorkerCheckedOut;
            var policy = new NotificationPolicy(
                Guid.NewGuid(), eventKey, enabled, NotificationSeverity.Information,
                isToastEnabled: true, isInboxEnabled: true, isSoundEnabled: soundEnabled, isBrowserEnabled: browserEnabled,
                soundKey: "default", $"{(type == WorkerAttendanceNotificationType.CheckIn ? "حضور" : "انصراف")} عامل",
                $"سجل العامل {{WorkerName}} — رقم {{EmployeeCode}} — {(type == WorkerAttendanceNotificationType.CheckIn ? "الحضور" : "الانصراف")} الساعة {{AttendanceTime}}. {{AssignmentText}}");
            policy.RecipientRules.Add(new NotificationPolicyRecipientRule(
                Guid.NewGuid(), policy.Id, NotificationRecipientKind.AllActiveUsers,
                null, null, null, null, false, 0));
            db.AddRange(userOne, userTwo, worker, record, policy);
            db.AttendanceNotificationEvents.Add(new AttendanceNotificationEvent(
                Guid.NewGuid(), record.Id, worker.Id, worker.FullName, worker.EmployeeCode,
                type, attendanceTime, "test", $"attendance:{record.Id:D}:{type}"));

            if (assigned)
            {
                var factory = new Factory(Guid.NewGuid(), "مصنع القاهرة", "CAI");
                var line = new ProductionLine(Guid.NewGuid(), factory.Id, "خط الإنتاج 2", 1);
                var mainStage = new MainStage(Guid.NewGuid(), line.Id, "التجميع", 1);
                var stage = new SubStage(Guid.NewGuid(), mainStage.Id, "التجميع", "ASM", 10, 1, productionLineId: line.Id);
                db.AddRange(factory, line, mainStage, stage,
                    new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, stage.Id, userOne.Id, attendanceTime.AddHours(-2)));
                if (multipleAssignments)
                {
                    var newerStage = new SubStage(Guid.NewGuid(), mainStage.Id, "مرحلة التعبئة", "PACK", 10, 2, productionLineId: line.Id);
                    db.AddRange(newerStage,
                        new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, newerStage.Id, userOne.Id, attendanceTime.AddHours(-1)));
                }
            }
            await db.SaveChangesAsync();

            var dispatcher = new RecordingDispatcher(db, throwOnLiveDispatch);
            var publisher = new NotificationPublisher(db, dispatcher, NullLogger<NotificationPublisher>.Instance);
            var catalog = new CodeNotificationEventCatalog();
            var policyEngine = new NotificationPolicyEngine(
                catalog,
                new NotificationTemplateResolver(),
                new NotificationRecipientResolver(db, new PermissionService(db)));
            var processor = new AttendanceNotificationOutboxProcessor(
                db, policyEngine, publisher, TestCairoTimeZoneProvider.Instance,
                NullLogger<AttendanceNotificationOutboxProcessor>.Instance);
            return new Fixture(db, processor, dispatcher);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingDispatcher(AppDbContext db, bool throwOnDispatch) : INotificationLiveDispatcher
    {
        public List<(Guid RecipientId, NotificationSummaryDto Notification, bool PersistedBeforeDispatch)> Deliveries { get; } = [];

        public async Task SendToUserAsync(Guid recipientUserId, NotificationSummaryDto notification, CancellationToken cancellationToken = default)
        {
            if (throwOnDispatch) throw new InvalidOperationException("SignalR unavailable");
            Deliveries.Add((recipientUserId, notification, await db.Notifications.AnyAsync(item => item.Id == notification.Id, cancellationToken)));
        }

        public Task SendToCapabilityAsync(string permission, NotificationSummaryDto notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
