using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class AssignmentEngineCurrentAssignmentTests
{
    [Fact]
    public async Task Expired_temporary_assignment_is_finalized_and_not_current()
    {
        await using var fixture = AssignmentFixture.Create();
        var asOfUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        var defaultAssignment = new WorkerDefaultAssignment(
            Guid.NewGuid(),
            fixture.Worker.Id,
            fixture.DefaultSubStage.Id,
            fixture.ActorId,
            asOfUtc.AddDays(-3));
        var expiredTemporary = new WorkerTemporaryAssignment(
            Guid.NewGuid(),
            fixture.Worker.Id,
            fixture.DefaultSubStage.Id,
            fixture.TemporarySubStage.Id,
            asOfUtc.AddHours(-3),
            asOfUtc.AddHours(-1),
            fixture.ActorId,
            "Expired temporary",
            status: "Active");
        fixture.Db.WorkerDefaultAssignments.Add(defaultAssignment);
        fixture.Db.WorkerTemporaryAssignments.Add(expiredTemporary);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Engine.ResolveCurrentAssignmentsAsync([fixture.Worker.Id], asOfUtc);

        Assert.True(result.IsSuccess);
        var assignment = Assert.Single(result.Value!);
        Assert.Equal(fixture.DefaultSubStage.Id, assignment.Value.EffectiveSubStageId);
        Assert.Equal(AssignmentType.Default, assignment.Value.AssignmentType);
        Assert.Equal("Completed", (await fixture.Db.WorkerTemporaryAssignments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Active_temporary_assignment_with_future_end_is_current()
    {
        await using var fixture = AssignmentFixture.Create();
        var asOfUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        fixture.Db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(
            Guid.NewGuid(),
            fixture.Worker.Id,
            fixture.DefaultSubStage.Id,
            fixture.ActorId,
            asOfUtc.AddDays(-3)));
        fixture.Db.WorkerTemporaryAssignments.Add(new WorkerTemporaryAssignment(
            Guid.NewGuid(),
            fixture.Worker.Id,
            fixture.DefaultSubStage.Id,
            fixture.TemporarySubStage.Id,
            asOfUtc.AddHours(-1),
            asOfUtc.AddHours(1),
            fixture.ActorId,
            "Active temporary",
            status: "Active"));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Engine.ResolveCurrentAssignmentsAsync([fixture.Worker.Id], asOfUtc);

        Assert.True(result.IsSuccess);
        var assignment = Assert.Single(result.Value!);
        Assert.Equal(fixture.TemporarySubStage.Id, assignment.Value.EffectiveSubStageId);
        Assert.Equal(AssignmentType.Temporary, assignment.Value.AssignmentType);
        Assert.Equal(asOfUtc.AddHours(1), assignment.Value.EndsAtUtc);
    }

    [Fact]
    public void Temporary_assignment_requires_non_default_end_time()
    {
        var actorId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var fromSubStageId = Guid.NewGuid();
        var toSubStageId = Guid.NewGuid();

        var exception = Assert.Throws<ArgumentException>(() => new WorkerTemporaryAssignment(
            Guid.NewGuid(),
            workerId,
            fromSubStageId,
            toSubStageId,
            new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc),
            default,
            actorId,
            "Invalid end"));

        Assert.Contains("StartAtUtc and EndAtUtc are required", exception.Message);
    }

    private sealed class AssignmentFixture : IAsyncDisposable
    {
        private AssignmentFixture(AppDbContext db, AssignmentEngine engine, Guid actorId, Worker worker, SubStage defaultSubStage, SubStage temporarySubStage)
        {
            Db = db;
            Engine = engine;
            ActorId = actorId;
            Worker = worker;
            DefaultSubStage = defaultSubStage;
            TemporarySubStage = temporarySubStage;
        }

        public AppDbContext Db { get; }
        public AssignmentEngine Engine { get; }
        public Guid ActorId { get; }
        public Worker Worker { get; }
        public SubStage DefaultSubStage { get; }
        public SubStage TemporarySubStage { get; }

        public static AssignmentFixture Create()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            var engine = new AssignmentEngine(db, new NoopAuditEngine());
            var actorId = Guid.NewGuid();
            var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
            var mainStage = new MainStage(Guid.NewGuid(), line.Id, "Main", 1);
            var defaultSubStage = new SubStage(Guid.NewGuid(), mainStage.Id, "Default", "DEF", 1, 1);
            var temporarySubStage = new SubStage(Guid.NewGuid(), mainStage.Id, "Temporary", "TMP", 1, 2);
            var worker = new Worker(Guid.NewGuid(), "W-1", "Worker One");

            db.Factories.Add(factory);
            db.ProductionLines.Add(line);
            db.MainStages.Add(mainStage);
            db.SubStages.AddRange(defaultSubStage, temporarySubStage);
            db.Workers.Add(worker);
            db.SaveChanges();

            return new AssignmentFixture(db, engine, actorId, worker, defaultSubStage, temporarySubStage);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class NoopAuditEngine : IAuditEngine
    {
        public Task<ProductionLinePlanner.Application.Common.Result> RecordAsync(
            Guid actorUserId,
            AuditActionType actionType,
            string entityType,
            string entityId,
            object? before = null,
            object? after = null,
            string? requestMeta = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProductionLinePlanner.Application.Common.Result.Success());
    }
}
