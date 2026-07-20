using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class Structure001HierarchyTests
{
    [Fact]
    public async Task Department_catalog_rejects_case_insensitive_duplicate_and_active_line_deactivation()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC", createdAtUtc: now);
        db.Factories.Add(factory);
        await db.SaveChangesAsync();
        var audit = new AuditEngine(db);
        var catalog = new DepartmentCatalogService(db, audit);
        var actor = Guid.NewGuid();

        var first = await catalog.CreateAsync(factory.Id, "cut", "القص", null, 1, true, actor);
        Assert.True(first.IsSuccess);
        var duplicate = await catalog.CreateAsync(factory.Id, "CUT", "قسم آخر", null, 2, true, actor);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("Conflict", duplicate.Error!.Code);

        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1, departmentId: first.Value!.Id);
        db.ProductionLines.Add(line);
        await db.SaveChangesAsync();
        var deactivate = await catalog.UpdateAsync(first.Value.Id, null, null, null, null, false, actor);

        Assert.True(deactivate.IsFailure);
        Assert.Equal("Conflict", deactivate.Error!.Code);
    }

    [Fact]
    public async Task Operational_stage_uses_line_group_and_next_legacy_stage_code()
    {
        await using var db = CreateDb();
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var department = new Department(Guid.NewGuid(), factory.Id, "CUT", "القص", null, 1);
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1, departmentId: department.Id);
        var group = new MainStage(Guid.NewGuid(), line.Id, "Grouping", 1);
        var legacy = new SubStage(Guid.NewGuid(), group.Id, "Legacy", "STG099", 0, 1, productionLineId: line.Id);
        db.AddRange(factory, department, line, group, legacy);
        await db.SaveChangesAsync();

        var service = new ProductionStageCatalogService(db, new AuditEngine(db), new StageDependencyInspector(db));
        var result = await service.CreateOperationalStageAsync(line.Id, null, "Operational", 2, 4, true, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(group.Id, result.Value!.MainStageId);
        Assert.Equal(line.Id, result.Value.ProductionLineId);
        Assert.Equal("STG100", result.Value.Code);
    }

    [Fact]
    public async Task Operational_stage_uses_one_deterministic_legacy_group_when_the_line_has_multiple_groups()
    {
        await using var db = CreateDb();
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
        var first = new MainStage(Guid.NewGuid(), line.Id, "First", 1);
        var second = new MainStage(Guid.NewGuid(), line.Id, "Second", 2);
        db.AddRange(factory, line, second, first);
        await db.SaveChangesAsync();

        var result = await new ProductionStageCatalogService(db, new AuditEngine(db), new StageDependencyInspector(db))
            .CreateOperationalStageAsync(line.Id, null, "Operational", 1, 1, true, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(first.Id, result.Value!.MainStageId);
        Assert.Equal(line.Id, result.Value.ProductionLineId);
    }

    [Fact]
    public async Task Operational_stage_creates_one_internal_legacy_group_only_when_the_line_has_no_active_group()
    {
        await using var db = CreateDb();
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
        db.AddRange(factory, line);
        await db.SaveChangesAsync();
        var service = new ProductionStageCatalogService(db, new AuditEngine(db), new StageDependencyInspector(db));

        var first = await service.CreateOperationalStageAsync(line.Id, null, "First", 1, 1, true, Guid.NewGuid());
        var second = await service.CreateOperationalStageAsync(line.Id, null, "Second", 2, 1, true, Guid.NewGuid());

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var groups = await db.MainStages.Where(item => item.ProductionLineId == line.Id).ToArrayAsync();
        Assert.Single(groups);
        Assert.Equal(groups[0].Id, first.Value!.MainStageId);
        Assert.Equal(groups[0].Id, second.Value!.MainStageId);
        Assert.All(await db.SubStages.Where(item => item.ProductionLineId == line.Id).ToArrayAsync(), item => Assert.Equal(line.Id, item.ProductionLineId));
    }

    [Fact]
    public async Task Stage_dependency_inspector_blocks_disable_for_active_assignments_but_preserves_history_for_delete()
    {
        await using var db = CreateDb();
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
        var group = new MainStage(Guid.NewGuid(), line.Id, "Grouping", 1);
        var stage = new SubStage(Guid.NewGuid(), group.Id, "Operational", "STG001", 0, 1, productionLineId: line.Id);
        var worker = new Worker(Guid.NewGuid(), "100", "Worker", null, null, null, true);
        var assignment = new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, stage.Id, Guid.NewGuid(), DateTime.UtcNow);
        db.AddRange(factory, line, group, stage, worker, assignment);
        await db.SaveChangesAsync();

        var result = await new StageDependencyInspector(db).InspectAsync(stage.Id);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanDisable);
        Assert.False(result.Value.CanDelete);
        Assert.Contains(result.Value.ActiveBlockers, x => x.Key == "active-default-assignments");
        Assert.Contains(result.Value.HistoricalDependencies, x => x.Key == "default-assignments");
    }

    [Fact]
    public void Migration_is_additive_and_uses_validated_backfill_without_zero_guid_default()
    {
        var migrationPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ProductionLinePlanner.Infrastructure", "Data", "Migrations", "20260720000006_CorrectDepartmentLineStageHierarchy.cs");
        var migration = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("UPDATE [s]", migration);
        Assert.Contains("StageCodeSequence", migration);
        Assert.Contains("THROW 51004", migration);
        Assert.DoesNotContain("defaultValue: new Guid(\"00000000-0000-0000-0000-000000000000\")", migration);
        Assert.DoesNotContain("DropTable(\n                name: \"SubStages\"", migration);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }
}
