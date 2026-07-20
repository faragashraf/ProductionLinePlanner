using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class AssignmentEngineCoverageTests
{
    [Fact]
    public async Task Active_sub_stage_coverage_uses_permanent_assignments_without_attendance()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);
        var now = new DateTime(2026, 7, 19, 9, 0, 0, DateTimeKind.Utc);
        var actorId = Guid.NewGuid();
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
        var mainStage = new MainStage(Guid.NewGuid(), line.Id, "Main", 1);
        var covered = new SubStage(Guid.NewGuid(), mainStage.Id, "Covered", "COV", 2, 1);
        var partial = new SubStage(Guid.NewGuid(), mainStage.Id, "Partial", "PAR", 2, 2);
        var empty = new SubStage(Guid.NewGuid(), mainStage.Id, "Empty", "EMP", 1, 3);
        var undefined = new SubStage(Guid.NewGuid(), mainStage.Id, "Undefined", "UND", 0, 4);
        var workerOne = new Worker(Guid.NewGuid(), "W1", "Worker One");
        var workerTwo = new Worker(Guid.NewGuid(), "W2", "Worker Two");
        var workerThree = new Worker(Guid.NewGuid(), "W3", "Worker Three");
        var workerFour = new Worker(Guid.NewGuid(), "W4", "Worker Four");

        db.AddRange(factory, line, mainStage, covered, partial, empty, undefined, workerOne, workerTwo, workerThree, workerFour);
        db.AddRange(
            new WorkerDefaultAssignment(Guid.NewGuid(), workerOne.Id, covered.Id, actorId, now.AddDays(-2)),
            new WorkerDefaultAssignment(Guid.NewGuid(), workerTwo.Id, partial.Id, actorId, now.AddDays(-2)),
            new WorkerDefaultAssignment(Guid.NewGuid(), workerThree.Id, partial.Id, actorId, now.AddDays(-2)),
            new WorkerDefaultAssignment(Guid.NewGuid(), workerFour.Id, undefined.Id, actorId, now.AddDays(-2)),
            new WorkerTemporaryAssignment(Guid.NewGuid(), workerThree.Id, partial.Id, covered.Id, now.AddHours(-1), now.AddHours(1), actorId, "Temporary coverage", status: "Active"));
        await db.SaveChangesAsync();

        var engine = new AssignmentEngine(db, new RecordingAuditEngine());
        var result = await engine.GetActiveSubStageAssignmentCoverageAsync(now);

        Assert.True(result.IsSuccess);
        var summaries = result.Value!;
        var coveredSummary = summaries.Single(summary => summary.SubStageId == covered.Id);
        var partialSummary = summaries.Single(summary => summary.SubStageId == partial.Id);
        var emptySummary = summaries.Single(summary => summary.SubStageId == empty.Id);
        var undefinedSummary = summaries.Single(summary => summary.SubStageId == undefined.Id);

        Assert.Equal(1, coveredSummary.AssignedWorkersCount);
        Assert.Equal(2, coveredSummary.RequiredWorkersCount);
        Assert.Equal(50, coveredSummary.AssignmentCoveragePercent);
        Assert.Equal("Understaffed", coveredSummary.StaffingStatus);

        Assert.Equal(2, partialSummary.AssignedWorkersCount);
        Assert.Equal(2, partialSummary.RequiredWorkersCount);
        Assert.Equal(100, partialSummary.AssignmentCoveragePercent);
        Assert.Equal("Staffed", partialSummary.StaffingStatus);

        Assert.Equal(0, emptySummary.AssignedWorkersCount);
        Assert.Equal(1, emptySummary.RequiredWorkersCount);
        Assert.Equal(0, emptySummary.AssignmentCoveragePercent);
        Assert.Equal("Unstaffed", emptySummary.StaffingStatus);

        Assert.Equal(1, undefinedSummary.AssignedWorkersCount);
        Assert.False(undefinedSummary.HasAuthoritativeRequiredWorkerCount);
        Assert.Null(undefinedSummary.RequiredWorkersCount);
        Assert.Null(undefinedSummary.AssignmentCoveragePercent);
        Assert.Equal("RequirementNotDefined", undefinedSummary.StaffingStatus);
        Assert.All(summaries, summary => Assert.Equal(4, summary.MainStageDistinctWorkersCount));
        Assert.All(summaries, summary => Assert.Equal(4, summary.ProductionLineDistinctWorkersCount));
        Assert.All(summaries, summary => Assert.Equal(4, summary.FactoryDistinctWorkersCount));
    }

    [Fact]
    public async Task Hierarchy_counts_one_worker_assigned_to_two_stages_once()
    {
        await using var fixture = await CoverageFixture.CreateAsync(1);
        fixture.Db.AddRange(
            new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Workers[0].Id, fixture.StageA.Id, fixture.ActorId, fixture.Now.AddDays(-1)),
            new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Workers[0].Id, fixture.StageB.Id, fixture.ActorId, fixture.Now.AddDays(-1)));
        await fixture.Db.SaveChangesAsync();

        var summaries = (await fixture.Engine.GetActiveSubStageAssignmentCoverageAsync(fixture.Now)).Value!;

        Assert.Equal(1, summaries.Single(item => item.SubStageId == fixture.StageA.Id).AssignedWorkersCount);
        Assert.Equal(1, summaries.Single(item => item.SubStageId == fixture.StageB.Id).AssignedWorkersCount);
        Assert.All(summaries, summary => Assert.Equal(1, summary.MainStageDistinctWorkersCount));
        Assert.All(summaries, summary => Assert.Equal(1, summary.ProductionLineDistinctWorkersCount));
        Assert.All(summaries, summary => Assert.Equal(1, summary.FactoryDistinctWorkersCount));
    }

    [Fact]
    public async Task Hierarchy_counts_two_different_workers_once_each()
    {
        await using var fixture = await CoverageFixture.CreateAsync(2);
        fixture.Db.AddRange(
            new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Workers[0].Id, fixture.StageA.Id, fixture.ActorId, fixture.Now.AddDays(-1)),
            new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Workers[1].Id, fixture.StageB.Id, fixture.ActorId, fixture.Now.AddDays(-1)));
        await fixture.Db.SaveChangesAsync();

        var summaries = (await fixture.Engine.GetActiveSubStageAssignmentCoverageAsync(fixture.Now)).Value!;

        Assert.All(summaries, summary => Assert.Equal(2, summary.MainStageDistinctWorkersCount));
        Assert.All(summaries, summary => Assert.Equal(2, summary.ProductionLineDistinctWorkersCount));
        Assert.All(summaries, summary => Assert.Equal(2, summary.FactoryDistinctWorkersCount));
    }

    [Fact]
    public async Task Permanent_and_temporary_overlap_does_not_duplicate_worker()
    {
        await using var fixture = await CoverageFixture.CreateAsync(1);
        fixture.Db.AddRange(
            new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Workers[0].Id, fixture.StageA.Id, fixture.ActorId, fixture.Now.AddDays(-1)),
            new WorkerTemporaryAssignment(
                Guid.NewGuid(), fixture.Workers[0].Id, null, fixture.StageA.Id,
                fixture.Now.AddHours(-1), fixture.Now.AddHours(1), fixture.ActorId, "Additional participation",
                participationMode: TemporaryAssignmentMode.AdditionalParticipation, status: "Active"));
        await fixture.Db.SaveChangesAsync();

        var summaries = (await fixture.Engine.GetActiveSubStageAssignmentCoverageAsync(fixture.Now)).Value!;
        var stage = summaries.Single(item => item.SubStageId == fixture.StageA.Id);

        Assert.Equal(1, stage.AssignedWorkersCount);
        Assert.Equal(1, stage.MainStageDistinctWorkersCount);
        Assert.Equal(1, stage.ProductionLineDistinctWorkersCount);
        Assert.Equal(1, stage.FactoryDistinctWorkersCount);
    }

    [Fact]
    public async Task Hierarchy_without_assignments_reports_zero_workers()
    {
        await using var fixture = await CoverageFixture.CreateAsync(1);

        var summaries = (await fixture.Engine.GetActiveSubStageAssignmentCoverageAsync(fixture.Now)).Value!;

        Assert.All(summaries, summary => Assert.Equal(0, summary.AssignedWorkersCount));
        Assert.All(summaries, summary => Assert.Equal(0, summary.MainStageDistinctWorkersCount));
        Assert.All(summaries, summary => Assert.Equal(0, summary.ProductionLineDistinctWorkersCount));
        Assert.All(summaries, summary => Assert.Equal(0, summary.FactoryDistinctWorkersCount));
    }

    private sealed class CoverageFixture : IAsyncDisposable
    {
        private CoverageFixture(
            AppDbContext db,
            AssignmentEngine engine,
            Guid actorId,
            DateTime now,
            SubStage stageA,
            SubStage stageB,
            Worker[] workers)
        {
            Db = db;
            Engine = engine;
            ActorId = actorId;
            Now = now;
            StageA = stageA;
            StageB = stageB;
            Workers = workers;
        }

        public AppDbContext Db { get; }
        public AssignmentEngine Engine { get; }
        public Guid ActorId { get; }
        public DateTime Now { get; }
        public SubStage StageA { get; }
        public SubStage StageB { get; }
        public Worker[] Workers { get; }

        public static async Task<CoverageFixture> CreateAsync(int workerCount)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
            var mainStage = new MainStage(Guid.NewGuid(), line.Id, "Main", 1);
            var stageA = new SubStage(Guid.NewGuid(), mainStage.Id, "Stage A", "A", 1, 1);
            var stageB = new SubStage(Guid.NewGuid(), mainStage.Id, "Stage B", "B", 1, 2);
            var workers = Enumerable.Range(1, workerCount)
                .Select(index => new Worker(Guid.NewGuid(), $"W{index}", $"Worker {index}"))
                .ToArray();
            db.AddRange(factory, line, mainStage, stageA, stageB);
            db.Workers.AddRange(workers);
            await db.SaveChangesAsync();

            return new CoverageFixture(
                db,
                new AssignmentEngine(db, new RecordingAuditEngine()),
                Guid.NewGuid(),
                new DateTime(2026, 7, 19, 9, 0, 0, DateTimeKind.Utc),
                stageA,
                stageB,
                workers);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
