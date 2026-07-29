using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class OperationalReadinessEngineTests
{
    [Fact]
    public async Task Stage_with_ten_assigned_and_six_currently_present_is_sixty_percent()
    {
        await using var fixture = await Fixture.CreateAsync(workerCount: 10, presentCount: 6);

        var result = await fixture.Engine.GetLineStagesAsync(fixture.Line.Id, fixture.AsOfUtc);

        var stage = Assert.Single(result.Value!.Stages, item => item.Id == fixture.StageA.Id);
        Assert.Equal(10, stage.Metrics.AssignedWorkerCount);
        Assert.Equal(6, stage.Metrics.CurrentlyPresentCount);
        Assert.Equal(60m, stage.Metrics.OperationalReadinessPercentage);
    }

    [Fact]
    public async Task New_check_in_updates_stage_line_department_and_factory_from_absolute_worker_counts()
    {
        await using var fixture = await Fixture.CreateAsync(workerCount: 10, presentCount: 6);
        fixture.SetAttendance(6, AttendanceStatus.Present);
        await fixture.Db.SaveChangesAsync();

        var stages = await fixture.Engine.GetLineStagesAsync(fixture.Line.Id, fixture.AsOfUtc);
        var snapshot = await fixture.Engine.GetSnapshotAsync(fixture.Factory.Id, fixture.AsOfUtc);

        Assert.Equal(70m, Assert.Single(stages.Value!.Stages, item => item.Id == fixture.StageA.Id).Metrics.OperationalReadinessPercentage);
        var factory = Assert.Single(snapshot.Value!.Factories);
        Assert.Equal(70m, factory.Metrics.OperationalReadinessPercentage);
        var department = Assert.Single(factory.Departments);
        Assert.Equal(70m, department.Metrics.OperationalReadinessPercentage);
        Assert.Equal(70m, Assert.Single(department.ProductionLines).Metrics.OperationalReadinessPercentage);
    }

    [Fact]
    public async Task Late_check_in_counts_as_present_and_keeps_late_classification()
    {
        await using var fixture = await Fixture.CreateAsync(workerCount: 1, presentCount: 0);
        fixture.SetAttendance(0, AttendanceStatus.Late, firstInUtc: fixture.AsOfUtc.AddHours(-1));
        await fixture.Db.SaveChangesAsync();

        var workers = await fixture.Engine.GetStageWorkersAsync(fixture.Line.Id, fixture.StageA.Id, fixture.AsOfUtc);
        var snapshot = await fixture.Engine.GetSnapshotAsync(fixture.Factory.Id, fixture.AsOfUtc);

        var worker = Assert.Single(workers.Value!.Workers);
        Assert.Equal(OperationalAttendanceStates.Late, worker.AttendanceState);
        Assert.True(worker.IsOperationallyPresent);
        Assert.NotNull(worker.LateByMinutes);
        var metrics = Assert.Single(snapshot.Value!.Factories).Metrics;
        Assert.Equal(1, metrics.CurrentlyPresentCount);
        Assert.Equal(1, metrics.LateCount);
        Assert.Equal(100m, metrics.OperationalReadinessPercentage);
    }

    [Fact]
    public async Task Checked_out_worker_is_not_currently_present()
    {
        await using var fixture = await Fixture.CreateAsync(workerCount: 1, presentCount: 0);
        fixture.SetAttendance(
            0,
            AttendanceStatus.Present,
            firstInUtc: fixture.AsOfUtc.AddHours(-2),
            lastOutUtc: fixture.AsOfUtc.AddMinutes(-1));
        await fixture.Db.SaveChangesAsync();

        var workers = await fixture.Engine.GetStageWorkersAsync(fixture.Line.Id, fixture.StageA.Id, fixture.AsOfUtc);
        var snapshot = await fixture.Engine.GetSnapshotAsync(fixture.Factory.Id, fixture.AsOfUtc);

        Assert.Equal(OperationalAttendanceStates.CheckedOut, Assert.Single(workers.Value!.Workers).AttendanceState);
        var metrics = Assert.Single(snapshot.Value!.Factories).Metrics;
        Assert.Equal(0, metrics.CurrentlyPresentCount);
        Assert.Equal(1, metrics.CheckedOutCount);
        Assert.Equal(0m, metrics.OperationalReadinessPercentage);
    }

    [Fact]
    public async Task No_assignments_returns_no_percentage_instead_of_one_hundred_or_division_by_zero()
    {
        await using var fixture = await Fixture.CreateAsync(workerCount: 0, presentCount: 0);

        var snapshot = await fixture.Engine.GetSnapshotAsync(fixture.Factory.Id, fixture.AsOfUtc);

        var metrics = Assert.Single(snapshot.Value!.Factories).Metrics;
        Assert.Equal("NoAssignments", metrics.Status);
        Assert.Null(metrics.OperationalReadinessPercentage);
        Assert.Null(metrics.ContributionToParentShortage);
    }

    [Fact]
    public async Task Worker_participating_in_two_stages_is_counted_once_in_higher_levels()
    {
        await using var fixture = await Fixture.CreateAsync(workerCount: 1, presentCount: 1, assignFirstWorkerToSecondStage: true);

        var stages = await fixture.Engine.GetLineStagesAsync(fixture.Line.Id, fixture.AsOfUtc);
        var snapshot = await fixture.Engine.GetSnapshotAsync(fixture.Factory.Id, fixture.AsOfUtc);

        Assert.Equal(2, stages.Value!.Stages.Count);
        Assert.All(stages.Value.Stages, stage => Assert.Equal(1, stage.Metrics.AssignedWorkerCount));
        var factory = Assert.Single(snapshot.Value!.Factories);
        Assert.Equal(1, factory.Metrics.AssignedWorkerCount);
        Assert.Equal(1, Assert.Single(Assert.Single(factory.Departments).ProductionLines).Metrics.AssignedWorkerCount);
    }

    [Fact]
    public async Task Stale_sync_returns_unknown_instead_of_confirmed_absence()
    {
        await using var fixture = await Fixture.CreateAsync(workerCount: 1, presentCount: 0, syncAgeMinutes: 10);

        var workers = await fixture.Engine.GetStageWorkersAsync(fixture.Line.Id, fixture.StageA.Id, fixture.AsOfUtc);
        var snapshot = await fixture.Engine.GetSnapshotAsync(fixture.Factory.Id, fixture.AsOfUtc);

        Assert.False(snapshot.Value!.AttendanceSync.IsTrusted);
        Assert.Equal("Stale", snapshot.Value.AttendanceSync.Status);
        Assert.Equal(OperationalAttendanceStates.Unknown, Assert.Single(workers.Value!.Workers).AttendanceState);
        var metrics = Assert.Single(snapshot.Value.Factories).Metrics;
        Assert.Null(metrics.OperationalReadinessPercentage);
        Assert.Equal(0, metrics.AbsentCount);
        Assert.Equal(1, metrics.UnknownCount);
    }

    [Fact]
    public void Calculator_deduplicates_repeated_assignment_rows()
    {
        var workerId = Guid.NewGuid();
        var states = new Dictionary<Guid, OperationalWorkerState>
        {
            [workerId] = new(workerId, OperationalAttendanceStates.Present)
        };

        var metrics = OperationalReadinessCalculator.Calculate([workerId, workerId], states, true, 0);

        Assert.Equal(1, metrics.AssignedWorkerCount);
        Assert.Equal(1, metrics.CurrentlyPresentCount);
        Assert.Equal(100m, metrics.OperationalReadinessPercentage);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            AppDbContext db,
            OperationalReadinessEngine engine,
            Factory factory,
            ProductionLine line,
            SubStage stageA,
            Worker[] workers,
            DateTime asOfUtc)
        {
            Db = db;
            Engine = engine;
            Factory = factory;
            Line = line;
            StageA = stageA;
            Workers = workers;
            AsOfUtc = asOfUtc;
        }

        public AppDbContext Db { get; }
        public OperationalReadinessEngine Engine { get; }
        public Factory Factory { get; }
        public ProductionLine Line { get; }
        public SubStage StageA { get; }
        public Worker[] Workers { get; }
        public DateTime AsOfUtc { get; }

        public static async Task<Fixture> CreateAsync(
            int workerCount,
            int presentCount,
            bool assignFirstWorkerToSecondStage = false,
            int syncAgeMinutes = 1)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            var asOfUtc = new DateTime(2026, 7, 29, 8, 0, 0, DateTimeKind.Utc);
            var factory = new Factory(Guid.NewGuid(), "المصنع", "FAC");
            var department = new Department(Guid.NewGuid(), factory.Id, "SEW", "الخياطة", "Sewing", 1);
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, "خط 1", 1, "L1", departmentId: department.Id);
            var mainStage = new MainStage(Guid.NewGuid(), department.Id, "تجهيز", 1);
            var stageA = new SubStage(Guid.NewGuid(), mainStage.Id, "قص", "CUT", 10, 1);
            var stageB = new SubStage(Guid.NewGuid(), mainStage.Id, "خياطة", "SEW", 10, 2);
            var model = new ProductModel(Guid.NewGuid(), "M1", "موديل أساسي");
            var workers = Enumerable.Range(0, workerCount)
                .Select(index => new Worker(Guid.NewGuid(), $"W-{index + 1}", $"عامل {index + 1}"))
                .ToArray();
            db.AddRange(factory, department, line, mainStage, stageA, stageB, model);
            db.ProductModelStages.AddRange(
                new ProductModelStage(Guid.NewGuid(), model.Id, line.Id, stageA.Id, 1, 0m, 60m, CompensationMode.SharedPercentage),
                new ProductModelStage(Guid.NewGuid(), model.Id, line.Id, stageB.Id, 2, 0m, 60m, CompensationMode.SharedPercentage));
            db.Workers.AddRange(workers);
            var actorId = Guid.NewGuid();
            db.WorkerDefaultAssignments.AddRange(workers.Select(worker =>
                new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, stageA.Id, actorId, asOfUtc.AddDays(-1), productionLineId: line.Id)));
            if (assignFirstWorkerToSecondStage && workers.Length > 0)
                db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(Guid.NewGuid(), workers[0].Id, stageB.Id, actorId, asOfUtc.AddDays(-1), productionLineId: line.Id));

            var sync = new AttendanceSyncState(Guid.NewGuid(), "AttendanceSync", new DateOnly(2026, 7, 29));
            sync.RecordSuccess(asOfUtc.AddMinutes(-syncAgeMinutes));
            db.AttendanceSyncStates.Add(sync);
            for (var index = 0; index < workers.Length; index++)
            {
                var status = index < presentCount ? AttendanceStatus.Present : AttendanceStatus.Absent;
                db.AttendanceRecords.Add(CreateAttendance(workers[index].Id, status, asOfUtc.AddHours(-2), null, asOfUtc.AddMinutes(-1)));
            }
            await db.SaveChangesAsync();

            var options = Options.Create(new AttendanceSourceOptions
            {
                SourceName = "AttendanceSync",
                DayStartTime = new TimeSpan(8, 0, 0),
                LateThresholdMinutes = 15,
                FreshnessThresholdMinutes = 5
            });
            return new Fixture(
                db,
                new OperationalReadinessEngine(db, options, TestCairoTimeZoneProvider.Instance),
                factory,
                line,
                stageA,
                workers,
                asOfUtc);
        }

        public void SetAttendance(int workerIndex, AttendanceStatus status, DateTime? firstInUtc = null, DateTime? lastOutUtc = null)
        {
            var workerId = Workers[workerIndex].Id;
            var existing = Db.AttendanceRecords.Single(record => record.WorkerId == workerId);
            var firstIn = firstInUtc ?? AsOfUtc.AddHours(-2);
            existing.UpdateAttendanceStatus(
                firstIn,
                status,
                "AttendanceSync",
                JsonSerializer.Serialize(new { FirstInUtc = firstIn, LastOutUtc = lastOutUtc }),
                "test",
                updatedAtUtc: AsOfUtc.AddSeconds(-1));
        }

        private static AttendanceRecord CreateAttendance(
            Guid workerId,
            AttendanceStatus status,
            DateTime firstInUtc,
            DateTime? lastOutUtc,
            DateTime createdAtUtc)
        {
            var time = status == AttendanceStatus.Absent
                ? new DateTime(2026, 7, 29, 20, 59, 59, DateTimeKind.Utc)
                : firstInUtc;
            return new AttendanceRecord(
                Guid.NewGuid(), workerId, time, status, "AttendanceSync",
                status == AttendanceStatus.Absent ? null : JsonSerializer.Serialize(new { FirstInUtc = firstInUtc, LastOutUtc = lastOutUtc }),
                "test", createdAtUtc: createdAtUtc);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
