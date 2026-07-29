using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class AttendanceWorkforceEngineTests
{
    private static readonly DateOnly ProductionDate = new(2026, 7, 19);
    private static readonly DateTime DayEvidenceUtc = new(2026, 7, 19, 4, 52, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPage_projects_attendance_without_mutating_workers()
    {
        await using var db = CreateDb();
        var presentWorker = new Worker(Guid.NewGuid(), "1001", "أحمد");
        var absentWorker = new Worker(Guid.NewGuid(), "1002", "محمود");
        db.AddRange(
            presentWorker,
            absentWorker,
            Attendance(presentWorker.Id, AttendanceStatus.Present),
            Attendance(absentWorker.Id, AttendanceStatus.Absent));
        await db.SaveChangesAsync();
        var engine = CreateEngine(db, new Dictionary<Guid, AttendancePresenceWindowDto>
        {
            [presentWorker.Id] = new(presentWorker.Id, AttendanceStatus.Present, DayEvidenceUtc, DayEvidenceUtc.AddHours(8), true),
            [absentWorker.Id] = new(absentWorker.Id, AttendanceStatus.Absent, null, null, false)
        });

        var result = await engine.GetPageAsync(new AttendanceWorkforceQuery(ProductionDate));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(1, result.Value.Summary.PresentWorkers);
        Assert.Equal(1, result.Value.Summary.AbsentWorkers);
        Assert.All(db.ChangeTracker.Entries<Worker>(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public async Task GetPage_reports_needs_sync_when_the_day_has_no_attendance_evidence()
    {
        await using var db = CreateDb();
        var worker = new Worker(Guid.NewGuid(), "1001", "أحمد");
        db.Add(worker);
        await db.SaveChangesAsync();
        var engine = CreateEngine(db, new Dictionary<Guid, AttendancePresenceWindowDto>
        {
            [worker.Id] = new(worker.Id, AttendanceStatus.Present, DayEvidenceUtc, DayEvidenceUtc.AddHours(8), true)
        });

        var result = await engine.GetPageAsync(new AttendanceWorkforceQuery(ProductionDate));

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("NeedsSync", row.AttendanceStatus);
        Assert.False(result.Value.Summary.AttendanceDataAvailable);
    }

    [Fact]
    public async Task GetPage_filters_attendance_and_keeps_name_sort_stable()
    {
        await using var db = CreateDb();
        var secondByName = new Worker(Guid.NewGuid(), "1002", "محمود");
        var firstByName = new Worker(Guid.NewGuid(), "1001", "أحمد");
        db.AddRange(secondByName, firstByName, Attendance(secondByName.Id, AttendanceStatus.Present), Attendance(firstByName.Id, AttendanceStatus.Present));
        await db.SaveChangesAsync();
        var windows = new[] { secondByName, firstByName }.ToDictionary(
            worker => worker.Id,
            worker => new AttendancePresenceWindowDto(worker.Id, AttendanceStatus.Present, DayEvidenceUtc, DayEvidenceUtc.AddHours(8), true));

        var result = await CreateEngine(db, windows).GetPageAsync(new AttendanceWorkforceQuery(ProductionDate, AttendanceFilter: "Present", SortBy: "name", SortDirection: "asc"));

        Assert.Equal(["أحمد", "محمود"], result.Value!.Items.Select(item => item.FullName));
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetWorkerDetail_returns_neutral_utc_punches_without_raw_direction_codes()
    {
        await using var db = CreateDb();
        var worker = new Worker(Guid.NewGuid(), "1001", "أحمد");
        db.AddRange(worker, new AttendanceRecord(
            Guid.NewGuid(),
            worker.Id,
            DayEvidenceUtc,
            AttendanceStatus.Present,
            sourcePayload: "{\"FirstInUtc\":\"2026-07-19T04:52:00Z\",\"LastOutUtc\":\"2026-07-19T13:00:00Z\"}"));
        await db.SaveChangesAsync();

        var result = await CreateEngine(db, new Dictionary<Guid, AttendancePresenceWindowDto>()).GetWorkerDetailAsync(worker.Id, ProductionDate);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value!.AttendanceRecords,
            first => { Assert.Equal("Punch", first.Label); Assert.Equal(DateTimeKind.Utc, first.OccurredAtUtc.Kind); Assert.Equal(DayEvidenceUtc, first.OccurredAtUtc); },
            last => { Assert.Equal("Punch", last.Label); Assert.Equal(DateTimeKind.Utc, last.OccurredAtUtc.Kind); Assert.Equal(new DateTime(2026, 7, 19, 13, 0, 0, DateTimeKind.Utc), last.OccurredAtUtc); });
    }

    [Fact]
    public async Task GetWorkerDetail_keeps_historical_attendance_available_after_worker_becomes_inactive()
    {
        await using var db = CreateDb();
        var worker = new Worker(Guid.NewGuid(), "1001", "عامل تاريخي");
        worker.Suspend();
        db.AddRange(worker, Attendance(worker.Id, AttendanceStatus.Present));
        await db.SaveChangesAsync();

        var result = await CreateEngine(db, new Dictionary<Guid, AttendancePresenceWindowDto>()).GetWorkerDetailAsync(worker.Id, ProductionDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(worker.Id, result.Value!.WorkerId);
    }

    [Fact]
    public async Task GetWorkerProfileSummary_returns_today_window_and_latest_known_movement()
    {
        await using var db = CreateDb();
        var worker = new Worker(Guid.NewGuid(), "1001", "أحمد");
        var lastOut = new DateTime(2026, 7, 19, 13, 0, 0, DateTimeKind.Utc);
        db.AddRange(worker, new AttendanceRecord(
            Guid.NewGuid(),
            worker.Id,
            DayEvidenceUtc,
            AttendanceStatus.Present,
            sourcePayload: $"{{\"FirstInUtc\":\"{DayEvidenceUtc:O}\",\"LastOutUtc\":\"{lastOut:O}\"}}"));
        await db.SaveChangesAsync();
        var windows = new Dictionary<Guid, AttendancePresenceWindowDto>
        {
            [worker.Id] = new(worker.Id, AttendanceStatus.Present, DayEvidenceUtc, lastOut, true)
        };

        var result = await CreateEngine(db, windows).GetWorkerProfileSummaryAsync(worker.Id, ProductionDate);

        Assert.True(result.IsSuccess);
        Assert.Equal("Present", result.Value!.TodayStatus);
        Assert.True(result.Value.AttendanceDataAvailableForDate);
        Assert.Equal(DayEvidenceUtc, result.Value.FirstCheckInUtc);
        Assert.Equal(lastOut, result.Value.LastCheckOutUtc);
        Assert.Equal(lastOut, result.Value.LastKnownMovementUtc);
        Assert.Equal(DateTimeKind.Utc, result.Value.LastKnownMovementUtc!.Value.Kind);
    }

    [Fact]
    public async Task GetWorkerProfileSummary_distinguishes_no_movement_from_an_unsynchronized_day()
    {
        await using var db = CreateDb();
        var worker = new Worker(Guid.NewGuid(), "1001", "أحمد");
        var otherWorker = new Worker(Guid.NewGuid(), "1002", "محمود");
        db.AddRange(worker, otherWorker, Attendance(otherWorker.Id, AttendanceStatus.Present));
        await db.SaveChangesAsync();
        var engine = CreateEngine(db, new Dictionary<Guid, AttendancePresenceWindowDto>());

        var noMovement = await engine.GetWorkerProfileSummaryAsync(worker.Id, ProductionDate);
        var needsSync = await engine.GetWorkerProfileSummaryAsync(worker.Id, ProductionDate.AddDays(1));

        Assert.Equal("NoMovement", noMovement.Value!.TodayStatus);
        Assert.True(noMovement.Value.AttendanceDataAvailableForDate);
        Assert.Equal("NeedsSync", needsSync.Value!.TodayStatus);
        Assert.False(needsSync.Value.AttendanceDataAvailableForDate);
    }

    [Fact]
    public async Task GetWorkerAttendanceHistory_filters_inclusive_dates_projects_explicit_in_out_and_pages_newest_first()
    {
        await using var db = CreateDb();
        var worker = new Worker(Guid.NewGuid(), "1001", "أحمد");
        var firstIn = new DateTime(2026, 7, 19, 4, 30, 0, DateTimeKind.Utc);
        var firstOut = firstIn.AddHours(8);
        var secondIn = firstIn.AddDays(1);
        var secondOut = secondIn.AddHours(8);
        db.AddRange(
            worker,
            AttendanceWindow(worker.Id, firstIn, firstOut),
            AttendanceWindow(worker.Id, secondIn, secondOut),
            new AttendanceRecord(Guid.NewGuid(), worker.Id, secondIn.AddHours(1), AttendanceStatus.Absent));
        await db.SaveChangesAsync();

        var result = await CreateEngine(db, new Dictionary<Guid, AttendancePresenceWindowDto>()).GetWorkerAttendanceHistoryAsync(
            worker.Id,
            new WorkerAttendanceHistoryQuery(new DateOnly(2026, 7, 19), new DateOnly(2026, 7, 20), Page: 1, PageSize: 1));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.TotalPages);
        var record = Assert.Single(result.Value.Items);
        Assert.Equal(new DateOnly(2026, 7, 20), record.ProductionDate);
        Assert.Collection(record.Movements,
            movement => { Assert.Equal("In", movement.MovementType); Assert.Equal(secondIn, movement.OccurredAtUtc); Assert.Equal(DateTimeKind.Utc, movement.OccurredAtUtc.Kind); },
            movement => { Assert.Equal("Out", movement.MovementType); Assert.Equal(secondOut, movement.OccurredAtUtc); });
    }

    [Fact]
    public async Task GetWorkerAttendanceHistory_includes_both_range_boundaries_and_returns_empty_for_worker_without_movements()
    {
        await using var db = CreateDb();
        var worker = new Worker(Guid.NewGuid(), "1001", "أحمد");
        var emptyWorker = new Worker(Guid.NewGuid(), "1002", "محمود");
        db.AddRange(
            worker,
            emptyWorker,
            AttendanceWindow(worker.Id, new DateTime(2026, 7, 18, 22, 0, 0, DateTimeKind.Utc), null),
            AttendanceWindow(worker.Id, new DateTime(2026, 7, 20, 20, 0, 0, DateTimeKind.Utc), null),
            AttendanceWindow(worker.Id, new DateTime(2026, 7, 20, 22, 0, 0, DateTimeKind.Utc), null));
        await db.SaveChangesAsync();
        var engine = CreateEngine(db, new Dictionary<Guid, AttendancePresenceWindowDto>());

        var bounded = await engine.GetWorkerAttendanceHistoryAsync(worker.Id, new(new(2026, 7, 19), new(2026, 7, 20), PageSize: 10));
        var empty = await engine.GetWorkerAttendanceHistoryAsync(emptyWorker.Id, new(new(2026, 7, 19), new(2026, 7, 20)));

        Assert.Equal(2, bounded.Value!.TotalCount);
        Assert.Equal([new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 19)], bounded.Value.Items.Select(item => item.ProductionDate));
        Assert.Empty(empty.Value!.Items);
        Assert.Equal(0, empty.Value.TotalCount);
    }

    [Theory]
    [InlineData(0, 20, "2026-07-19", "2026-07-20")]
    [InlineData(1, 101, "2026-07-19", "2026-07-20")]
    [InlineData(1, 20, "2026-07-20", "2026-07-19")]
    public async Task GetWorkerAttendanceHistory_rejects_invalid_range_and_pagination(int page, int pageSize, string from, string to)
    {
        await using var db = CreateDb();
        var worker = new Worker(Guid.NewGuid(), "1001", "أحمد");
        db.Add(worker);
        await db.SaveChangesAsync();

        var result = await CreateEngine(db, new Dictionary<Guid, AttendancePresenceWindowDto>()).GetWorkerAttendanceHistoryAsync(
            worker.Id,
            new WorkerAttendanceHistoryQuery(DateOnly.Parse(from), DateOnly.Parse(to), page, pageSize));

        Assert.Equal("ValidationError", result.Error?.Code);
    }

    [Fact]
    public async Task GetPage_resolves_filtered_workers_in_bounded_batches()
    {
        await using var db = CreateDb();
        var workers = Enumerable.Range(1, 225).Select(index => new Worker(Guid.NewGuid(), index.ToString("D4"), $"عامل {index:D4}")).ToArray();
        db.AddRange(workers);
        db.AddRange(workers.Select(worker => Attendance(worker.Id, AttendanceStatus.Present)));
        await db.SaveChangesAsync();
        var windows = workers.ToDictionary(worker => worker.Id, worker => new AttendancePresenceWindowDto(worker.Id, AttendanceStatus.Present, DayEvidenceUtc, DayEvidenceUtc.AddHours(8), true));
        var attendanceEngine = new AttendanceEngineStub(windows);
        var assignmentEngine = new AssignmentEngineStub();
        var engine = new AttendanceWorkforceEngine(db, attendanceEngine, assignmentEngine, new CairoTimeZoneProviderStub());

        var result = await engine.GetPageAsync(new AttendanceWorkforceQuery(ProductionDate, Page: 2, PageSize: 25, AttendanceFilter: "Present", SortBy: "name"));

        Assert.True(result.IsSuccess);
        Assert.Equal(225, result.Value!.TotalCount);
        Assert.Equal(25, result.Value.Items.Count);
        Assert.Equal(3, attendanceEngine.CallCount);
        Assert.InRange(attendanceEngine.MaxRequestedWorkerCount, 1, 100);
        Assert.InRange(assignmentEngine.MaxRequestedWorkerCount, 1, 100);
    }

    [Fact]
    public async Task GetPage_filters_by_worker_id_in_the_database_and_keeps_historical_inactive_worker_visible()
    {
        await using var db = CreateDb();
        var historicalWorker = new Worker(Guid.NewGuid(), "1001", "عامل تاريخي");
        historicalWorker.Suspend();
        var otherWorker = new Worker(Guid.NewGuid(), "1002", "عامل آخر");
        db.AddRange(historicalWorker, otherWorker, Attendance(historicalWorker.Id, AttendanceStatus.Present));
        await db.SaveChangesAsync();
        var windows = new Dictionary<Guid, AttendancePresenceWindowDto>
        {
            [historicalWorker.Id] = new(historicalWorker.Id, AttendanceStatus.Present, DayEvidenceUtc, DayEvidenceUtc.AddHours(8), true)
        };

        var result = await CreateEngine(db, windows).GetPageAsync(new AttendanceWorkforceQuery(ProductionDate, WorkerId: historicalWorker.Id));

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(historicalWorker.Id, row.WorkerId);
        Assert.Equal(1, result.Value.TotalCount);
    }

    private static AttendanceRecord Attendance(Guid workerId, AttendanceStatus status) =>
        new(Guid.NewGuid(), workerId, DayEvidenceUtc, status);

    private static AttendanceRecord AttendanceWindow(Guid workerId, DateTime firstInUtc, DateTime? lastOutUtc) =>
        new(
            Guid.NewGuid(),
            workerId,
            firstInUtc,
            AttendanceStatus.Present,
            source: "AttendanceSync",
            sourcePayload: $"{{\"FirstInUtc\":\"{firstInUtc:O}\",\"LastOutUtc\":{(lastOutUtc.HasValue ? $"\"{lastOutUtc.Value:O}\"" : "null")}}}");

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .Options);

    private static AttendanceWorkforceEngine CreateEngine(AppDbContext db, IReadOnlyDictionary<Guid, AttendancePresenceWindowDto> windows) =>
        new(db, new AttendanceEngineStub(windows), new AssignmentEngineStub(), new CairoTimeZoneProviderStub());

    private sealed class CairoTimeZoneProviderStub : ICairoTimeZoneProvider
    {
        public TimeZoneInfo TimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Test Cairo", TimeSpan.FromHours(3), "Test Cairo", "Test Cairo");
    }

    private sealed class AttendanceEngineStub(IReadOnlyDictionary<Guid, AttendancePresenceWindowDto> windows) : IAttendanceEngine
    {
        public int CallCount { get; private set; }
        public int MaxRequestedWorkerCount { get; private set; }
        public Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(IEnumerable<Guid> workerIds, DateOnly productionDate, CancellationToken cancellationToken = default)
        {
            var ids = workerIds.Distinct().ToArray();
            CallCount++;
            MaxRequestedWorkerCount = Math.Max(MaxRequestedWorkerCount, ids.Length);
            return Task.FromResult(Result<Dictionary<Guid, AttendancePresenceWindowDto>>.Success(ids.Where(windows.ContainsKey).ToDictionary(id => id, id => windows[id])));
        }
        public Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(Guid? factoryId, Guid? lineId, DateTime? dateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(Guid workerId, DateTime? fromDateUtc, DateTime? toDateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(Guid subStageId, DateTime? dateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<Dictionary<Guid, AttendanceStatusRecord>>> GetLatestAttendanceStatusByWorkerAsync(IEnumerable<Guid> workerIds, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class AssignmentEngineStub : IAssignmentEngine
    {
        public int MaxRequestedWorkerCount { get; private set; }
        public Task<Result<Dictionary<Guid, IReadOnlyCollection<WorkerAssignmentState>>>> ResolveEffectiveAssignmentsAsync(IEnumerable<Guid> workerIds, DateTime asOfUtc, CancellationToken cancellationToken = default)
        {
            var ids = workerIds.Distinct().ToArray();
            MaxRequestedWorkerCount = Math.Max(MaxRequestedWorkerCount, ids.Length);
            return Task.FromResult(Result<Dictionary<Guid, IReadOnlyCollection<WorkerAssignmentState>>>.Success(ids.ToDictionary(id => id, _ => (IReadOnlyCollection<WorkerAssignmentState>)Array.Empty<WorkerAssignmentState>())));
        }
        public Task<Result<CurrentWorkerAssignmentDto>> GetCurrentAssignmentAsync(Guid workerId, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<Dictionary<Guid, WorkerAssignmentState>>> ResolveCurrentAssignmentsAsync(IEnumerable<Guid> workerIds, DateTime asOfUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<int>> FinalizeCompletedTemporaryAssignmentsAsync(DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> CreateOrUpdateDefaultAssignmentAsync(CreateDefaultAssignmentRequest request, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<StageDefaultAssignmentsUpdateResultDto>> UpdateStageDefaultAssignmentsAsync(Guid productionLineId, Guid subStageId, IReadOnlyCollection<Guid>? workerIds, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> RemoveDefaultAssignmentAsync(Guid workerId, Guid productionLineId, Guid subStageId, string reason, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> CreateTemporaryAssignmentAsync(CreateTemporaryAssignmentRequest request, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> CreateReplacementAssignmentAsync(CreateReplacementAssignmentRequest request, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> MoveCurrentAssignmentAsync(MoveCurrentWorkerAssignmentRequest request, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<CancelTemporaryAssignmentResultDto>> CancelTemporaryAssignmentAsync(Guid assignmentId, string reason, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AssignmentTimelineDto>>> GetWorkerTimelineAsync(Guid workerId, int page = 1, int pageSize = 50, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SubStageCurrentWorkersDto>> GetSubStageWorkersAsync(Guid subStageId, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyCollection<SubStageAssignmentCoverageDto>>> GetActiveSubStageAssignmentCoverageAsync(DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
