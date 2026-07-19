using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class ReadinessEngineTests
{
    [Fact]
    public async Task All_assigned_workers_present_produces_complete_operational_readiness()
    {
        await using var fixture = await ReadinessFixture.CreateAsync(2, [AttendanceStatus.Present, AttendanceStatus.Present]);

        var result = await fixture.Engine.GetProductionLinesReadinessAsync(fixture.AsOfUtc);

        var line = Assert.Single(result.Value!.Items);
        Assert.Equal(100m, line.AssignmentCoveragePercent);
        Assert.Equal(100m, line.ReadinessPercent);
        Assert.Equal("Complete", line.AttendanceDataStatus);
        Assert.Equal(0, line.RequiredWorkers - line.PresentWorkers);
    }

    [Fact]
    public async Task Absent_assigned_worker_reduces_operational_readiness_without_reducing_assignment_coverage()
    {
        await using var fixture = await ReadinessFixture.CreateAsync(1, [AttendanceStatus.Absent]);

        var result = await fixture.Engine.GetProductionLinesReadinessAsync(fixture.AsOfUtc);

        var line = Assert.Single(result.Value!.Items);
        Assert.Equal(100m, line.AssignmentCoveragePercent);
        Assert.Equal(0m, line.ReadinessPercent);
        Assert.Equal(1, line.RequiredWorkers - line.PresentWorkers);
        Assert.Equal("Complete", line.AttendanceDataStatus);
    }

    [Fact]
    public async Task Present_replacement_covers_a_stage_when_the_default_worker_is_absent()
    {
        await using var fixture = await ReadinessFixture.CreateAsync(
            1,
            [AttendanceStatus.Absent, AttendanceStatus.Present],
            [AssignmentType.Default, AssignmentType.Replacement]);

        var result = await fixture.Engine.GetSubStageReadinessAsync(fixture.SubStageId, fixture.AsOfUtc);

        Assert.Equal(100m, result.Value!.AssignmentCoveragePercent);
        Assert.Equal(100m, result.Value.ReadinessPercent);
        Assert.Equal("Complete", result.Value.AttendanceDataStatus);
    }

    [Fact]
    public async Task Missing_assignment_reduces_both_assignment_coverage_and_operational_readiness()
    {
        await using var fixture = await ReadinessFixture.CreateAsync(1, [AttendanceStatus.Present], assignedWorkerIndexes: []);

        var result = await fixture.Engine.GetProductionLinesReadinessAsync(fixture.AsOfUtc);

        var line = Assert.Single(result.Value!.Items);
        Assert.Equal(0m, line.AssignmentCoveragePercent);
        Assert.Equal(0m, line.ReadinessPercent);
        Assert.Equal("NoAssignments", line.AttendanceDataStatus);
    }

    [Fact]
    public async Task Missing_attendance_records_never_turn_assignment_coverage_into_operational_readiness()
    {
        await using var fixture = await ReadinessFixture.CreateAsync(1, [null]);

        var result = await fixture.Engine.GetFactoryReadinessAsync(fixture.AsOfUtc);

        Assert.Equal(100m, result.Value!.AssignmentCoveragePercent);
        Assert.Equal(0m, result.Value.ReadinessPercent);
        Assert.Equal(ReadinessStatus.Unknown, result.Value.Status);
        Assert.Equal("Unavailable", result.Value.AttendanceDataStatus);
    }

    [Fact]
    public async Task Batch_sub_stage_attendance_keeps_structural_assignment_separate_from_absence()
    {
        await using var fixture = await ReadinessFixture.CreateAsync(1, [AttendanceStatus.Absent]);

        var result = await fixture.Engine.GetActiveSubStageAttendanceSummariesAsync(fixture.AsOfUtc);

        var stage = Assert.Single(result.Value!);
        Assert.Equal(1, stage.AssignedWorkersCount);
        Assert.Equal(0, stage.PresentAssignedWorkersCount);
        Assert.Equal(1, stage.AbsentAssignedWorkersCount);
        Assert.Equal("Complete", stage.AttendanceDataStatus);
        Assert.Equal("AllAbsent", stage.AttendanceStatus);
    }

    [Fact]
    public async Task Batch_sub_stage_attendance_reports_partial_presence_without_reclassifying_staffing()
    {
        await using var fixture = await ReadinessFixture.CreateAsync(3, [AttendanceStatus.Present, AttendanceStatus.Late, AttendanceStatus.Absent]);

        var result = await fixture.Engine.GetActiveSubStageAttendanceSummariesAsync(fixture.AsOfUtc);

        var stage = Assert.Single(result.Value!);
        Assert.Equal(3, stage.AssignedWorkersCount);
        Assert.Equal(2, stage.PresentAssignedWorkersCount);
        Assert.Equal(1, stage.LateAssignedWorkersCount);
        Assert.Equal(1, stage.AbsentAssignedWorkersCount);
        Assert.Equal("PartiallyPresent", stage.AttendanceStatus);
    }

    [Fact]
    public async Task Batch_sub_stage_attendance_requires_sync_when_no_today_evidence_exists()
    {
        await using var fixture = await ReadinessFixture.CreateAsync(1, [null]);

        var result = await fixture.Engine.GetActiveSubStageAttendanceSummariesAsync(fixture.AsOfUtc);

        var stage = Assert.Single(result.Value!);
        Assert.Equal("Unavailable", stage.AttendanceDataStatus);
        Assert.Equal("NeedsSync", stage.AttendanceStatus);
    }

    private sealed class ReadinessFixture : IAsyncDisposable
    {
        private ReadinessFixture(AppDbContext db, ReadinessEngine engine, Guid subStageId, DateTime asOfUtc)
        {
            Db = db;
            Engine = engine;
            SubStageId = subStageId;
            AsOfUtc = asOfUtc;
        }

        public AppDbContext Db { get; }
        public ReadinessEngine Engine { get; }
        public Guid SubStageId { get; }
        public DateTime AsOfUtc { get; }

        public static async Task<ReadinessFixture> CreateAsync(
            int capacity,
            IReadOnlyList<AttendanceStatus?> attendanceStatuses,
            IReadOnlyList<AssignmentType>? assignmentTypes = null,
            IReadOnlyList<int>? assignedWorkerIndexes = null)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            var factory = new Factory(Guid.NewGuid(), "Readiness Factory", "RDF");
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Readiness Line", 1);
            var mainStage = new MainStage(Guid.NewGuid(), line.Id, "Readiness Main", 1);
            var subStage = new SubStage(Guid.NewGuid(), mainStage.Id, "Readiness Sub", "RDS", capacity, 1);
            var workers = attendanceStatuses.Select((_, index) => new Worker(Guid.NewGuid(), $"R-{index + 1}", $"Worker {index + 1}")).ToArray();
            db.AddRange(factory, line, mainStage, subStage);
            db.Workers.AddRange(workers);
            await db.SaveChangesAsync();

            var asOfUtc = new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);
            var assignedIndexes = assignedWorkerIndexes ?? Enumerable.Range(0, workers.Length).ToArray();
            var assignments = assignedIndexes.ToDictionary(
                index => workers[index].Id,
                index => new WorkerAssignmentState(
                    Guid.NewGuid(),
                    workers[index].Id,
                    assignmentTypes?.ElementAtOrDefault(index) ?? AssignmentType.Default,
                    asOfUtc.AddDays(-1),
                    null,
                    subStage.Id,
                    null,
                    null,
                    null));
            var attendance = workers
                .Select((worker, index) => new { worker, status = attendanceStatuses[index] })
                .Where(x => x.status.HasValue)
                .ToDictionary(
                    x => x.worker.Id,
                    x => new AttendanceStatusRecord(x.worker.Id, x.status!.Value, asOfUtc, "test"));

            return new ReadinessFixture(
                db,
                new ReadinessEngine(db, new AssignmentEngineStub(assignments), new AttendanceEngineStub(attendance)),
                subStage.Id,
                asOfUtc);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class AttendanceEngineStub(IReadOnlyDictionary<Guid, AttendanceStatusRecord> statuses) : IAttendanceEngine
    {
        public Task<Result<Dictionary<Guid, AttendanceStatusRecord>>> GetLatestAttendanceStatusByWorkerAsync(IEnumerable<Guid> workerIds, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, AttendanceStatusRecord>>.Success(workerIds.Distinct().Where(statuses.ContainsKey).ToDictionary(id => id, id => statuses[id])));

        public Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(Guid? factoryId, Guid? lineId, DateTime? dateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(Guid workerId, DateTime? fromDateUtc, DateTime? toDateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(Guid subStageId, DateTime? dateUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(IEnumerable<Guid> workerIds, DateOnly productionDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class AssignmentEngineStub(IReadOnlyDictionary<Guid, WorkerAssignmentState> assignments) : IAssignmentEngine
    {
        public Task<Result<Dictionary<Guid, WorkerAssignmentState>>> ResolveCurrentAssignmentsAsync(IEnumerable<Guid> workerIds, DateTime asOfUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, WorkerAssignmentState>>.Success(assignments.Where(x => workerIds.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value)));

        public Task<Result<CurrentWorkerAssignmentDto>> GetCurrentAssignmentAsync(Guid workerId, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<Dictionary<Guid, IReadOnlyCollection<WorkerAssignmentState>>>> ResolveEffectiveAssignmentsAsync(IEnumerable<Guid> workerIds, DateTime asOfUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, IReadOnlyCollection<WorkerAssignmentState>>>.Success(
                assignments
                    .Where(x => workerIds.Contains(x.Key))
                    .ToDictionary(
                        x => x.Key,
                        x => (IReadOnlyCollection<WorkerAssignmentState>)[x.Value])));
        public Task<Result<int>> FinalizeCompletedTemporaryAssignmentsAsync(DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> CreateOrUpdateDefaultAssignmentAsync(CreateDefaultAssignmentRequest request, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<StageDefaultAssignmentsUpdateResultDto>> UpdateStageDefaultAssignmentsAsync(Guid subStageId, IReadOnlyCollection<Guid>? workerIds, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> RemoveDefaultAssignmentAsync(Guid workerId, Guid subStageId, string reason, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> CreateTemporaryAssignmentAsync(CreateTemporaryAssignmentRequest request, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> CreateReplacementAssignmentAsync(CreateReplacementAssignmentRequest request, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AssignmentActionResultDto>> MoveCurrentAssignmentAsync(MoveCurrentWorkerAssignmentRequest request, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<CancelTemporaryAssignmentResultDto>> CancelTemporaryAssignmentAsync(Guid assignmentId, string reason, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AssignmentTimelineDto>>> GetWorkerTimelineAsync(Guid workerId, int page = 1, int pageSize = 50, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SubStageCurrentWorkersDto>> GetSubStageWorkersAsync(Guid subStageId, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyCollection<SubStageAssignmentCoverageDto>>> GetActiveSubStageAssignmentCoverageAsync(DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
