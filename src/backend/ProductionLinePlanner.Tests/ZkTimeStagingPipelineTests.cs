using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Realtime;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class ZkTimeStagingPipelineTests
{
    private static readonly DateOnly ProductionDate = new(2026, 7, 16);

    [Fact]
    public async Task Staging_pipeline_creates_required_workers_preserves_local_fields_and_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Source.AddWorkers(
            Worker(1, 17252, "2429"),
            Worker(2, 17253, "2430"),
            Worker(3, 17254, "2431"),
            Worker(4, 17255, "2434"),
            Worker(5, 17256, "2437"),
            Worker(6, 17257, "2436"),
            Worker(7, 3887, "1024", "External replacement name"));
        fixture.Source.AddPunches(
            Punch(101, 17252, "2429", 8, 5, "I"),
            Punch(102, 17252, "2429", 17, 10, "O"),
            Punch(103, 17253, "2430", 8, 10, "I"),
            Punch(104, 3887, "1024", 8, 15, "I"),
            Punch(105, 3887, "1024", 17, 20, "O"));

        await using (var beforeBackend = fixture.CreateDbContext())
        {
            Assert.Single(await beforeBackend.Workers.ToArrayAsync());
            Assert.Empty(await beforeBackend.AttendanceRecords.ToArrayAsync());
            Assert.Empty(await beforeBackend.AttendanceNotificationEvents.ToArrayAsync());
        }

        var first = await fixture.RunAsync();

        Assert.True(first.IsSuccess, first.Error?.Message);
        await using (var firstRead = fixture.CreateDbContext())
        {
            Assert.Equal(7, await firstRead.Workers.CountAsync());
            Assert.Equal(7, await firstRead.AttendanceRecords.CountAsync());
            Assert.Equal(5, await firstRead.AttendanceNotificationEvents.CountAsync());
            Assert.Equal(3, await firstRead.AttendanceNotificationEvents.CountAsync(item => item.AttendanceType == WorkerAttendanceNotificationType.CheckIn));
            Assert.Equal(2, await firstRead.AttendanceNotificationEvents.CountAsync(item => item.AttendanceType == WorkerAttendanceNotificationType.CheckOut));
            var importedIds = await firstRead.Workers.AsNoTracking()
                .Where(worker => worker.AttendanceUserId != "3887")
                .OrderBy(worker => worker.AttendanceUserId)
                .Select(worker => worker.AttendanceUserId!)
                .ToArrayAsync();
            Assert.Equal(["17252", "17253", "17254", "17255", "17256", "17257"], importedIds);

            var existing = await firstRead.Workers.AsNoTracking().SingleAsync(worker => worker.AttendanceUserId == "3887");
            Assert.Equal("Planner-owned name", existing.FullName);
            Assert.Equal("planner-photo.png", existing.PhotoReference);
            var existingAttendance = Assert.Single(
                await firstRead.AttendanceRecords.AsNoTracking().Where(record => record.WorkerId == existing.Id).ToArrayAsync());
            Assert.Contains("FirstInUtc", existingAttendance.SourcePayload, StringComparison.Ordinal);
            Assert.Contains("LastOutUtc", existingAttendance.SourcePayload, StringComparison.Ordinal);
        }

        fixture.Source.AddPunches(Punch(106, 17253, "2430", 17, 30, "O"));
        var second = await fixture.RunAsync();
        var third = await fixture.RunAsync();

        Assert.True(second.IsSuccess, second.Error?.Message);
        Assert.True(third.IsSuccess, third.Error?.Message);
        await using var reloaded = fixture.CreateDbContext();
        Assert.Equal(7, await reloaded.Workers.CountAsync());
        Assert.Equal(7, await reloaded.AttendanceRecords.CountAsync());
        Assert.Equal(6, await reloaded.AttendanceNotificationEvents.CountAsync());
        Assert.Equal(6, await reloaded.AttendanceNotificationEvents.Select(item => item.IdempotencyKey).Distinct().CountAsync());
        Assert.Equal(3, await reloaded.AttendanceNotificationEvents.CountAsync(item => item.AttendanceType == WorkerAttendanceNotificationType.CheckIn));
        Assert.Equal(3, await reloaded.AttendanceNotificationEvents.CountAsync(item => item.AttendanceType == WorkerAttendanceNotificationType.CheckOut));
        var worker2430 = await reloaded.Workers.AsNoTracking().SingleAsync(worker => worker.BadgeNumber == "2430");
        var worker2430Attendance = await reloaded.AttendanceRecords.AsNoTracking()
            .SingleAsync(record => record.WorkerId == worker2430.Id);
        using var payload = JsonDocument.Parse(worker2430Attendance.SourcePayload!);
        Assert.Equal("2026-07-16T05:10:00Z", payload.RootElement.GetProperty("FirstInUtc").GetDateTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'"));
        Assert.Equal("2026-07-16T14:30:00Z", payload.RootElement.GetProperty("LastOutUtc").GetDateTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'"));
        Assert.Equal(7, fixture.Source.ProcessedWorkerIds.Count);
        Assert.Equal(6, fixture.Source.ProcessedPunchIds.Count);
        Assert.Empty(fixture.Source.FailedPunchIds);
    }

    [Fact]
    public async Task Existing_notification_does_not_block_late_arriving_in_out_punches()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Source.AddWorkers(Worker(1, 3887, "1024"));
        fixture.Source.AddPunches(
            Punch(101, 3887, "1024", 8, 5, "I"),
            Punch(102, 3887, "1024", 17, 0, "O"));

        await using (var setup = fixture.CreateDbContext())
        {
            var worker = await setup.Workers.SingleAsync(item => item.AttendanceUserId == "3887");
            var record = new AttendanceRecord(
                Guid.NewGuid(), worker.Id, new DateTime(2026, 7, 16, 13, 29, 55, DateTimeKind.Utc),
                AttendanceStatus.Late, source: "Existing");
            setup.AttendanceRecords.Add(record);
            setup.AttendanceNotificationEvents.Add(new AttendanceNotificationEvent(
                Guid.NewGuid(), record.Id, worker.Id, worker.FullName, worker.EmployeeCode!,
                WorkerAttendanceNotificationType.CheckIn, record.AttendanceTimeUtc, "Existing",
                $"attendance:{record.Id:D}:CheckIn"));
            await setup.SaveChangesAsync();
        }

        var result = await fixture.RunAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        await using var reloaded = fixture.CreateDbContext();
        Assert.Equal(2, await reloaded.AttendanceNotificationEvents.CountAsync());
        Assert.Equal(2, await reloaded.AttendanceNotificationEvents.Select(item => item.IdempotencyKey).Distinct().CountAsync());
        Assert.Contains(101, fixture.Source.ProcessedPunchIds);
        Assert.Contains(102, fixture.Source.ProcessedPunchIds);
    }

    [Fact]
    public async Task Punch_type_aggregation_uses_the_first_in_and_last_out_without_inventing_a_checkout()
    {
        await using var fixture = await Fixture.CreateAsync(includeExistingWorker: false);
        fixture.Source.AddWorkers(
            Worker(1, 19, "19"),
            Worker(2, 17189, "17189"),
            Worker(3, 100, "100"),
            Worker(4, 101, "101"),
            Worker(5, 102, "102"),
            Worker(6, 103, "103"),
            Worker(7, 104, "104"),
            Worker(8, 105, "105"),
            Worker(9, 106, "106"),
            Worker(10, 107, "107"));
        fixture.Source.AddPunches(
            Punch(101, 19, "19", 7, 56, "I", 47),
            Punch(102, 19, "19", 7, 56, "I", 51),
            Punch(103, 17189, "17189", 8, 1, "I", 13),
            Punch(104, 17189, "17189", 9, 3, "I", 18),
            Punch(105, 17189, "17189", 9, 3, "O", 45),
            Punch(106, 100, "100", 8, 0, "I"),
            Punch(107, 100, "100", 17, 0, "o"),
            Punch(108, 101, "101", 8, 0, "I"),
            Punch(109, 101, "101", 16, 0, "O"),
            Punch(110, 101, "101", 17, 0, "O"),
            Punch(111, 102, "102", 17, 0, "O"),
            Punch(112, 103, "103", 8, 0, "X"),
            Punch(113, 104, "104", 8, 0, "I"),
            Punch(114, 105, "105", 8, 0, " "),
            Punch(115, 106, "106", 8, 0, "I"),
            Punch(116, 106, "106", 17, 0, "O"),
            Punch(117, 107, "107", 8, 0, "i"),
            Punch(118, 107, "107", 17, 0, "o"));

        var result = await fixture.RunAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        await using var db = fixture.CreateDbContext();
        var records = await db.AttendanceRecords.AsNoTracking().ToArrayAsync();

        AssertWindow(records, 19, "2026-07-16T04:56:47Z", null);
        AssertWindow(records, 17189, "2026-07-16T05:01:13Z", "2026-07-16T06:03:45Z");
        AssertWindow(records, 100, "2026-07-16T05:00:00Z", "2026-07-16T14:00:00Z");
        AssertWindow(records, 101, "2026-07-16T05:00:00Z", "2026-07-16T14:00:00Z");
        AssertWindow(records, 104, "2026-07-16T05:00:00Z", null);
        AssertWindow(records, 106, "2026-07-16T05:00:00Z", "2026-07-16T14:00:00Z");
        AssertWindow(records, 107, "2026-07-16T05:00:00Z", "2026-07-16T14:00:00Z");

        var outOnlyWorker = await db.Workers.SingleAsync(worker => worker.AttendanceUserId == "102");
        var outOnlyRecord = Assert.Single(records, record => record.WorkerId == outOnlyWorker.Id);
        Assert.Equal(AttendanceStatus.Absent, outOnlyRecord.AttendanceStatus);
        Assert.Null(outOnlyRecord.SourcePayload);
        Assert.Contains(111, fixture.Source.ProcessedPunchIds);
        Assert.Contains(112, fixture.Source.FailedPunchIds);
        Assert.Contains(114, fixture.Source.FailedPunchIds);
    }

    [Fact]
    public async Task Unresolved_punch_is_retried_without_blocking_batch_and_can_be_processed_after_worker_arrives()
    {
        await using var fixture = await Fixture.CreateAsync(includeExistingWorker: false);
        fixture.Source.AddWorkers(Worker(1, 17252, "2429"));
        fixture.Source.AddPunches(
            Punch(101, 17252, "2429", 8, 5, "I"),
            Punch(102, 19999, "2999", 8, 10, "I"));

        var first = await fixture.RunAsync();

        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Contains(101, fixture.Source.ProcessedPunchIds);
        Assert.Contains(102, fixture.Source.PendingPunchIds);
        await using (var firstRead = fixture.CreateDbContext())
        {
            Assert.Single(await firstRead.Workers.ToArrayAsync());
            Assert.Single(await firstRead.AttendanceRecords.ToArrayAsync());
        }

        fixture.Source.AddWorkers(Worker(2, 19999, "2999"));
        var retry = await fixture.RunAsync();

        Assert.True(retry.IsSuccess, retry.Error?.Message);
        Assert.Contains(102, fixture.Source.ProcessedPunchIds);
        Assert.DoesNotContain(102, fixture.Source.PendingPunchIds);
        await using var reloaded = fixture.CreateDbContext();
        Assert.Equal(2, await reloaded.Workers.CountAsync());
        Assert.Equal(2, await reloaded.AttendanceRecords.CountAsync());
    }

    [Fact]
    public async Task Inactive_worker_attendance_is_skipped_without_publishing_an_attendance_refresh()
    {
        var publisher = new RecordingPublisher();
        await using var fixture = await Fixture.CreateAsync(includeExistingWorker: false, publisher: publisher);
        await using (var setup = fixture.CreateDbContext())
        {
            setup.Workers.Add(new Worker(
                Guid.NewGuid(), "2429", "Inactive planner worker", "17252", "2429", isActive: false,
                lastExternalSyncAt: DateTime.UtcNow));
            await setup.SaveChangesAsync();
        }

        fixture.Source.AddWorkers(Worker(1, 17252, "2429", isCurrentWorker: false));
        fixture.Source.AddPunches(Punch(101, 17252, "2429", 8, 5, "I"));
        publisher.Changes.Clear();

        var result = await fixture.RunAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains(101, fixture.Source.SkippedPunchIds);
        Assert.DoesNotContain(publisher.Changes, change => change.EntityType == ManufacturingEntityType.AttendanceRecord);
        Assert.Single(publisher.Changes, change => change.EntityType == ManufacturingEntityType.Worker);
        await using var reloaded = fixture.CreateDbContext();
        Assert.Empty(await reloaded.AttendanceRecords.ToArrayAsync());

        var claimedBatches = fixture.Source.AttendanceClaims.Count;
        var publishedChanges = publisher.Changes.Count;
        var publishedSyncChanges = publisher.Changes.Count(change => change.EntityType == ManufacturingEntityType.AttendanceSyncState);
        var replay = await fixture.RunAsync();

        Assert.True(replay.IsSuccess, replay.Error?.Message);
        Assert.Equal(claimedBatches, fixture.Source.AttendanceClaims.Count);
        Assert.Equal(publishedChanges + 1, publisher.Changes.Count);
        Assert.Equal(publishedSyncChanges + 1, publisher.Changes.Count(change => change.EntityType == ManufacturingEntityType.AttendanceSyncState));
        Assert.Single(publisher.Changes, change => change.EntityType == ManufacturingEntityType.Worker);
        Assert.Contains(101, fixture.Source.SkippedPunchIds);
    }

    [Fact]
    public async Task Unresolved_worker_becomes_failed_only_after_maximum_attempts()
    {
        await using var fixture = await Fixture.CreateAsync(maximumAttempts: 2, includeExistingWorker: false);
        fixture.Source.AddPunches(Punch(101, 19999, "2999", 8, 10, "I"));

        var first = await fixture.RunAsync();
        var second = await fixture.RunAsync();

        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.True(second.IsSuccess, second.Error?.Message);
        Assert.Contains(101, fixture.Source.FailedPunchIds);
        Assert.DoesNotContain(101, fixture.Source.PendingPunchIds);
        Assert.Equal(2, fixture.Source.GetPunchInbox(101).AttemptCount);
    }

    [Fact]
    public async Task Attendance_after_employment_end_is_skipped()
    {
        await using var fixture = await Fixture.CreateAsync(includeExistingWorker: false);
        await using (var setup = fixture.CreateDbContext())
        {
            setup.Workers.Add(new Worker(
                Guid.NewGuid(), "2429", "Ended planner worker", "17252", "2429",
                employmentEndDate: new DateTime(2026, 7, 15), lastExternalSyncAt: DateTime.UtcNow));
            await setup.SaveChangesAsync();
        }

        fixture.Source.AddWorkers(Worker(1, 17252, "2429", isCurrentWorker: false));
        fixture.Source.AddPunches(Punch(101, 17252, "2429", 8, 5, "I"));

        var result = await fixture.RunAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains(101, fixture.Source.SkippedPunchIds);
        await using var reloaded = fixture.CreateDbContext();
        Assert.Empty(await reloaded.AttendanceRecords.ToArrayAsync());
    }

    [Fact]
    public async Task Ambiguous_badge_identity_is_failed_without_creating_an_attendance_summary()
    {
        await using var fixture = await Fixture.CreateAsync(includeExistingWorker: false);
        await using (var setup = fixture.CreateDbContext())
        {
            setup.Workers.AddRange(
                new Worker(Guid.NewGuid(), "A-2999", "First candidate", "10001", "2999"),
                new Worker(Guid.NewGuid(), "B-2999", "Second candidate", "10002", "2999"));
            await setup.SaveChangesAsync();
        }

        fixture.Source.AddPunches(Punch(101, 19999, "2999", 8, 10, "I"));

        var result = await fixture.RunAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains(101, fixture.Source.FailedPunchIds);
        await using var reloaded = fixture.CreateDbContext();
        Assert.DoesNotContain(await reloaded.AttendanceRecords.ToArrayAsync(), record => record.BadgeNumber == "2999" && record.AttendanceUserId == "19999");
    }

    [Fact]
    public async Task Conflicting_source_user_and_badge_identities_are_failed()
    {
        await using var fixture = await Fixture.CreateAsync(includeExistingWorker: false);
        await using (var setup = fixture.CreateDbContext())
        {
            setup.Workers.AddRange(
                new Worker(Guid.NewGuid(), "A-100", "Attendance identity candidate", "17252", "2429"),
                new Worker(Guid.NewGuid(), "B-200", "Badge identity candidate", "17253", "2430"));
            await setup.SaveChangesAsync();
        }

        fixture.Source.AddPunches(Punch(101, 17252, "2430", 8, 10, "I"));

        var result = await fixture.RunAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains(101, fixture.Source.FailedPunchIds);
    }

    [Fact]
    public async Task Successful_staging_batch_publishes_domain_changes_and_replay_only_refreshes_sync_freshness()
    {
        var publisher = new RecordingPublisher();
        await using var fixture = await Fixture.CreateAsync(publisher: publisher);
        publisher.Changes.Clear();
        fixture.Source.AddWorkers(
            Worker(1, 17252, "2429"),
            Worker(2, 3887, "1024", "External replacement name"));
        fixture.Source.AddPunches(
            Punch(101, 17252, "2429", 8, 5, "I"),
            Punch(102, 3887, "1024", 8, 15, "I"));

        var first = await fixture.RunAsync();

        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Equal(3, publisher.Changes.Count);
        var workerChange = Assert.Single(publisher.Changes, change => change.EntityType == ManufacturingEntityType.Worker);
        var attendanceChange = Assert.Single(publisher.Changes, change => change.EntityType == ManufacturingEntityType.AttendanceRecord);
        var syncChange = Assert.Single(publisher.Changes, change => change.EntityType == ManufacturingEntityType.AttendanceSyncState);
        Assert.Equal("ZkTimeSync", workerChange.Source);
        Assert.Equal("ZkTimeSync", attendanceChange.Source);
        Assert.Equal(ProductionDate, attendanceChange.ProductionDate);
        Assert.Equal(2, attendanceChange.AddedAttendanceCount);
        Assert.Equal(0, attendanceChange.UpdatedAttendanceCount);
        Assert.Equal("ZkTimeSync", syncChange.Source);
        Assert.Equal(ProductionDate, syncChange.ProductionDate);
        publisher.Changes.Clear();

        var replay = await fixture.RunAsync();

        Assert.True(replay.IsSuccess, replay.Error?.Message);
        Assert.Collection(publisher.Changes, change => Assert.Equal(ManufacturingEntityType.AttendanceSyncState, change.EntityType));
    }

    [Fact]
    public async Task Claimed_rows_receive_a_lease_and_every_claimed_row_is_completed_once()
    {
        await using var fixture = await Fixture.CreateAsync(includeExistingWorker: false);
        fixture.Source.AddWorkers(Worker(1, 17252, "2429"));
        fixture.Source.AddPunches(
            Punch(101, 17252, "2429", 8, 5, "I"),
            Punch(102, 17252, "2429", 17, 10, "O"));

        var result = await fixture.RunAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Single(fixture.Source.AttendanceClaims);
        var claim = fixture.Source.AttendanceClaims.Single();
        Assert.NotEqual(Guid.Empty, claim.LeaseId);
        Assert.Equal([101L, 102L], claim.InboxIds);
        Assert.Equal([101L, 102L], fixture.Source.AttendanceCompletions.Single().InboxIds);
        Assert.All([101L, 102L], inboxId =>
        {
            var row = fixture.Source.GetPunchInbox(inboxId);
            Assert.Equal(FakeStagingSource.State.Processed, row.State);
            Assert.Equal(1, row.AttemptCount);
            Assert.Null(row.ProcessingLeaseId);
        });
    }

    [Fact]
    public async Task Domain_failure_after_claim_releases_every_row_for_retry_without_leaving_a_lease()
    {
        await using var fixture = await Fixture.CreateAsync(
            includeExistingWorker: false,
            throwDuringAttendancePersistence: true);
        fixture.Source.AddWorkers(Worker(1, 17252, "2429"));
        fixture.Source.AddPunches(
            Punch(101, 17252, "2429", 8, 5, "I"),
            Punch(102, 17252, "2429", 17, 10, "O"));

        var result = await fixture.RunAsync();

        Assert.True(result.IsFailure);
        await using (var reloaded = fixture.CreateDbContext())
        {
            Assert.Empty(await reloaded.AttendanceRecords.ToArrayAsync());
            Assert.Empty(await reloaded.AttendanceNotificationEvents.ToArrayAsync());
        }
        Assert.All([101L, 102L], inboxId =>
        {
            var row = fixture.Source.GetPunchInbox(inboxId);
            Assert.Equal(FakeStagingSource.State.Pending, row.State);
            Assert.Equal(1, row.AttemptCount);
            Assert.Null(row.ProcessingLeaseId);
            Assert.Equal("Synthetic attendance persistence failure.", row.ResolutionDetails);
        });
        Assert.Equal([101L, 102L], fixture.Source.AttendanceCompletions.Single().InboxIds);
    }

    private static WorkerIdentitySourceItem Worker(
        long id,
        int userId,
        string badge,
        string? name = null,
        bool isCurrentWorker = true) =>
        new(id, new AttendanceEmployeeRecord(
            userId.ToString(), isCurrentWorker ? 1 : 2, badge, name ?? $"Worker {badge}", isCurrentWorker, badge,
            SourceDefaultDepartmentId: isCurrentWorker ? 1 : 2, IsCurrentWorker: isCurrentWorker), true);

    private static void AssertWindow(IReadOnlyCollection<AttendanceRecord> records, int attendanceUserId, string firstInUtc, string? lastOutUtc)
    {
        var record = records.Single(item => item.AttendanceUserId == attendanceUserId.ToString());
        using var payload = JsonDocument.Parse(record.SourcePayload!);
        Assert.Equal(firstInUtc, payload.RootElement.GetProperty("FirstInUtc").GetDateTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'"));
        var lastOut = payload.RootElement.GetProperty("LastOutUtc");
        if (lastOutUtc is null)
        {
            Assert.Equal(JsonValueKind.Null, lastOut.ValueKind);
        }
        else
        {
            Assert.Equal(lastOutUtc, lastOut.GetDateTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'"));
        }
    }

    private static AttendanceSourcePunch Punch(long id, int userId, string badge, int hour, int minute, string checkType, int second = 0) =>
        new(id, userId, badge, new DateTime(2026, 7, 16, hour, minute, second, DateTimeKind.Unspecified), checkType, $"source-{id}");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<AppDbContext> options;
        private readonly AttendanceSourceOptions sourceOptions;
        private readonly ManufacturingRealtimeChangeContext realtimeChangeContext;

        private Fixture(SqliteConnection anchor, DbContextOptions<AppDbContext> options, FakeStagingSource source, int maximumAttempts, ManufacturingRealtimeChangeContext realtimeChangeContext)
        {
            this.anchor = anchor;
            this.options = options;
            Source = source;
            this.realtimeChangeContext = realtimeChangeContext;
            sourceOptions = new AttendanceSourceOptions
            {
                Mode = AttendanceSourceOptions.StagingMode,
                MaxProcessingAttempts = maximumAttempts
            };
        }

        public FakeStagingSource Source { get; }

        public static async Task<Fixture> CreateAsync(
            bool includeExistingWorker = true,
            IManufacturingDataChangePublisher? publisher = null,
            int maximumAttempts = 5,
            bool throwDuringAttendancePersistence = false)
        {
            var connection = new SqliteConnection($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
            await connection.OpenAsync();
            connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", StringComparer.OrdinalIgnoreCase.Compare);
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
            var realtimeChangeContext = new ManufacturingRealtimeChangeContext();
            if (throwDuringAttendancePersistence)
            {
                optionsBuilder.AddInterceptors(new ThrowOnAttendancePersistenceInterceptor());
            }
            if (publisher is not null)
            {
                var coordinator = new ManufacturingDataChangeTransactionCoordinator(
                    publisher,
                    NullLogger<ManufacturingDataChangeTransactionCoordinator>.Instance);
                optionsBuilder.AddInterceptors(
                    new ManufacturingDataChangeSaveChangesInterceptor(
                        publisher,
                        new CurrentUserStub(),
                        new CorrelationStub(),
                        coordinator,
                        NullLogger<ManufacturingDataChangeSaveChangesInterceptor>.Instance,
                        realtimeChangeContext),
                    new ManufacturingDataChangeTransactionInterceptor(coordinator));
            }
            var options = optionsBuilder.Options;
            await using var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            if (includeExistingWorker)
            {
                db.Workers.Add(new Worker(
                    Guid.NewGuid(), "1024", "Planner-owned name", "3887", "1024",
                    photoReference: "planner-photo.png"));
                await db.SaveChangesAsync();
            }

            return new Fixture(connection, options, new FakeStagingSource(maximumAttempts), maximumAttempts, realtimeChangeContext);
        }

        public AppDbContext CreateDbContext() => new(options);

        public async Task<Result<ProductionLinePlanner.Application.DTOs.AttendanceSyncResultDto>> RunAsync()
        {
            await using var db = CreateDbContext();
            var workerSync = new WorkerInitialSyncService(
                db,
                Source,
                new WorkerSyncPolicy(),
                new AuthoritativeWorkerSnapshotValidator(),
                new RecordingAuditEngine(),
                NullLogger<WorkerInitialSyncService>.Instance,
                realtimeChangeContext);
            var attendanceSync = new AttendanceSyncService(
                db,
                Source,
                Options.Create(sourceOptions),
                NullLogger<AttendanceSyncService>.Instance,
                TestCairoTimeZoneProvider.Instance,
                workerSync,
                realtimeChangeContext);
            return await attendanceSync.SyncForProductionDateAsync(ProductionDate);
        }

        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }

    private sealed class FakeStagingSource(int maximumAttempts) : IWorkerIdentitySource, IAttendanceSource
    {
        private readonly Dictionary<long, (WorkerIdentitySourceItem Item, State State, int Attempts)> workers = [];
        private readonly Dictionary<long, PunchInboxRow> punches = [];

        public IReadOnlySet<long> ProcessedWorkerIds => workers.Where(row => row.Value.State == State.Processed).Select(row => row.Key).ToHashSet();
        public IReadOnlySet<long> ProcessedPunchIds => punches.Where(row => row.Value.State == State.Processed).Select(row => row.Key).ToHashSet();
        public IReadOnlySet<long> PendingPunchIds => punches.Where(row => row.Value.State == State.Pending).Select(row => row.Key).ToHashSet();
        public IReadOnlySet<long> SkippedPunchIds => punches.Where(row => row.Value.State == State.Skipped).Select(row => row.Key).ToHashSet();
        public IReadOnlySet<long> FailedPunchIds => punches.Where(row => row.Value.State == State.Failed).Select(row => row.Key).ToHashSet();
        public List<AttendanceClaim> AttendanceClaims { get; } = [];
        public List<AttendanceCompletion> AttendanceCompletions { get; } = [];

        public PunchInboxSnapshot GetPunchInbox(long inboxId)
        {
            var row = punches[inboxId];
            return new PunchInboxSnapshot(row.State, row.Attempts, row.ProcessingLeaseId, row.ResolutionDetails);
        }

        public void AddWorkers(params WorkerIdentitySourceItem[] rows)
        {
            foreach (var row in rows) workers[row.SourceRecordId!.Value] = (row, State.Pending, 0);
        }

        public void AddPunches(params AttendanceSourcePunch[] rows)
        {
            foreach (var row in rows) punches[row.SourceRecordId!.Value] = new PunchInboxRow(row);
        }

        public Task<Result<AttendanceEmployeeRecord?>> GetByAttendanceUserIdAsync(string attendanceUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AttendanceEmployeeRecord?>.Success(
                workers.Values.Select(row => row.Item.Worker).SingleOrDefault(worker => worker.AttendanceUserId == attendanceUserId)));

        public Task<Result<AttendanceEmployeeRecord[]>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AttendanceEmployeeRecord[]>.Success(workers.Values.Select(row => row.Item.Worker).ToArray()));

        public Task<Result<WorkerIdentitySourceBatch>> ReadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<WorkerIdentitySourceBatch>.Success(new(
                null,
                workers.Values.Select(row => row.Item with { IsClaimed = false }).ToArray(),
                true)));

        public Task<Result<WorkerIdentitySourceBatch>> ClaimBatchAsync(CancellationToken cancellationToken = default)
        {
            var lease = Guid.NewGuid();
            var items = workers.Select(row => row.Value.Item with
            {
                IsClaimed = row.Value.State == State.Pending
            }).ToArray();
            return Task.FromResult(Result<WorkerIdentitySourceBatch>.Success(new(lease, items, true)));
        }

        public Task<Result> CompleteBatchAsync(WorkerIdentitySourceBatch batch, IReadOnlyCollection<SourceProcessingOutcome> outcomes, CancellationToken cancellationToken = default)
        {
            foreach (var outcome in outcomes)
            {
                var row = workers[outcome.SourceRecordId];
                workers[outcome.SourceRecordId] = (row.Item, ResolveState(outcome, row.Attempts + 1), row.Attempts + 1);
            }
            return Task.FromResult(Result.Success());
        }

        public Task<Result<AttendanceSourceBatch>> ClaimAsync(DateTime startLocal, DateTime endLocal, CancellationToken cancellationToken = default)
        {
            var leaseId = Guid.NewGuid();
            var claimed = punches
                .Where(row => row.Value.State == State.Pending &&
                              row.Value.Punch.CheckTimeLocal >= startLocal &&
                              row.Value.Punch.CheckTimeLocal < endLocal)
                .Select(row => row.Key)
                .ToHashSet();
            foreach (var inboxId in claimed)
            {
                var row = punches[inboxId];
                row.State = State.Processing;
                row.Attempts++;
                row.ProcessingLeaseId = leaseId;
            }

            var rows = punches.Values
                .Where(row => row.Punch.CheckTimeLocal >= startLocal && row.Punch.CheckTimeLocal < endLocal)
                .Select(row => claimed.Contains(row.Punch.SourceRecordId!.Value)
                    ? row.Punch
                    : row.Punch with { SourceRecordId = null })
                .OrderBy(row => row.CheckTimeLocal)
                .ToArray();
            if (claimed.Count > 0)
            {
                AttendanceClaims.Add(new AttendanceClaim(leaseId, claimed.OrderBy(inboxId => inboxId).ToArray()));
            }
            return Task.FromResult(Result<AttendanceSourceBatch>.Success(new(
                claimed.Count > 0 ? leaseId : null,
                rows.Select(row => row.UserId).Distinct().Count(),
                rows,
                true)));
        }

        public Task<Result> CompleteAsync(AttendanceSourceBatch batch, IReadOnlyCollection<SourceProcessingOutcome> outcomes, CancellationToken cancellationToken = default)
        {
            foreach (var outcome in outcomes)
            {
                var row = punches[outcome.SourceRecordId];
                if (row.State != State.Processing || row.ProcessingLeaseId != batch.LeaseId)
                {
                    continue;
                }

                row.State = ResolveState(outcome, row.Attempts);
                row.ProcessingLeaseId = null;
                row.ResolutionDetails = outcome.ResolutionDetails;
            }
            if (batch.LeaseId.HasValue && outcomes.Count > 0)
            {
                AttendanceCompletions.Add(new AttendanceCompletion(
                    batch.LeaseId.Value,
                    outcomes.Select(outcome => outcome.SourceRecordId).OrderBy(inboxId => inboxId).ToArray()));
            }
            return Task.FromResult(Result.Success());
        }

        private State ResolveState(SourceProcessingOutcome outcome, int attempts) => outcome.Disposition switch
        {
            SourceProcessingDisposition.Processed => State.Processed,
            SourceProcessingDisposition.Skipped => State.Skipped,
            SourceProcessingDisposition.Failed => State.Failed,
            SourceProcessingDisposition.Pending when attempts >= maximumAttempts => State.Failed,
            _ => State.Pending
        };

        private sealed class PunchInboxRow(AttendanceSourcePunch punch)
        {
            public AttendanceSourcePunch Punch { get; } = punch;
            public State State { get; set; } = State.Pending;
            public int Attempts { get; set; }
            public Guid? ProcessingLeaseId { get; set; }
            public string? ResolutionDetails { get; set; }
        }

        public sealed record AttendanceClaim(Guid LeaseId, long[] InboxIds);
        public sealed record AttendanceCompletion(Guid LeaseId, long[] InboxIds);
        public sealed record PunchInboxSnapshot(State State, int AttemptCount, Guid? ProcessingLeaseId, string? ResolutionDetails);
        public enum State { Pending, Processing, Processed, Skipped, Failed }
    }

    private sealed class ThrowOnAttendancePersistenceInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            if (eventData.Context?.ChangeTracker.Entries<AttendanceRecord>().Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new InvalidOperationException("Synthetic attendance persistence failure.");
            }

            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var intercepted = SavingChanges(eventData, result);
            return ValueTask.FromResult(intercepted);
        }
    }

    private sealed class RecordingPublisher : IManufacturingDataChangePublisher
    {
        public List<ManufacturingDataChanged> Changes { get; } = [];

        public Task PublishAsync(ManufacturingDataChanged change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }

    private sealed class CurrentUserStub : ICurrentUserService
    {
        public Guid? UserId => null;
        public string? UserName => null;
        public bool IsAuthenticated => false;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class CorrelationStub : IManufacturingRealtimeCorrelationContext
    {
        public string? CorrelationId => null;
    }
}
