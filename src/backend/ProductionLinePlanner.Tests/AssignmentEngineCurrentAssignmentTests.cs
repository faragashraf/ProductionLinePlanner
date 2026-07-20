using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
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
    public async Task Expired_temporary_assignment_is_not_current_and_assignment_read_does_not_write()
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
        Assert.Equal("Active", (await fixture.Db.WorkerTemporaryAssignments.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await fixture.Db.AssignmentTimelineEntries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Explicit_finalization_is_the_only_path_that_completes_expired_temporary_assignments()
    {
        await using var fixture = AssignmentFixture.Create();
        var asOfUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        fixture.Db.WorkerTemporaryAssignments.Add(new WorkerTemporaryAssignment(
            Guid.NewGuid(), fixture.Worker.Id, fixture.DefaultSubStage.Id, fixture.TemporarySubStage.Id,
            asOfUtc.AddHours(-3), asOfUtc.AddHours(-1), fixture.ActorId, "Expired temporary", status: "Active"));
        await fixture.Db.SaveChangesAsync();

        var finalized = await fixture.Engine.FinalizeCompletedTemporaryAssignmentsAsync(asOfUtc);

        Assert.True(finalized.IsSuccess);
        Assert.Equal(1, finalized.Value);
        Assert.Equal("Completed", (await fixture.Db.WorkerTemporaryAssignments.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await fixture.Db.AssignmentTimelineEntries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Assignment_read_does_not_invoke_SaveChanges_when_expired_assignments_exist()
    {
        var saveInterceptor = new ThrowingSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(saveInterceptor)
            .Options;
        var asOfUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        var workerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using var db = new AppDbContext(options);
        db.AddRange(
            new WorkerDefaultAssignment(Guid.NewGuid(), workerId, Guid.NewGuid(), actorId, asOfUtc.AddDays(-1)),
            new WorkerTemporaryAssignment(Guid.NewGuid(), workerId, Guid.NewGuid(), Guid.NewGuid(), asOfUtc.AddHours(-3), asOfUtc.AddHours(-1), actorId, "Expired temporary", status: "Active"));
        await db.SaveChangesAsync();
        saveInterceptor.ThrowOnSave = true;
        var engine = new AssignmentEngine(db, new NoopAuditEngine());

        var result = await engine.ResolveCurrentAssignmentsAsync([workerId], asOfUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, saveInterceptor.BlockedSaveAttempts);
    }

    [Fact]
    public async Task Active_historical_temporary_assignment_is_not_included_in_current_assignment_reads()
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
        Assert.Equal(fixture.DefaultSubStage.Id, assignment.Value.EffectiveSubStageId);
        Assert.Equal(AssignmentType.Default, assignment.Value.AssignmentType);
        Assert.Null(assignment.Value.EndsAtUtc);
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

    [Theory]
    [InlineData("TemporaryMove")]
    [InlineData("AdditionalParticipation")]
    [InlineData("Replacement")]
    [InlineData("Move")]
    public async Task Non_permanent_assignment_commands_are_rejected_without_writing(string command)
    {
        await using var fixture = AssignmentFixture.Create();
        var start = DateTime.UtcNow.AddHours(1);
        Result<AssignmentActionResultDto> result = command switch
        {
            "TemporaryMove" => await fixture.Engine.CreateTemporaryAssignmentAsync(new CreateTemporaryAssignmentRequest
            {
                WorkerId = fixture.Worker.Id,
                FromSubStageId = fixture.DefaultSubStage.Id,
                ToSubStageId = fixture.TemporarySubStage.Id,
                StartAtUtc = start,
                EndAtUtc = start.AddHours(1),
                Reason = "تسكين غير دائم",
                ParticipationMode = TemporaryAssignmentMode.TemporaryMove
            }, fixture.ActorId),
            "AdditionalParticipation" => await fixture.Engine.CreateTemporaryAssignmentAsync(new CreateTemporaryAssignmentRequest
            {
                WorkerId = fixture.Worker.Id,
                ToSubStageId = fixture.TemporarySubStage.Id,
                StartAtUtc = start,
                EndAtUtc = start.AddHours(1),
                Reason = "مشاركة غير دائمة",
                ParticipationMode = TemporaryAssignmentMode.AdditionalParticipation
            }, fixture.ActorId),
            "Replacement" => await fixture.Engine.CreateReplacementAssignmentAsync(new CreateReplacementAssignmentRequest
            {
                ReplacementWorkerId = fixture.Worker.Id,
                ReplacedWorkerId = Guid.NewGuid(),
                SubStageId = fixture.TemporarySubStage.Id,
                StartAtUtc = start,
                EndAtUtc = start.AddHours(1),
                Reason = "استبدال غير دائم"
            }, fixture.ActorId),
            _ => await fixture.Engine.MoveCurrentAssignmentAsync(new MoveCurrentWorkerAssignmentRequest
            {
                WorkerId = fixture.Worker.Id,
                SourceAssignmentId = Guid.NewGuid(),
                FromSubStageId = fixture.DefaultSubStage.Id,
                ToSubStageId = fixture.TemporarySubStage.Id,
                EffectiveAtUtc = start,
                Reason = "نقل غير دائم"
            }, fixture.ActorId)
        };

        Assert.True(result.IsFailure);
        Assert.Equal("FeatureDisabled", result.Error?.Code);
        Assert.Empty(await fixture.Db.WorkerTemporaryAssignments.ToArrayAsync());
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

    private sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public bool ThrowOnSave { get; set; }
        public int BlockedSaveAttempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                BlockedSaveAttempts++;
                throw new InvalidOperationException("Assignment reads must not save changes.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
