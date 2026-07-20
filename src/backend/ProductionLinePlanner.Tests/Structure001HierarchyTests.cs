using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
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
        var result = await service.CreateOperationalStageAsync(line.Id, "Operational", 4, true, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(group.Id, result.Value!.MainStageId);
        Assert.Equal(line.Id, result.Value.ProductionLineId);
        Assert.Equal("STG100", result.Value.Code);
        Assert.Equal(2, result.Value.DefaultOrder);
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
            .CreateOperationalStageAsync(line.Id, "Operational", 1, true, Guid.NewGuid());

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

        var first = await service.CreateOperationalStageAsync(line.Id, "First", 1, true, Guid.NewGuid());
        var second = await service.CreateOperationalStageAsync(line.Id, "Second", 1, true, Guid.NewGuid());

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var groups = await db.MainStages.Where(item => item.ProductionLineId == line.Id).ToArrayAsync();
        Assert.Single(groups);
        Assert.Equal(groups[0].Id, first.Value!.MainStageId);
        Assert.Equal(groups[0].Id, second.Value!.MainStageId);
        Assert.All(await db.SubStages.Where(item => item.ProductionLineId == line.Id).ToArrayAsync(), item => Assert.Equal(line.Id, item.ProductionLineId));
        Assert.Equal(1, first.Value.DefaultOrder);
        Assert.Equal(2, second.Value.DefaultOrder);
    }

    [Fact]
    public async Task Operational_stage_allocates_after_the_highest_existing_order_including_inactive_stages()
    {
        await using var db = CreateDb();
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
        var group = new MainStage(Guid.NewGuid(), line.Id, "Grouping", 1);
        var active = new SubStage(Guid.NewGuid(), group.Id, "Active", "STG001", 0, 2, productionLineId: line.Id);
        var inactive = new SubStage(Guid.NewGuid(), group.Id, "Inactive", "STG002", 0, 7, isActive: false, productionLineId: line.Id);
        db.AddRange(factory, line, group, active, inactive);
        await db.SaveChangesAsync();

        var result = await new ProductionStageCatalogService(db, new AuditEngine(db), new StageDependencyInspector(db))
            .CreateOperationalStageAsync(line.Id, "Operational", 1, true, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(8, result.Value!.DefaultOrder);
    }

    [Fact]
    public async Task Operational_stage_allocates_independent_orders_for_different_lines()
    {
        await using var db = CreateDb();
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var firstLine = new ProductionLine(Guid.NewGuid(), factory.Id, "First", 1);
        var secondLine = new ProductionLine(Guid.NewGuid(), factory.Id, "Second", 2);
        db.AddRange(factory, firstLine, secondLine);
        await db.SaveChangesAsync();
        var service = new ProductionStageCatalogService(db, new AuditEngine(db), new StageDependencyInspector(db));

        var first = await service.CreateOperationalStageAsync(firstLine.Id, "First", 1, true, Guid.NewGuid());
        var second = await service.CreateOperationalStageAsync(secondLine.Id, "Second", 1, true, Guid.NewGuid());

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, first.Value!.DefaultOrder);
        Assert.Equal(1, second.Value!.DefaultOrder);
    }

    [Fact]
    public async Task Operational_stage_request_cannot_override_the_server_allocated_order()
    {
        await using var db = CreateDb();
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
        var group = new MainStage(Guid.NewGuid(), line.Id, "Grouping", 1);
        var existing = new SubStage(Guid.NewGuid(), group.Id, "Existing", "STG001", 0, 4, productionLineId: line.Id);
        db.AddRange(factory, line, group, existing);
        await db.SaveChangesAsync();

        var request = System.Text.Json.JsonSerializer.Deserialize<CreateOperationalStageRequest>(
            $$"""{"productionLineId":"{{line.Id}}","name":"Operational","capacity":1,"defaultOrder":1}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(request);
        Assert.DoesNotContain(typeof(CreateOperationalStageRequest).GetProperties(), property => property.Name == "DefaultOrder");

        var result = await new ProductionStageCatalogService(db, new AuditEngine(db), new StageDependencyInspector(db))
            .CreateOperationalStageAsync(request!.ProductionLineId, request.Name, request.Capacity, request.IsActive, Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(5, result.Value!.DefaultOrder);
    }

    [Fact]
    public async Task Concurrent_operational_stage_creates_allocate_distinct_orders_in_one_group()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"file:stage-order-{Guid.NewGuid():N}?mode=memory&cache=shared",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        }.ToString();
        await using var setupConnection = CreateSqliteConnection(connectionString);
        await setupConnection.OpenAsync();
        var setupOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(setupConnection).Options;
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
        var group = new MainStage(Guid.NewGuid(), line.Id, "Grouping", 1);
        var actor = new AppUser(Guid.NewGuid(), "Test Actor", "actor@example.test", "hash");
        await using (var setup = new AppDbContext(setupOptions))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.AddRange(factory, line, group, actor);
            await setup.SaveChangesAsync();
        }

        await using var firstConnection = CreateSqliteConnection(connectionString);
        await using var secondConnection = CreateSqliteConnection(connectionString);
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        await using var firstDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(firstConnection).Options);
        await using var secondDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(secondConnection).Options);
        var firstService = new ProductionStageCatalogService(firstDb, new AuditEngine(firstDb), new StageDependencyInspector(firstDb));
        var secondService = new ProductionStageCatalogService(secondDb, new AuditEngine(secondDb), new StageDependencyInspector(secondDb));

        var results = await Task.WhenAll(
            Task.Run(() => firstService.CreateOperationalStageAsync(line.Id, "First", 1, true, actor.Id)),
            Task.Run(() => secondService.CreateOperationalStageAsync(line.Id, "Second", 1, true, actor.Id)));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal([1, 2], results.Select(result => result.Value!.DefaultOrder).OrderBy(order => order).ToArray());
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
    public async Task Operational_stage_deactivation_returns_the_persisted_inactive_stage_and_is_idempotent()
    {
        await using var db = CreateDb();
        var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1);
        var group = new MainStage(Guid.NewGuid(), line.Id, "Grouping", 1);
        var stage = new SubStage(Guid.NewGuid(), group.Id, "Operational", "STG001", 0, 1, productionLineId: line.Id);
        db.AddRange(factory, line, group, stage);
        await db.SaveChangesAsync();
        var service = new ProductionStageCatalogService(db, new AuditEngine(db), new StageDependencyInspector(db));

        var first = await service.DeactivateSubStageAsync(stage.Id, Guid.NewGuid());
        var second = await service.DeactivateSubStageAsync(stage.Id, Guid.NewGuid());

        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Equal(stage.Id, first.Value!.Id);
        Assert.False(first.Value.IsActive);
        Assert.False((await db.SubStages.SingleAsync(item => item.Id == stage.Id)).IsActive);
        Assert.True(second.IsSuccess, second.Error?.Message);
        Assert.Equal(stage.Id, second.Value!.Id);
        Assert.False(second.Value.IsActive);
    }

    [Fact]
    public async Task Operational_stage_deactivation_blocks_before_persistence_and_only_missing_stage_is_not_found()
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
        var service = new ProductionStageCatalogService(db, new AuditEngine(db), new StageDependencyInspector(db));

        var blocked = await service.DeactivateSubStageAsync(stage.Id, Guid.NewGuid());
        var missing = await service.DeactivateSubStageAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(blocked.IsFailure);
        Assert.Equal("Conflict", blocked.Error!.Code);
        Assert.True((await db.SubStages.SingleAsync(item => item.Id == stage.Id)).IsActive);
        Assert.True(missing.IsFailure);
        Assert.Equal("NotFound", missing.Error!.Code);
        Assert.Equal("المرحلة غير موجودة.", missing.Error.Message);
        Assert.DoesNotContain("Sub stage", missing.Error.Message, StringComparison.OrdinalIgnoreCase);
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

    private static SqliteConnection CreateSqliteConnection(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        return connection;
    }
}
