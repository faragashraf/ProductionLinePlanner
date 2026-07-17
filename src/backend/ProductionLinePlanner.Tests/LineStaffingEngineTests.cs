using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class LineStaffingEngineTests
{
    [Fact]
    public async Task Staffing_plan_uses_all_active_workers_without_querying_attendance_and_reports_model_stages_together()
    {
        await using var fixture = await StaffingFixture.CreateAsync();
        var result = await fixture.Engine.GetLineStaffingPlanAsync(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            fixture.ReferenceDate);

        Assert.True(result.IsSuccess);
        var plan = result.Value!;
        Assert.Equal(2, plan.TotalStages);
        Assert.Equal(2, plan.Workers.Count);
        Assert.DoesNotContain(plan.Workers, worker => worker.WorkerId == fixture.FormerWorker.Id || worker.WorkerId == fixture.InactiveWorker.Id);
        Assert.All(plan.Workers, worker => Assert.NotNull(worker.EmployeeCode));
        Assert.True(plan.OperationalAttendanceChecked is false);

        var defaultStage = plan.Stages.Single(stage => stage.SubStageId == fixture.DefaultSubStage.Id);
        var temporaryStage = plan.Stages.Single(stage => stage.SubStageId == fixture.TemporarySubStage.Id);
        Assert.Equal(2, defaultStage.DefaultAssignedWorkersCount);
        Assert.Equal(1, defaultStage.EffectiveAssignedWorkersCount);
        Assert.Equal(1, temporaryStage.EffectiveAssignedWorkersCount);
        Assert.Equal(1, temporaryStage.TemporaryAssignedWorkersCount);
        Assert.Equal("SharedPercentage", temporaryStage.CompensationMode);
        Assert.Equal(.38m, temporaryStage.PiecePrice);
        Assert.True(temporaryStage.IsFinancialReviewPending);
        Assert.True(plan.StaffingPlanComplete);
    }

    [Fact]
    public async Task Staffing_worker_directory_returns_all_active_workers_immediately_without_attendance_data()
    {
        await using var fixture = await StaffingFixture.CreateAsync();

        var result = await fixture.Engine.GetActiveStaffingWorkersAsync(fixture.ReferenceDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.All(result.Value, worker => Assert.True(worker.IsOnActiveService));
        Assert.DoesNotContain(result.Value, worker => worker.WorkerId == fixture.FormerWorker.Id || worker.WorkerId == fixture.InactiveWorker.Id);
        Assert.Contains(result.Value, worker => worker.WorkerId == fixture.TemporarilyMovedWorker.Id && worker.EffectiveAssignmentType == "Temporary");
    }

    [Fact]
    public async Task Temporary_assignment_overrides_only_inside_its_window_then_the_worker_returns_to_the_preserved_default()
    {
        await using var fixture = await StaffingFixture.CreateAsync();

        var duringTemporary = await fixture.Engine.GetLineStaffingPlanAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.ReferenceDate);
        var afterTemporary = await fixture.Engine.GetLineStaffingPlanAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.ReferenceDate.AddDays(2));

        Assert.True(duringTemporary.IsSuccess);
        Assert.True(afterTemporary.IsSuccess);
        var duringWorker = duringTemporary.Value!.Workers.Single(worker => worker.WorkerId == fixture.TemporarilyMovedWorker.Id);
        var afterWorker = afterTemporary.Value!.Workers.Single(worker => worker.WorkerId == fixture.TemporarilyMovedWorker.Id);
        Assert.Equal("Temporary", duringWorker.EffectiveAssignmentType);
        Assert.Equal(fixture.TemporarySubStage.Id, duringWorker.EffectiveSubStageId);
        Assert.Equal(fixture.DefaultSubStage.Id, afterWorker.EffectiveSubStageId);
        Assert.Equal("Default", afterWorker.EffectiveAssignmentType);
        Assert.Equal(fixture.DefaultSubStage.Id, afterWorker.DefaultSubStageId);
        Assert.True((await fixture.Db.WorkerDefaultAssignments.SingleAsync(assignment => assignment.WorkerId == fixture.TemporarilyMovedWorker.Id)).IsActive);
        Assert.Equal("Active", (await fixture.Db.WorkerTemporaryAssignments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Effective_assignment_uses_default_before_the_temporary_period_and_after_its_end()
    {
        await using var fixture = await StaffingFixture.CreateAsync();

        var before = await fixture.Engine.GetLineStaffingPlanAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.ReferenceDate.AddDays(-2));
        var inside = await fixture.Engine.GetLineStaffingPlanAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.ReferenceDate);
        var after = await fixture.Engine.GetLineStaffingPlanAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.ReferenceDate.AddDays(2));

        Assert.True(before.IsSuccess);
        Assert.True(inside.IsSuccess);
        Assert.True(after.IsSuccess);
        Assert.Equal("Default", before.Value!.Workers.Single(x => x.WorkerId == fixture.TemporarilyMovedWorker.Id).EffectiveAssignmentType);
        Assert.Equal("Temporary", inside.Value!.Workers.Single(x => x.WorkerId == fixture.TemporarilyMovedWorker.Id).EffectiveAssignmentType);
        Assert.Equal("Default", after.Value!.Workers.Single(x => x.WorkerId == fixture.TemporarilyMovedWorker.Id).EffectiveAssignmentType);
    }

    [Fact]
    public async Task Staffing_plan_returns_one_worker_in_every_active_stage_participation()
    {
        await using var fixture = await StaffingFixture.CreateAsync();
        fixture.Db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(
            Guid.NewGuid(), fixture.TemporarilyMovedWorker.Id, fixture.TemporarySubStage.Id, Guid.NewGuid(),
            new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc)));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Engine.GetLineStaffingPlanAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.ReferenceDate.AddDays(2));

        Assert.True(result.IsSuccess);
        var worker = result.Value!.Workers.Single(candidate => candidate.WorkerId == fixture.TemporarilyMovedWorker.Id);
        Assert.Equal(2, worker.Participations.Count);
        Assert.Contains(worker.Participations, participation => participation.SubStageId == fixture.DefaultSubStage.Id);
        Assert.Contains(worker.Participations, participation => participation.SubStageId == fixture.TemporarySubStage.Id);
        Assert.Contains(result.Value.Stages.Single(stage => stage.SubStageId == fixture.DefaultSubStage.Id).EffectiveWorkerIds, id => id == fixture.TemporarilyMovedWorker.Id);
        Assert.Contains(result.Value.Stages.Single(stage => stage.SubStageId == fixture.TemporarySubStage.Id).EffectiveWorkerIds, id => id == fixture.TemporarilyMovedWorker.Id);
    }

    [Fact]
    public async Task Stage_refresh_after_removing_the_last_worker_returns_needs_staffing_and_cleared_worker_state()
    {
        await using var fixture = await StaffingFixture.CreateAsync();
        var lastAssignment = await fixture.Db.WorkerDefaultAssignments
            .SingleAsync(assignment => assignment.WorkerId != fixture.TemporarilyMovedWorker.Id);
        lastAssignment.Deactivate(DateTime.UtcNow);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Engine.GetLineStaffingStageRefreshAsync(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            fixture.DefaultSubStage.Id,
            fixture.ReferenceDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Stage.EffectiveAssignedWorkersCount);
        Assert.Equal("NeedsStaffing", result.Value.Stage.StaffingStatus);
        Assert.Empty(result.Value.Stage.EffectiveWorkerIds);
        Assert.Equal(2, result.Value.Workers.Count);
        Assert.DoesNotContain(
            result.Value.Workers.Single(worker => worker.WorkerId == lastAssignment.WorkerId).Participations,
            participation => participation.SubStageId == fixture.DefaultSubStage.Id);
    }

    private sealed class StaffingFixture : IAsyncDisposable
    {
        private StaffingFixture(
            AppDbContext db,
            LineStaffingEngine engine,
            Factory factory,
            ProductionLine line,
            ProductModel model,
            SubStage defaultSubStage,
            SubStage temporarySubStage,
            Worker temporarilyMovedWorker,
            Worker formerWorker,
            Worker inactiveWorker,
            DateOnly referenceDate)
        {
            Db = db;
            Engine = engine;
            Factory = factory;
            Line = line;
            Model = model;
            DefaultSubStage = defaultSubStage;
            TemporarySubStage = temporarySubStage;
            TemporarilyMovedWorker = temporarilyMovedWorker;
            FormerWorker = formerWorker;
            InactiveWorker = inactiveWorker;
            ReferenceDate = referenceDate;
        }

        public AppDbContext Db { get; }
        public LineStaffingEngine Engine { get; }
        public Factory Factory { get; }
        public ProductionLine Line { get; }
        public ProductModel Model { get; }
        public SubStage DefaultSubStage { get; }
        public SubStage TemporarySubStage { get; }
        public Worker TemporarilyMovedWorker { get; }
        public Worker FormerWorker { get; }
        public Worker InactiveWorker { get; }
        public DateOnly ReferenceDate { get; }

        public static async Task<StaffingFixture> CreateAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            var actorId = Guid.NewGuid();
            var referenceDate = new DateOnly(2026, 7, 13);
            var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
            var mainStage = new MainStage(Guid.NewGuid(), line.Id, "Main", 1);
            var defaultSubStage = new SubStage(Guid.NewGuid(), mainStage.Id, "Default", "DEF", 1, 1);
            var temporarySubStage = new SubStage(Guid.NewGuid(), mainStage.Id, "Temporary", "TMP", 1, 2);
            var model = new ProductModel(Guid.NewGuid(), "MODEL", "Model");
            var moved = new Worker(Guid.NewGuid(), "100", "Temporarily moved", photoReference: "worker-photo:version-a");
            var defaultWorker = new Worker(Guid.NewGuid(), "101", "Default worker");
            var former = new Worker(Guid.NewGuid(), "102", "Former worker");
            former.SetEmploymentStatus(EmploymentStatus.LeftEmployment, DateTime.UtcNow, DateTime.UtcNow);
            var inactive = new Worker(Guid.NewGuid(), "103", "Inactive worker");
            inactive.Suspend();

            db.AddRange(factory, line, mainStage, defaultSubStage, temporarySubStage, model, moved, defaultWorker, former, inactive);
            db.ProductModelStages.AddRange(
                new ProductModelStage(Guid.NewGuid(), model.Id, defaultSubStage.Id, 1, .38m, 10m, CompensationMode.SharedPercentage),
                new ProductModelStage(Guid.NewGuid(), model.Id, temporarySubStage.Id, 2, .38m, 10m, CompensationMode.SharedPercentage));
            db.WorkerDefaultAssignments.AddRange(
                new WorkerDefaultAssignment(Guid.NewGuid(), moved.Id, defaultSubStage.Id, actorId, new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc)),
                new WorkerDefaultAssignment(Guid.NewGuid(), defaultWorker.Id, defaultSubStage.Id, actorId, new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc)));
            db.WorkerTemporaryAssignments.Add(new WorkerTemporaryAssignment(
                Guid.NewGuid(), moved.Id, defaultSubStage.Id, temporarySubStage.Id,
                new DateTime(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 14, 23, 0, 0, DateTimeKind.Utc),
                actorId, "Temporary staffing", status: "Active"));
            await db.SaveChangesAsync();

            var assignmentEngine = new AssignmentEngine(db, new NoopAuditEngine());
            return new StaffingFixture(
                db,
                new LineStaffingEngine(db, assignmentEngine),
                factory,
                line,
                model,
                defaultSubStage,
                temporarySubStage,
                moved,
                former,
                inactive,
                referenceDate);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class NoopAuditEngine : IAuditEngine
    {
        public Task<Result> RecordAsync(
            Guid actorUserId,
            AuditActionType actionType,
            string entityType,
            string entityId,
            object? before = null,
            object? after = null,
            string? requestMeta = null,
            CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }
}
