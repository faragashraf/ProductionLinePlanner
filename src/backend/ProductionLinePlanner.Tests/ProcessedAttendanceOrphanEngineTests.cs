using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class ProcessedAttendanceOrphanEngineTests
{
    private static readonly DateOnly July29 = new(2026, 7, 29);

    [Fact]
    public async Task Detector_finds_true_processed_orphan_and_groups_by_worker_and_operational_date()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = await fixture.AddWorkerAsync("3913", "1042", "Regression worker 1042");
        fixture.Store.AddProcessed(Row(5380, 3913, "1042", July29, 7, 25, "I"));

        var result = await fixture.Engine.PreviewAsync(Query());

        Assert.True(result.IsSuccess, result.Error?.Message);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(5380, item.InboxId);
        Assert.Equal(worker.Id, item.WorkerId);
        Assert.Equal("ProcessedWithoutAttendance", item.ReasonCode);
        Assert.Equal(new DateTime(2026, 7, 29, 4, 25, 0, DateTimeKind.Utc), item.ExpectedAttendanceTimeUtc);
        Assert.Equal(July29, item.OperationalDate);
        Assert.Equal(1, Assert.Single(result.Value.Groups).Count);
    }

    [Fact]
    public async Task Detector_excludes_valid_processed_row_with_exact_persisted_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = await fixture.AddWorkerAsync("3913", "1042", "Mapped worker");
        var row = Row(5380, 3913, "1042", July29, 7, 25, "I");
        fixture.Store.AddProcessed(row);
        await fixture.AddExactRecordAsync(worker, row);

        var result = await fixture.Engine.PreviewAsync(Query());

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task Preview_is_default_and_performs_no_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddWorkerAsync("3913", "1042", "Mapped worker");
        fixture.Store.AddProcessed(Row(5380, 3913, "1042", July29, 7, 25, "I"));

        var result = await fixture.Engine.RepairAsync(Guid.NewGuid(), new(
            July29, July29, MaximumRows: 10));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.False(result.Value!.Executed);
        Assert.Equal("Processed", fixture.Store.Get(5380).ProcessingStatus);
        Assert.Equal(0, fixture.Store.WriteCount);
        Assert.Equal(0, fixture.Sync.CallCount);
    }

    [Fact]
    public async Task Execute_replays_only_selected_orphans_and_creates_attendance_exactly_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddWorkerAsync("3913", "1042", "First worker");
        await fixture.AddWorkerAsync("3914", "1043", "Second worker");
        fixture.Store.AddProcessed(Row(5380, 3913, "1042", July29, 7, 25, "I"));
        fixture.Store.AddProcessed(Row(5381, 3914, "1043", July29, 7, 30, "I"));

        var result = await fixture.Engine.RepairAsync(Guid.NewGuid(), new(
            July29,
            July29,
            MaximumRows: 10,
            Execute: true,
            Confirmation: ProcessedAttendanceOrphanEngine.ExecuteConfirmation,
            InboxIds: [5380]));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("Repaired", Assert.Single(result.Value!.Results).Result);
        Assert.Equal("Processed", fixture.Store.Get(5380).ProcessingStatus);
        Assert.Equal("Imported", fixture.Store.Get(5380).ResolutionCode);
        Assert.Equal("Processed", fixture.Store.Get(5381).ProcessingStatus);
        Assert.Null(fixture.Store.Get(5381).ResolutionCode);
        Assert.Equal(1, fixture.Sync.CallCount);
        Assert.Equal(1, await fixture.Db.AttendanceRecords.CountAsync());
    }

    [Fact]
    public async Task Repeated_and_concurrent_repair_remains_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddWorkerAsync("3913", "1042", "Mapped worker");
        var first = Row(5380, 3913, "1042", July29, 7, 25, "I");
        var replay = Row(5381, 3913, "1042", July29, 7, 25, "I") with { SourceRawId = first.SourceRawId };
        fixture.Store.AddProcessed(first);
        fixture.Store.AddProcessed(replay);

        var result = await fixture.Engine.RepairAsync(Guid.NewGuid(), new(
            July29,
            July29,
            MaximumRows: 10,
            Execute: true,
            Confirmation: ProcessedAttendanceOrphanEngine.ExecuteConfirmation));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value!.Results.Count);
        Assert.Contains(result.Value.Results, item => item.Result == "Repaired");
        Assert.Contains(result.Value.Results, item => item.Result == "AlreadyImported");
        Assert.Equal(1, await fixture.Db.AttendanceRecords.CountAsync());

        var secondPreview = await fixture.Engine.PreviewAsync(Query());
        Assert.True(secondPreview.IsSuccess);
        Assert.Empty(secondPreview.Value!.Items);
    }

    [Fact]
    public async Task Execute_rechecks_rowversion_and_does_not_overwrite_concurrent_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddWorkerAsync("3913", "1042", "Mapped worker");
        fixture.Store.AddProcessed(Row(5380, 3913, "1042", July29, 7, 25, "I"));
        fixture.Store.FailNextRequeue = true;

        var result = await fixture.Engine.RepairAsync(Guid.NewGuid(), ExecuteRequest());

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("NoLongerOrphan", Assert.Single(result.Value!.Results).Result);
        Assert.Equal(0, fixture.Sync.CallCount);
        Assert.Empty(await fixture.Db.AttendanceRecords.ToArrayAsync());
    }

    [Fact]
    public async Task Detector_honors_badge_filter_across_multiple_workers_and_dates()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddWorkerAsync("3913", "1042", "First worker");
        await fixture.AddWorkerAsync("3914", "1043", "Second worker");
        fixture.Store.AddProcessed(Row(5380, 3913, "1042", July29, 7, 25, "I"));
        fixture.Store.AddProcessed(Row(6139, 3913, "1042", July29.AddDays(1), 8, 4, "O"));
        fixture.Store.AddProcessed(Row(6140, 3914, "1043", July29.AddDays(1), 8, 5, "I"));

        var result = await fixture.Engine.PreviewAsync(new(
            July29, July29.AddDays(1), BadgeNumber: "1042", MaximumRows: 10));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal([5380L, 6139L], result.Value!.Items.Select(item => item.InboxId).ToArray());
        Assert.Equal(2, result.Value.Groups.Count);
    }

    [Fact]
    public async Task Batch_limit_and_explicit_confirmation_are_enforced()
    {
        await using var fixture = await Fixture.CreateAsync();

        var tooLarge = await fixture.Engine.PreviewAsync(new(July29, July29, MaximumRows: 101));
        Assert.True(tooLarge.IsFailure);
        Assert.Equal("ValidationError", tooLarge.Error!.Code);

        var noConfirmation = await fixture.Engine.RepairAsync(Guid.NewGuid(), new(
            July29, July29, MaximumRows: 10, Execute: true));
        Assert.True(noConfirmation.IsFailure);
        Assert.Equal("ConfirmationRequired", noConfirmation.Error!.Code);
    }

    [Fact]
    public async Task Five_am_boundary_assigns_pre_boundary_punch_to_previous_operational_date()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddWorkerAsync("3913", "1042", "Mapped worker");
        fixture.Store.AddProcessed(Row(5380, 3913, "1042", July29, 4, 59, "I"));

        var result = await fixture.Engine.PreviewAsync(new(July29.AddDays(-1), July29, MaximumRows: 10));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(July29.AddDays(-1), Assert.Single(result.Value!.Items).OperationalDate);
    }

    private static ProcessedAttendanceOrphanQuery Query() => new(July29, July29, MaximumRows: 10);

    private static ProcessedAttendanceOrphanRepairRequest ExecuteRequest() => new(
        July29,
        July29,
        MaximumRows: 10,
        Execute: true,
        Confirmation: ProcessedAttendanceOrphanEngine.ExecuteConfirmation);

    private static ProcessedAttendanceInboxRow Row(
        long inboxId,
        int sourceUserId,
        string badge,
        DateOnly date,
        int hour,
        int minute,
        string type) => new(
            inboxId,
            sourceUserId,
            badge,
            date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified),
            type,
            $"source-{inboxId}",
            "Processed",
            1,
            null,
            null,
            BitConverter.GetBytes(inboxId));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private Fixture(SqliteConnection connection, AppDbContext db, FakeInboxStore store, FakeRepairSync sync)
        {
            this.connection = connection;
            Db = db;
            Store = store;
            Sync = sync;
            var options = Options.Create(new AttendanceSourceOptions
            {
                Mode = AttendanceSourceOptions.StagingMode,
                SourceName = "AttendanceSync",
                WorkdayBoundaryTime = new TimeSpan(5, 0, 0)
            });
            var policy = new AttendanceWorkdayPolicy(options, TestCairoTimeZoneProvider.Instance);
            Engine = new ProcessedAttendanceOrphanEngine(
                db,
                store,
                policy,
                TestCairoTimeZoneProvider.Instance,
                options,
                sync,
                new RecordingAuditEngine(),
                NullLogger<ProcessedAttendanceOrphanEngine>.Instance);
            sync.Configure(db, store, policy);
        }

        public AppDbContext Db { get; }
        public FakeInboxStore Store { get; }
        public FakeRepairSync Sync { get; }
        public ProcessedAttendanceOrphanEngine Engine { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", StringComparer.OrdinalIgnoreCase.Compare);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var store = new FakeInboxStore();
            return new Fixture(connection, db, store, new FakeRepairSync());
        }

        public async Task<Worker> AddWorkerAsync(string attendanceUserId, string badge, string name)
        {
            var worker = new Worker(Guid.NewGuid(), badge, name, attendanceUserId, badge);
            Db.Workers.Add(worker);
            await Db.SaveChangesAsync();
            return worker;
        }

        public async Task AddExactRecordAsync(Worker worker, ProcessedAttendanceInboxRow row)
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(row.SourceCheckTimeLocal, TestCairoTimeZoneProvider.Instance.TimeZone);
            var isIn = row.SourceCheckType.Equals("I", StringComparison.OrdinalIgnoreCase);
            Db.AttendanceRecords.Add(new AttendanceRecord(
                Guid.NewGuid(),
                worker.Id,
                utc,
                AttendanceStatus.Present,
                "AttendanceSync",
                JsonSerializer.Serialize(new { FirstInUtc = isIn ? utc : (DateTime?)null, LastOutUtc = isIn ? (DateTime?)null : utc }),
                isIn ? row.SourceRawId : "original-check-in",
                worker.AttendanceUserId,
                worker.BadgeNumber));
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeInboxStore : IProcessedAttendanceInboxStore
    {
        private readonly Dictionary<long, ProcessedAttendanceInboxRow> rows = [];
        public int WriteCount { get; private set; }
        public bool FailNextRequeue { get; set; }

        public void AddProcessed(ProcessedAttendanceInboxRow row) => rows[row.InboxId] = row;
        public ProcessedAttendanceInboxRow Get(long id) => rows[id];
        public IEnumerable<ProcessedAttendanceInboxRow> Pending => rows.Values.Where(row => row.ProcessingStatus == "Pending");

        public Task<IReadOnlyList<ProcessedAttendanceInboxRow>> ReadProcessedAsync(
            DateTime fromLocal,
            DateTime toLocal,
            int? sourceUserId,
            string? badgeNumber,
            int maximumRows,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProcessedAttendanceInboxRow>>(rows.Values
                .Where(row => row.ProcessingStatus == "Processed"
                    && row.SourceCheckTimeLocal >= fromLocal
                    && row.SourceCheckTimeLocal < toLocal
                    && (!sourceUserId.HasValue || row.SourceUserId == sourceUserId)
                    && (badgeNumber is null || row.BadgeNumber == badgeNumber))
                .OrderBy(row => row.SourceCheckTimeLocal)
                .ThenBy(row => row.InboxId)
                .Take(maximumRows)
                .ToArray());

        public Task<ProcessedAttendanceInboxRow?> ReadForUpdateAsync(long inboxId, CancellationToken cancellationToken) =>
            Task.FromResult(rows.GetValueOrDefault(inboxId));

        public Task<bool> RequeueAsync(ProcessedAttendanceInboxRow row, string details, CancellationToken cancellationToken)
        {
            if (FailNextRequeue)
            {
                FailNextRequeue = false;
                return Task.FromResult(false);
            }
            if (!rows.TryGetValue(row.InboxId, out var current) || !current.RowVersion.SequenceEqual(row.RowVersion)) return Task.FromResult(false);
            rows[row.InboxId] = current with
            {
                ProcessingStatus = "Pending",
                ResolutionCode = "ProcessedOrphanRequeued",
                ResolutionDetails = details,
                RowVersion = NextVersion(current.RowVersion)
            };
            WriteCount++;
            return Task.FromResult(true);
        }

        public Task<bool> MarkAlreadyImportedAsync(ProcessedAttendanceInboxRow row, string details, CancellationToken cancellationToken)
        {
            if (!rows.TryGetValue(row.InboxId, out var current) || !current.RowVersion.SequenceEqual(row.RowVersion)) return Task.FromResult(false);
            rows[row.InboxId] = current with
            {
                ResolutionCode = "AlreadyImported",
                ResolutionDetails = details,
                RowVersion = NextVersion(current.RowVersion)
            };
            WriteCount++;
            return Task.FromResult(true);
        }

        public Task<ProcessedAttendanceInboxState?> ReadStateAsync(long inboxId, CancellationToken cancellationToken)
        {
            var row = rows.GetValueOrDefault(inboxId);
            return Task.FromResult(row is null ? null : new ProcessedAttendanceInboxState(row.InboxId, row.ProcessingStatus, row.ResolutionCode, row.ResolutionDetails));
        }

        public void Complete(long inboxId, string resolutionCode)
        {
            var current = rows[inboxId];
            rows[inboxId] = current with
            {
                ProcessingStatus = "Processed",
                ResolutionCode = resolutionCode,
                ResolutionDetails = "Completed by the corrected processor.",
                RowVersion = NextVersion(current.RowVersion)
            };
        }

        private static byte[] NextVersion(byte[] version) => BitConverter.GetBytes(BitConverter.ToInt64(version) + 1);
    }

    private sealed class FakeRepairSync : IAttendanceSyncService
    {
        private AppDbContext db = null!;
        private FakeInboxStore store = null!;
        private IAttendanceWorkdayPolicy workdayPolicy = null!;
        public int CallCount { get; private set; }

        public void Configure(AppDbContext context, FakeInboxStore inboxStore, IAttendanceWorkdayPolicy policy)
        {
            db = context;
            store = inboxStore;
            workdayPolicy = policy;
        }

        public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default) =>
            SyncForProductionDateAsync(workdayPolicy.GetOperationalDate(DateTime.UtcNow), cancellationToken);

        public async Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var pending = store.Pending
                .Where(row => workdayPolicy.GetOperationalDate(ToUtc(row.SourceCheckTimeLocal)) == productionDate)
                .OrderBy(row => row.InboxId)
                .ToArray();
            foreach (var row in pending)
            {
                var worker = await db.Workers.SingleAsync(item => item.AttendanceUserId == row.SourceUserId.ToString(), cancellationToken);
                var utc = ToUtc(row.SourceCheckTimeLocal);
                var exact = (await db.AttendanceRecords.Where(record => record.WorkerId == worker.Id).ToArrayAsync(cancellationToken))
                    .Any(record => AttendancePunchEvidenceMatcher.IsExact(record, worker.Id, "AttendanceSync", utc, row.SourceCheckType == "I", row.SourceRawId));
                if (!exact)
                {
                    db.AttendanceRecords.Add(new AttendanceRecord(
                        Guid.NewGuid(),
                        worker.Id,
                        utc,
                        AttendanceStatus.Present,
                        "AttendanceSync",
                        JsonSerializer.Serialize(new { FirstInUtc = utc, LastOutUtc = (DateTime?)null }),
                        row.SourceRawId,
                        worker.AttendanceUserId,
                        worker.BadgeNumber));
                    await db.SaveChangesAsync(cancellationToken);
                    store.Complete(row.InboxId, "Imported");
                }
                else
                {
                    store.Complete(row.InboxId, "AlreadyImported");
                }
            }

            return Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto());
        }

        private static DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            TestCairoTimeZoneProvider.Instance.TimeZone);
    }
}
