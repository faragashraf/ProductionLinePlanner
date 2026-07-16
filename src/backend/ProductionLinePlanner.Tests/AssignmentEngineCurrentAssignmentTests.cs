using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
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
    public async Task Current_worker_assignment_list_excludes_former_workers()
    {
        await using var fixture = AssignmentFixture.Create();
        fixture.Worker.SetEmploymentStatus(EmploymentStatus.LeftEmployment, DateTime.UtcNow, DateTime.UtcNow);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Engine.GetSubStageWorkersAsync(fixture.DefaultSubStage.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
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

    [Fact]
    public async Task Permanent_stage_participations_allow_multiple_stages_without_a_reason_and_cancel_only_the_selected_stage()
    {
        await using var fixture = AssignmentFixture.Create();
        fixture.Db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(
            Guid.NewGuid(), fixture.Worker.Id, fixture.DefaultSubStage.Id, fixture.ActorId, DateTime.UtcNow));
        await fixture.Db.SaveChangesAsync();

        var additionalParticipation = await fixture.Engine.CreateOrUpdateDefaultAssignmentAsync(
            new CreateDefaultAssignmentRequest { WorkerId = fixture.Worker.Id, SubStageId = fixture.TemporarySubStage.Id },
            fixture.ActorId);
        Assert.True(additionalParticipation.IsSuccess);
        Assert.Equal(2, await fixture.Db.WorkerDefaultAssignments.CountAsync(x => x.WorkerId == fixture.Worker.Id && x.IsActive));

        var duplicate = await fixture.Engine.CreateOrUpdateDefaultAssignmentAsync(
            new CreateDefaultAssignmentRequest { WorkerId = fixture.Worker.Id, SubStageId = fixture.TemporarySubStage.Id },
            fixture.ActorId);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("Conflict", duplicate.Error?.Code);

        var missingRemovalReason = await fixture.Engine.RemoveDefaultAssignmentAsync(fixture.Worker.Id, fixture.TemporarySubStage.Id, "", fixture.ActorId);
        Assert.True(missingRemovalReason.IsFailure);
        var removed = await fixture.Engine.RemoveDefaultAssignmentAsync(fixture.Worker.Id, fixture.TemporarySubStage.Id, "Shift completed", fixture.ActorId);
        Assert.True(removed.IsSuccess);
        var remaining = await fixture.Db.WorkerDefaultAssignments.Where(x => x.WorkerId == fixture.Worker.Id && x.IsActive).ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(fixture.DefaultSubStage.Id, remaining.Single().SubStageId);
        Assert.Contains(await fixture.Db.AssignmentTimelineEntries.ToListAsync(), entry => entry.ActionType == "Cancel" && entry.Reason == "Shift completed");
    }

    [Fact]
    public async Task Permanent_assignment_without_reason_creates_an_additional_stage_participation()
    {
        await using var fixture = AssignmentFixture.Create();

        var first = await fixture.Engine.CreateOrUpdateDefaultAssignmentAsync(
            new CreateDefaultAssignmentRequest { WorkerId = fixture.Worker.Id, SubStageId = fixture.DefaultSubStage.Id },
            fixture.ActorId);
        var additional = await fixture.Engine.CreateOrUpdateDefaultAssignmentAsync(
            new CreateDefaultAssignmentRequest { WorkerId = fixture.Worker.Id, SubStageId = fixture.TemporarySubStage.Id },
            fixture.ActorId);

        Assert.True(first.IsSuccess);
        Assert.True(additional.IsSuccess);
        Assert.Equal(2, await fixture.Db.WorkerDefaultAssignments.CountAsync(x => x.WorkerId == fixture.Worker.Id && x.IsActive));
    }

    [Fact]
    public async Task Bulk_stage_selection_adds_and_removes_only_permanent_participations_for_the_selected_stage()
    {
        await using var fixture = AssignmentFixture.Create();
        var otherWorker = new Worker(Guid.NewGuid(), "W-2", "Worker Two");
        var now = DateTime.UtcNow;
        fixture.Db.AddRange(
            otherWorker,
            new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Worker.Id, fixture.DefaultSubStage.Id, fixture.ActorId, now.AddMinutes(-2)),
            new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Worker.Id, fixture.TemporarySubStage.Id, fixture.ActorId, now.AddMinutes(-1)));
        await fixture.Db.SaveChangesAsync();

        var duplicateRequest = await fixture.Engine.UpdateStageDefaultAssignmentsAsync(
            fixture.DefaultSubStage.Id,
            [otherWorker.Id, otherWorker.Id],
            fixture.ActorId);
        var result = await fixture.Engine.UpdateStageDefaultAssignmentsAsync(
            fixture.DefaultSubStage.Id,
            [otherWorker.Id],
            fixture.ActorId);

        Assert.True(duplicateRequest.IsFailure);
        Assert.Equal("ValidationError", duplicateRequest.Error?.Code);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.AddedWorkersCount);
        Assert.Equal(1, result.Value.RemovedWorkersCount);
        Assert.Equal([otherWorker.Id], result.Value.ActiveWorkerIds);

        var activeAssignments = await fixture.Db.WorkerDefaultAssignments
            .Where(assignment => assignment.IsActive)
            .ToListAsync();
        Assert.Contains(activeAssignments, assignment => assignment.WorkerId == otherWorker.Id && assignment.SubStageId == fixture.DefaultSubStage.Id);
        Assert.Contains(activeAssignments, assignment => assignment.WorkerId == fixture.Worker.Id && assignment.SubStageId == fixture.TemporarySubStage.Id);
        Assert.DoesNotContain(activeAssignments, assignment => assignment.WorkerId == fixture.Worker.Id && assignment.SubStageId == fixture.DefaultSubStage.Id);
        Assert.Contains(await fixture.Db.AssignmentTimelineEntries.ToListAsync(), entry =>
            entry.WorkerId == fixture.Worker.Id &&
            entry.FromSubStageId == fixture.DefaultSubStage.Id &&
            entry.ActionType == "Cancel");
    }

    [Fact]
    public async Task Temporary_move_ends_the_source_and_creates_the_destination_as_one_assignment_operation()
    {
        await using var fixture = AssignmentFixture.Create();
        var now = DateTime.UtcNow;
        fixture.Db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Worker.Id, fixture.DefaultSubStage.Id, fixture.ActorId, now.AddHours(-2)));
        var source = new WorkerTemporaryAssignment(Guid.NewGuid(), fixture.Worker.Id, fixture.DefaultSubStage.Id, fixture.TemporarySubStage.Id, now.AddMinutes(-15), now.AddHours(1), fixture.ActorId, "Original move", status: "Active");
        fixture.Db.WorkerTemporaryAssignments.Add(source);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Engine.MoveCurrentAssignmentAsync(new MoveCurrentWorkerAssignmentRequest
        {
            WorkerId = fixture.Worker.Id,
            SourceAssignmentId = source.Id,
            FromSubStageId = fixture.TemporarySubStage.Id,
            ToSubStageId = fixture.DefaultSubStage.Id,
            EffectiveAtUtc = now,
            TemporaryEndAtUtc = now.AddMinutes(30),
            Reason = "تغيير تشغيل الوردية"
        }, fixture.ActorId);

        Assert.True(result.IsSuccess);
        var all = await fixture.Db.WorkerTemporaryAssignments.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.Equal("Cancelled", all.Single(x => x.Id == source.Id).Status);
        var replacement = all.Single(x => x.Id == result.Value!.AssignmentId);
        Assert.Equal(fixture.TemporarySubStage.Id, replacement.FromSubStageId);
        Assert.Equal(fixture.DefaultSubStage.Id, replacement.ToSubStageId);
        Assert.Equal(now, all.Single(x => x.Id == source.Id).EndAtUtc);
    }

    [Fact]
    public async Task Temporary_assignments_use_start_inclusive_end_exclusive_boundaries_and_reject_overlap()
    {
        await using var fixture = AssignmentFixture.Create();
        var start = DateTime.UtcNow.AddHours(1);
        var end = start.AddHours(1);
        fixture.Db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Worker.Id, fixture.DefaultSubStage.Id, fixture.ActorId, start.AddHours(-1)));
        await fixture.Db.SaveChangesAsync();

        var first = await fixture.Engine.CreateTemporaryAssignmentAsync(new CreateTemporaryAssignmentRequest
        {
            WorkerId = fixture.Worker.Id,
            FromSubStageId = fixture.DefaultSubStage.Id,
            ToSubStageId = fixture.TemporarySubStage.Id,
            StartAtUtc = start,
            EndAtUtc = end,
            Reason = "وردية مؤقتة"
        }, fixture.ActorId);
        var startState = await fixture.Engine.ResolveCurrentAssignmentsAsync([fixture.Worker.Id], start);
        var overlapping = await fixture.Engine.CreateTemporaryAssignmentAsync(new CreateTemporaryAssignmentRequest
        {
            WorkerId = fixture.Worker.Id,
            FromSubStageId = fixture.DefaultSubStage.Id,
            ToSubStageId = fixture.TemporarySubStage.Id,
            StartAtUtc = start,
            EndAtUtc = end.AddHours(1),
            Reason = "تداخل غير مسموح"
        }, fixture.ActorId);
        var endState = await fixture.Engine.ResolveCurrentAssignmentsAsync([fixture.Worker.Id], end);

        Assert.True(first.IsSuccess);
        Assert.Equal(fixture.TemporarySubStage.Id, startState.Value![fixture.Worker.Id].EffectiveSubStageId);
        Assert.Equal(fixture.DefaultSubStage.Id, endState.Value![fixture.Worker.Id].EffectiveSubStageId);
        Assert.True(overlapping.IsFailure);
        Assert.Equal("Conflict", overlapping.Error?.Code);
    }

    [Fact]
    public async Task Additional_temporary_participation_preserves_all_permanent_stages_and_expiration_removes_only_the_temporary_stage()
    {
        await using var fixture = AssignmentFixture.Create();
        var start = new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(2);
        fixture.Db.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Worker.Id, fixture.DefaultSubStage.Id, fixture.ActorId, start.AddHours(-1)));
        await fixture.Db.SaveChangesAsync();

        var temporary = await fixture.Engine.CreateTemporaryAssignmentAsync(new CreateTemporaryAssignmentRequest
        {
            WorkerId = fixture.Worker.Id,
            ToSubStageId = fixture.TemporarySubStage.Id,
            StartAtUtc = start,
            EndAtUtc = end,
            Reason = "دعم مؤقت إضافي",
            ParticipationMode = TemporaryAssignmentMode.AdditionalParticipation
        }, fixture.ActorId);
        var during = await fixture.Engine.ResolveEffectiveAssignmentsAsync([fixture.Worker.Id], start.AddMinutes(30));
        var after = await fixture.Engine.ResolveEffectiveAssignmentsAsync([fixture.Worker.Id], end);

        Assert.True(temporary.IsSuccess);
        Assert.Equal(new[] { fixture.DefaultSubStage.Id, fixture.TemporarySubStage.Id }.Order(), during.Value![fixture.Worker.Id].Select(assignment => assignment.EffectiveSubStageId!.Value).Order());
        Assert.Equal([fixture.DefaultSubStage.Id], after.Value![fixture.Worker.Id].Select(assignment => assignment.EffectiveSubStageId!.Value).ToArray());
        Assert.True((await fixture.Db.WorkerDefaultAssignments.SingleAsync()).IsActive);
        Assert.Equal("Completed", (await fixture.Db.WorkerTemporaryAssignments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Explicit_move_deactivates_only_the_selected_source_participation()
    {
        await using var fixture = AssignmentFixture.Create();
        var destination = new SubStage(Guid.NewGuid(), fixture.DefaultSubStage.MainStageId, "Destination", "DST", 1, 3);
        var sourceA = new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Worker.Id, fixture.DefaultSubStage.Id, fixture.ActorId, DateTime.UtcNow.AddMinutes(-2));
        var sourceB = new WorkerDefaultAssignment(Guid.NewGuid(), fixture.Worker.Id, fixture.TemporarySubStage.Id, fixture.ActorId, DateTime.UtcNow.AddMinutes(-1));
        fixture.Db.AddRange(destination, sourceA, sourceB);
        await fixture.Db.SaveChangesAsync();

        var moved = await fixture.Engine.MoveCurrentAssignmentAsync(new MoveCurrentWorkerAssignmentRequest
        {
            WorkerId = fixture.Worker.Id,
            SourceAssignmentId = sourceB.Id,
            FromSubStageId = fixture.TemporarySubStage.Id,
            ToSubStageId = destination.Id,
            EffectiveAtUtc = DateTime.UtcNow,
            Reason = "نقل مقصود لمشاركة واحدة"
        }, fixture.ActorId);

        Assert.True(moved.IsSuccess);
        var activeStages = await fixture.Db.WorkerDefaultAssignments
            .Where(assignment => assignment.WorkerId == fixture.Worker.Id && assignment.IsActive)
            .Select(assignment => assignment.SubStageId)
            .ToArrayAsync();
        Assert.Contains(fixture.DefaultSubStage.Id, activeStages);
        Assert.Contains(destination.Id, activeStages);
        Assert.DoesNotContain(fixture.TemporarySubStage.Id, activeStages);
    }

    [Fact]
    public async Task Concurrent_current_assignment_removal_is_detected()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var workerId = Guid.NewGuid();
        var subStageId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assignedAt = DateTime.UtcNow.AddMinutes(-5);
        await using (var seed = new AppDbContext(options))
        {
            seed.WorkerDefaultAssignments.Add(new WorkerDefaultAssignment(Guid.NewGuid(), workerId, subStageId, actorId, assignedAt));
            await seed.SaveChangesAsync();
        }

        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        var firstAssignment = await first.WorkerDefaultAssignments.SingleAsync();
        var secondAssignment = await second.WorkerDefaultAssignments.SingleAsync();
        firstAssignment.Deactivate(assignedAt.AddMinutes(1));
        secondAssignment.Deactivate(assignedAt.AddMinutes(2));
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
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
