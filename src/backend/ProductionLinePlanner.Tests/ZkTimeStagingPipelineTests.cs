using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        }

        var first = await fixture.RunAsync();

        Assert.True(first.IsSuccess, first.Error?.Message);
        await using (var firstRead = fixture.CreateDbContext())
        {
            Assert.Equal(7, await firstRead.Workers.CountAsync());
            Assert.Equal(7, await firstRead.AttendanceRecords.CountAsync());
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

        fixture.Source.AddWorkers(Worker(1, 17252, "2429"));
        fixture.Source.AddPunches(Punch(101, 17252, "2429", 8, 5, "I"));
        publisher.Changes.Clear();

        var result = await fixture.RunAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains(101, fixture.Source.SkippedPunchIds);
        Assert.Empty(publisher.Changes);
        await using var reloaded = fixture.CreateDbContext();
        Assert.Empty(await reloaded.AttendanceRecords.ToArrayAsync());
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

        fixture.Source.AddWorkers(Worker(1, 17252, "2429"));
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
    public async Task Successful_staging_batch_publishes_one_event_per_changed_domain_type_and_replay_publishes_none()
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
        Assert.Equal(2, publisher.Changes.Count);
        Assert.Single(publisher.Changes, change => change.EntityType == ManufacturingEntityType.Worker);
        Assert.Single(publisher.Changes, change => change.EntityType == ManufacturingEntityType.AttendanceRecord);
        publisher.Changes.Clear();

        var replay = await fixture.RunAsync();

        Assert.True(replay.IsSuccess, replay.Error?.Message);
        Assert.Empty(publisher.Changes);
    }

    private static WorkerIdentitySourceItem Worker(long id, int userId, string badge, string? name = null) =>
        new(id, new AttendanceEmployeeRecord(
            userId.ToString(), 1, badge, name ?? $"Worker {badge}", true, badge), true);

    private static AttendanceSourcePunch Punch(long id, int userId, string badge, int hour, int minute, string checkType) =>
        new(id, userId, badge, new DateTime(2026, 7, 16, hour, minute, 0, DateTimeKind.Unspecified), checkType, $"source-{id}");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<AppDbContext> options;
        private readonly AttendanceSourceOptions sourceOptions;

        private Fixture(SqliteConnection anchor, DbContextOptions<AppDbContext> options, FakeStagingSource source, int maximumAttempts)
        {
            this.anchor = anchor;
            this.options = options;
            Source = source;
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
            int maximumAttempts = 5)
        {
            var connection = new SqliteConnection($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
            await connection.OpenAsync();
            connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", StringComparer.OrdinalIgnoreCase.Compare);
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
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
                        NullLogger<ManufacturingDataChangeSaveChangesInterceptor>.Instance),
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

            return new Fixture(connection, options, new FakeStagingSource(maximumAttempts), maximumAttempts);
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
                NullLogger<WorkerInitialSyncService>.Instance);
            var attendanceSync = new AttendanceSyncService(
                db,
                Source,
                Options.Create(sourceOptions),
                NullLogger<AttendanceSyncService>.Instance,
                TestCairoTimeZoneProvider.Instance,
                workerSync);
            return await attendanceSync.SyncForProductionDateAsync(ProductionDate);
        }

        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }

    private sealed class FakeStagingSource(int maximumAttempts) : IWorkerIdentitySource, IAttendanceSource
    {
        private readonly Dictionary<long, (WorkerIdentitySourceItem Item, State State, int Attempts)> workers = [];
        private readonly Dictionary<long, (AttendanceSourcePunch Punch, State State, int Attempts)> punches = [];

        public IReadOnlySet<long> ProcessedWorkerIds => workers.Where(row => row.Value.State == State.Processed).Select(row => row.Key).ToHashSet();
        public IReadOnlySet<long> ProcessedPunchIds => punches.Where(row => row.Value.State == State.Processed).Select(row => row.Key).ToHashSet();
        public IReadOnlySet<long> PendingPunchIds => punches.Where(row => row.Value.State == State.Pending).Select(row => row.Key).ToHashSet();
        public IReadOnlySet<long> SkippedPunchIds => punches.Where(row => row.Value.State == State.Skipped).Select(row => row.Key).ToHashSet();
        public IReadOnlySet<long> FailedPunchIds => punches.Where(row => row.Value.State == State.Failed).Select(row => row.Key).ToHashSet();

        public void AddWorkers(params WorkerIdentitySourceItem[] rows)
        {
            foreach (var row in rows) workers[row.SourceRecordId!.Value] = (row, State.Pending, 0);
        }

        public void AddPunches(params AttendanceSourcePunch[] rows)
        {
            foreach (var row in rows) punches[row.SourceRecordId!.Value] = (row, State.Pending, 0);
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
            var rows = punches.Values
                .Where(row => row.Punch.CheckTimeLocal >= startLocal && row.Punch.CheckTimeLocal < endLocal)
                .Select(row => row.State == State.Pending
                    ? row.Punch
                    : row.Punch with { SourceRecordId = null })
                .OrderBy(row => row.CheckTimeLocal)
                .ToArray();
            return Task.FromResult(Result<AttendanceSourceBatch>.Success(new(
                Guid.NewGuid(),
                rows.Select(row => row.UserId).Distinct().Count(),
                rows,
                true)));
        }

        public Task<Result> CompleteAsync(AttendanceSourceBatch batch, IReadOnlyCollection<SourceProcessingOutcome> outcomes, CancellationToken cancellationToken = default)
        {
            foreach (var outcome in outcomes)
            {
                var row = punches[outcome.SourceRecordId];
                punches[outcome.SourceRecordId] = (row.Punch, ResolveState(outcome, row.Attempts + 1), row.Attempts + 1);
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

        private enum State { Pending, Processed, Skipped, Failed }
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
