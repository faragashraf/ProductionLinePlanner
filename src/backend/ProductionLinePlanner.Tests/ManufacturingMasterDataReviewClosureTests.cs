using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Data.Migrations;

namespace ProductionLinePlanner.Tests;

public sealed class ManufacturingMasterDataReviewClosureTests
{
    [Fact]
    public void Manufacturing_migration_remediates_existing_sub_stages_before_constraints_and_indexes()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableManufacturingMigration().BuildUp(builder);
        var operations = builder.Operations;

        var addCode = operations.FindIndex(x => x is AddColumnOperation add && add.Table == "SubStages" && add.Name == "Code");
        var codeRemediation = operations.FindIndex(x => x is SqlOperation sql && sql.Sql.Contains("MissingCodes", StringComparison.Ordinal));
        var sequenceRemediation = operations.FindIndex(x => x is SqlOperation sql && sql.Sql.Contains("InvalidOrders", StringComparison.Ordinal));
        var codeIndex = operations.FindIndex(x => x is CreateIndexOperation index && index.Name == "IX_SubStages_Code");
        var sequenceConstraint = operations.FindIndex(x => x is AddCheckConstraintOperation check && check.Name == "CK_SubStage_DefaultOrder_Positive");

        Assert.True(addCode < codeRemediation);
        Assert.True(codeRemediation < codeIndex);
        Assert.True(sequenceRemediation < sequenceConstraint);
        var sql = Assert.IsType<SqlOperation>(operations[codeRemediation]).Sql;
        Assert.Contains("THROW 51000", sql, StringComparison.Ordinal);
        Assert.Contains("SQL_Latin1_General_CP1_CI_AS", sql, StringComparison.Ordinal);

        var sequenceSql = Assert.IsType<SqlOperation>(operations[sequenceRemediation]).Sql;
        Assert.Contains("MaxOrderByMainStage", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("CurrentMaxOrder", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY [MainStageId]", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(m.[CurrentMaxOrder], 0)", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("PARTITION BY s.[MainStageId]", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY s.[Id]", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("WHERE s.[SequenceOrder] <= 0", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("FROM [dbo].[SubStages] s", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN InvalidOrders i ON i.[Id] = s.[Id]", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("SequenceOrderCollisionCheck", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("THROW 51002", sequenceSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sequence_order_remediation_is_partitioned_per_main_stage_and_ordered_by_id()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        new TestableManufacturingMigration().BuildUp(builder);
        var operations = builder.Operations;

        var sequenceOperationIndex = operations.FindIndex(
            x => x is SqlOperation sql && sql.Sql.Contains("InvalidOrders", StringComparison.Ordinal));
        Assert.True(sequenceOperationIndex >= 0);

        var sequenceSql = Assert.IsType<SqlOperation>(operations[sequenceOperationIndex]).Sql;
        Assert.Contains("LEFT JOIN MaxOrderByMainStage", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY s.[MainStageId] ORDER BY s.[Id])", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(m.[CurrentMaxOrder], 0)", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("[CurrentMaxOrder]", sequenceSql, StringComparison.Ordinal);
        Assert.Contains("THROW 51001", sequenceSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_piece_price_preserves_timing_fields_and_explicit_null_clears_them()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        var originalEffectiveFrom = DateTime.UtcNow.Date.AddDays(2);
        var stage = fixture.AddStage(1, 5m, 30m, originalEffectiveFrom);

        var updated = await fixture.Service.UpdateModelStageAsync(
            fixture.Model.Id,
            stage.Id,
            new UpsertProductModelStageRequest { PiecePrice = 8m },
            fixture.ActorUserId);

        Assert.True(updated.IsSuccess);
        Assert.Equal(30m, updated.Value!.StandardSeconds);
        Assert.Equal(originalEffectiveFrom, updated.Value.EffectiveFrom);

        var cleared = await fixture.Service.UpdateModelStageAsync(
            fixture.Model.Id,
            stage.Id,
            new UpsertProductModelStageRequest { HasStandardSeconds = true, HasEffectiveFrom = true },
            fixture.ActorUserId);

        Assert.True(cleared.IsSuccess);
        Assert.Null(cleared.Value!.StandardSeconds);
        Assert.Null(cleared.Value.EffectiveFrom);
    }

    [Fact]
    public async Task Patch_rejects_invalid_new_standard_seconds()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        var stage = fixture.AddStage(1, 5m, 30m, null);

        var result = await fixture.Service.UpdateModelStageAsync(
            fixture.Model.Id,
            stage.Id,
            new UpsertProductModelStageRequest { StandardSeconds = 0m, HasStandardSeconds = true },
            fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("ValidationError", result.Error!.Code);
    }

    [Fact]
    public async Task Stage_order_conflicts_include_inactive_configurations_and_copy_is_prevalidated()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        fixture.AddStage(2, 1m, null, null, isActive: false);

        var add = await fixture.Service.AddModelStageAsync(
            fixture.Model.Id,
            new UpsertProductModelStageRequest
            {
                SubStageId = fixture.SubStages[2].Id,
                StageOrder = 2,
                PiecePrice = 1m,
                CompensationMode = CompensationMode.FixedAmount
            },
            fixture.ActorUserId);

        Assert.True(add.IsFailure);
        Assert.Equal("Conflict", add.Error!.Code);

        var target = new ProductModel(Guid.NewGuid(), "TARGET", "Target");
        fixture.DbContext.ProductModels.Add(target);
        fixture.DbContext.ProductModelStages.Add(new ProductModelStage(Guid.NewGuid(), target.Id, fixture.SubStages[1].Id, 2, 1m, null, CompensationMode.FixedAmount));
        await fixture.DbContext.SaveChangesAsync();

        var copy = await fixture.Service.CopyModelStagesAsync(fixture.Model.Id, new CopyProductModelStagesRequest { TargetModelId = target.Id }, fixture.ActorUserId);
        Assert.True(copy.IsFailure);
        Assert.Equal("Conflict", copy.Error!.Code);
        Assert.Single(fixture.DbContext.ProductModelStages.Where(x => x.ProductModelId == target.Id));
    }

    [Fact]
    public async Task Copy_model_stages_rejects_sub_stage_conflicts_and_copies_all_when_target_is_clear()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        fixture.AddStage(1, 1m, null, null);
        var conflictingTarget = new ProductModel(Guid.NewGuid(), "CONFLICT", "Conflict");
        fixture.DbContext.ProductModels.Add(conflictingTarget);
        fixture.DbContext.ProductModelStages.Add(new ProductModelStage(Guid.NewGuid(), conflictingTarget.Id, fixture.SubStages[0].Id, 4, 1m, null, CompensationMode.FixedAmount));
        await fixture.DbContext.SaveChangesAsync();

        var conflict = await fixture.Service.CopyModelStagesAsync(fixture.Model.Id, new CopyProductModelStagesRequest { TargetModelId = conflictingTarget.Id }, fixture.ActorUserId);
        Assert.True(conflict.IsFailure);
        Assert.Equal("Conflict", conflict.Error!.Code);
        Assert.Single(fixture.DbContext.ProductModelStages.Where(x => x.ProductModelId == conflictingTarget.Id));

        var clearTarget = new ProductModel(Guid.NewGuid(), "CLEAR", "Clear");
        fixture.DbContext.ProductModels.Add(clearTarget);
        await fixture.DbContext.SaveChangesAsync();
        var copied = await fixture.Service.CopyModelStagesAsync(fixture.Model.Id, new CopyProductModelStagesRequest { TargetModelId = clearTarget.Id }, fixture.ActorUserId);

        Assert.True(copied.IsSuccess);
        Assert.Single(fixture.DbContext.ProductModelStages.Where(x => x.ProductModelId == clearTarget.Id));
    }

    [Fact]
    public async Task Current_salary_uses_effective_interval_with_exclusive_end()
    {
        var today = DateTime.UtcNow.Date;
        await using var fixture = await CompensationFixture.CreateAsync();
        // The ranges are adjacent at midnight, where EffectiveTo remains exclusive.
        fixture.DbContext.WorkerSalaryHistories.AddRange(
            new WorkerSalaryHistory(Guid.NewGuid(), fixture.Worker.Id, 100m, "EGP", today.AddDays(-2), today.AddDays(1)),
            new WorkerSalaryHistory(Guid.NewGuid(), fixture.Worker.Id, 200m, "EGP", today.AddDays(1)),
            new WorkerSalaryHistory(Guid.NewGuid(), fixture.Worker.Id, 50m, "EGP", today.AddDays(-4), today.AddDays(-2)));
        await fixture.DbContext.SaveChangesAsync();

        var current = await fixture.Service.GetCurrentSalaryAsync(fixture.Worker.Id, today.AddHours(12));
        var future = await fixture.Service.GetCurrentSalaryAsync(fixture.Worker.Id, today.AddDays(1));
        var ended = await fixture.Service.GetCurrentSalaryAsync(fixture.Worker.Id, today.AddDays(-3));

        Assert.Equal(100m, current.Value!.Amount);
        Assert.Equal(200m, future.Value!.Amount);
        Assert.Equal(50m, ended.Value!.Amount);
    }

    [Fact]
    public void Salary_configuration_has_a_unique_filtered_open_record_index()
    {
        using var dbContext = ProductModelFixture.CreateContext();
        var index = dbContext.Model.FindEntityType(typeof(WorkerSalaryHistory))!.GetIndexes()
            .Single(x => x.GetDatabaseName() == "IX_WorkerSalaryHistories_Current");

        Assert.True(index.IsUnique);
        Assert.Equal("[EffectiveTo] IS NULL", index.GetFilter());
    }

    [Fact]
    public async Task Employee_name_rollback_uses_the_original_external_name_and_reports_reconciliation_failure()
    {
        var worker = new Worker(Guid.NewGuid(), "W-1", "Local Original", "111", attendanceDepartmentId: 1);
        await using var dbContext = ProductModelFixture.CreateContext();
        dbContext.Workers.Add(worker);
        await dbContext.SaveChangesAsync();
        var employees = new Dictionary<string, AttendanceEmployeeRecord?>
        {
            ["111"] = new AttendanceEmployeeRecord("111", 1, "1001", "ZK Original", true)
        };
        var names = new List<string>();
        var writer = new FakeAttendanceEmployeeWriter(
            employees,
            (_, name, _) => { names.Add(name); return Task.FromResult(Result.Success()); },
            (_, _, _) => Task.FromResult(Result.Failure(new Error("ExternalFailure", "Department failed."))));
        var service = new EmployeeMasterDataService(
            dbContext, writer, new FakeAttendanceEmployeeReader(employees),
            new FakeAttendanceDepartmentReader(new Dictionary<int, AttendanceDepartmentRecord> { [2] = new(2, "Quality") }),
            new RecordingAuditEngine());

        var result = await service.UpdateMasterIdentityAsync(worker.Id, new UpdateWorkerRequest { FullName = "New Name", AttendanceDepartmentId = 2 }, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(new[] { "New Name", "ZK Original" }, names);
        Assert.Equal("Local Original", (await dbContext.Workers.AsNoTracking().SingleAsync()).FullName);
    }

    [Fact]
    public async Task Employee_name_rollback_failure_requires_reconciliation_and_does_not_update_planner()
    {
        var worker = new Worker(Guid.NewGuid(), "W-1", "Local Original", "111", attendanceDepartmentId: 1);
        await using var dbContext = ProductModelFixture.CreateContext();
        dbContext.Workers.Add(worker);
        await dbContext.SaveChangesAsync();
        var employees = new Dictionary<string, AttendanceEmployeeRecord?> { ["111"] = new("111", 1, "1001", "ZK Original", true) };
        var calls = 0;
        var writer = new FakeAttendanceEmployeeWriter(
            employees,
            (_, _, _) => Task.FromResult(++calls == 1 ? Result.Success() : Result.Failure(new Error("ExternalFailure", "Rollback failed."))),
            (_, _, _) => Task.FromResult(Result.Failure(new Error("ExternalFailure", "Department failed."))));
        var audit = new RecordingAuditEngine();
        var service = new EmployeeMasterDataService(
            dbContext, writer, new FakeAttendanceEmployeeReader(employees),
            new FakeAttendanceDepartmentReader(new Dictionary<int, AttendanceDepartmentRecord> { [2] = new(2, "Quality") }), audit);

        var result = await service.UpdateMasterIdentityAsync(worker.Id, new UpdateWorkerRequest { FullName = "New Name", AttendanceDepartmentId = 2 }, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("NeedsReconciliation", result.Error!.Code);
        Assert.Equal("Local Original", (await dbContext.Workers.AsNoTracking().SingleAsync()).FullName);
        Assert.Empty(audit.Calls);
    }

    [Fact]
    public async Task Department_persistence_failure_compensates_external_move_after_queuing_the_uncommitted_audit()
    {
        var interceptor = new ThrowingSaveChangesInterceptor();
        await using var dbContext = ProductModelFixture.CreateContext(interceptor);
        var worker = new Worker(Guid.NewGuid(), "W-1", "Worker", "111", attendanceDepartmentId: 1);
        dbContext.Workers.Add(worker);
        await dbContext.SaveChangesAsync();
        interceptor.ThrowOnSave = true;
        var employees = new Dictionary<string, AttendanceEmployeeRecord?> { ["111"] = new("111", 1, "1001", "Worker", true) };
        var writer = new FakeAttendanceEmployeeWriter(employees);
        var audit = new RecordingAuditEngine();
        var service = new DepartmentAdministrationService(
            dbContext,
            new FakeAttendanceDepartmentReader(new Dictionary<int, AttendanceDepartmentRecord> { [2] = new(2, "Quality") }),
            new FakeAttendanceDepartmentWriter(),
            writer,
            new FakeAttendanceEmployeeReader(employees),
            audit);

        var result = await service.MoveWorkerToDepartmentAsync(worker.Id, 2, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("PersistenceFailed", result.Error!.Code);
        Assert.Equal(new[] { 2, 1 }, writer.DepartmentUpdates.Select(x => x.DepartmentId));
        Assert.Single(audit.Calls);
    }

    [Fact]
    public async Task Department_rollback_failure_requires_reconciliation_after_queuing_the_uncommitted_audit()
    {
        var interceptor = new ThrowingSaveChangesInterceptor();
        await using var dbContext = ProductModelFixture.CreateContext(interceptor);
        var worker = new Worker(Guid.NewGuid(), "W-1", "Worker", "111", attendanceDepartmentId: 1);
        dbContext.Workers.Add(worker);
        await dbContext.SaveChangesAsync();
        interceptor.ThrowOnSave = true;
        var employees = new Dictionary<string, AttendanceEmployeeRecord?> { ["111"] = new("111", 1, "1001", "Worker", true) };
        var calls = 0;
        var writer = new FakeAttendanceEmployeeWriter(
            employees,
            null,
            (_, _, _) => Task.FromResult(++calls == 1 ? Result.Success() : Result.Failure(new Error("ExternalFailure", "Rollback failed."))));
        var audit = new RecordingAuditEngine();
        var service = new DepartmentAdministrationService(
            dbContext,
            new FakeAttendanceDepartmentReader(new Dictionary<int, AttendanceDepartmentRecord> { [2] = new(2, "Quality") }),
            new FakeAttendanceDepartmentWriter(),
            writer,
            new FakeAttendanceEmployeeReader(employees),
            audit);

        var result = await service.MoveWorkerToDepartmentAsync(worker.Id, 2, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("NeedsReconciliation", result.Error!.Code);
        Assert.Single(audit.Calls);
    }

    private sealed class TestableManufacturingMigration : AddManufacturingMasterDataFoundation
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);
    }

    private sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public bool ThrowOnSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave) throw new InvalidOperationException("Persistence failed.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ProductModelFixture : IAsyncDisposable
    {
        private ProductModelFixture(AppDbContext dbContext, ProductModel model, SubStage[] subStages)
        {
            DbContext = dbContext;
            Model = model;
            SubStages = subStages;
            Service = new ProductModelService(dbContext, new RecordingAuditEngine());
        }

        public AppDbContext DbContext { get; }
        public ProductModel Model { get; }
        public SubStage[] SubStages { get; }
        public ProductModelService Service { get; }
        public Guid ActorUserId { get; } = Guid.NewGuid();

        public static AppDbContext CreateContext(params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
            if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
            return new AppDbContext(builder.Options);
        }

        public static async Task<ProductModelFixture> CreateAsync()
        {
            var dbContext = CreateContext();
            var model = new ProductModel(Guid.NewGuid(), "MODEL", "Model");
            var mainStageId = Guid.NewGuid();
            var subStages = new[]
            {
                new SubStage(Guid.NewGuid(), mainStageId, "Cut", "CUT", 1, 1),
                new SubStage(Guid.NewGuid(), mainStageId, "Sew", "SEW", 1, 2),
                new SubStage(Guid.NewGuid(), mainStageId, "Pack", "PACK", 1, 3)
            };
            dbContext.ProductModels.Add(model);
            dbContext.SubStages.AddRange(subStages);
            await dbContext.SaveChangesAsync();
            return new ProductModelFixture(dbContext, model, subStages);
        }

        public ProductModelStage AddStage(int order, decimal piecePrice, decimal? standardSeconds, DateTime? effectiveFrom, bool isActive = true)
        {
            var stage = new ProductModelStage(Guid.NewGuid(), Model.Id, SubStages[0].Id, order, piecePrice, standardSeconds, CompensationMode.FixedAmount, isActive: isActive, effectiveFrom: effectiveFrom);
            DbContext.ProductModelStages.Add(stage);
            DbContext.SaveChanges();
            return stage;
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class CompensationFixture : IAsyncDisposable
    {
        private CompensationFixture(AppDbContext dbContext, Worker worker)
        {
            DbContext = dbContext;
            Worker = worker;
            Service = new WorkerCompensationService(dbContext, new RecordingAuditEngine());
        }

        public AppDbContext DbContext { get; }
        public Worker Worker { get; }
        public WorkerCompensationService Service { get; }

        public static async Task<CompensationFixture> CreateAsync(params WorkerSalaryHistory[] ignored)
        {
            var dbContext = ProductModelFixture.CreateContext();
            var worker = new Worker(Guid.NewGuid(), "W-1", "Worker");
            dbContext.Workers.Add(worker);
            await dbContext.SaveChangesAsync();
            return new CompensationFixture(dbContext, worker);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
