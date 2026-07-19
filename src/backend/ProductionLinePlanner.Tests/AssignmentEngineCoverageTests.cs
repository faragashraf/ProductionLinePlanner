using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class AssignmentEngineCoverageTests
{
    [Fact]
    public async Task Active_sub_stage_coverage_uses_effective_default_and_temporary_assignments_without_attendance()
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

        Assert.Equal(2, coveredSummary.AssignedWorkersCount);
        Assert.Equal(2, coveredSummary.RequiredWorkersCount);
        Assert.Equal(100, coveredSummary.AssignmentCoveragePercent);
        Assert.Equal("Staffed", coveredSummary.StaffingStatus);

        Assert.Equal(1, partialSummary.AssignedWorkersCount);
        Assert.Equal(2, partialSummary.RequiredWorkersCount);
        Assert.Equal(50, partialSummary.AssignmentCoveragePercent);
        Assert.Equal("Understaffed", partialSummary.StaffingStatus);

        Assert.Equal(0, emptySummary.AssignedWorkersCount);
        Assert.Equal(1, emptySummary.RequiredWorkersCount);
        Assert.Equal(0, emptySummary.AssignmentCoveragePercent);
        Assert.Equal("Unstaffed", emptySummary.StaffingStatus);

        Assert.Equal(1, undefinedSummary.AssignedWorkersCount);
        Assert.False(undefinedSummary.HasAuthoritativeRequiredWorkerCount);
        Assert.Null(undefinedSummary.RequiredWorkersCount);
        Assert.Null(undefinedSummary.AssignmentCoveragePercent);
        Assert.Equal("RequirementNotDefined", undefinedSummary.StaffingStatus);
    }
}
