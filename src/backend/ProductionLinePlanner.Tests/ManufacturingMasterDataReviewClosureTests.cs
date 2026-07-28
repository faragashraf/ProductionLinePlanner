using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
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
    public async Task Model_list_searches_only_model_name_and_code()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        var codeOnlyModel = new ProductModel(Guid.NewGuid(), "CODE-ONLY", "اسم لا يطابق الكود");
        fixture.DbContext.ProductModelStages.Add(
            new ProductModelStage(Guid.NewGuid(), fixture.Model.Id, fixture.Line.Id, fixture.SubStages[0].Id, 1, 1m, null, CompensationMode.FixedAmount));
        fixture.DbContext.ProductModels.Add(codeOnlyModel);
        await fixture.DbContext.SaveChangesAsync();

        var byName = await fixture.Service.GetModelsAsync("model", isActive: null);
        var byCode = await fixture.Service.GetModelsAsync("code-only", isActive: null);
        var byStageName = await fixture.Service.GetModelsAsync("cut", isActive: null);

        Assert.Equal(fixture.Model.Id, Assert.Single(byName.Value!).Id);
        Assert.Equal(codeOnlyModel.Id, Assert.Single(byCode.Value!).Id);
        Assert.Empty(byStageName.Value!);
        Assert.DoesNotContain(typeof(ProductModelDto).GetProperties(), property => property.Name == "Stages");
    }

    [Fact]
    public async Task Model_search_filters_before_count_and_paging()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        var lateMatch = new ProductModel(Guid.NewGuid(), "MODEL-055", "Late matching model");
        var inactiveMatch = new ProductModel(Guid.NewGuid(), "MODEL-056", "Inactive matching model", isActive: false);
        fixture.DbContext.ProductModels.AddRange(Enumerable.Range(1, 54).Select(index => new ProductModel(Guid.NewGuid(), $"MODEL-{index:D3}", $"Model {index:D3}")));
        fixture.DbContext.ProductModels.AddRange(lateMatch, inactiveMatch);
        await fixture.DbContext.SaveChangesAsync();

        var sixthPage = await fixture.Service.GetModelsAsync(null, isActive: null, page: 6, pageSize: 10);
        var byModel = await fixture.Service.GetModelsAsync("  055  ", isActive: true, page: 1, pageSize: 10);
        var includingInactive = await fixture.Service.GetModelsAsync("matching model", isActive: null, page: 1, pageSize: 10);

        Assert.True(sixthPage.IsSuccess);
        Assert.Contains(sixthPage.Value!, model => model.Id == lateMatch.Id);
        Assert.True(byModel.IsSuccess);
        Assert.Equal(1, byModel.TotalCount);
        Assert.Equal(lateMatch.Id, Assert.Single(byModel.Value!).Id);
        Assert.True(includingInactive.IsSuccess);
        Assert.Equal(2, includingInactive.TotalCount);
    }

    [Fact]
    public async Task Updating_a_model_returns_the_general_model_dto()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        var updated = await fixture.Service.UpdateModelAsync(
            fixture.Model.Id,
            new UpdateProductModelRequest { Name = "Updated model" },
            fixture.ActorUserId);

        Assert.True(updated.IsSuccess);
        Assert.Equal(fixture.Model.Id, updated.Value!.Id);
        Assert.Equal("Updated model", updated.Value.Name);
        Assert.DoesNotContain(typeof(ProductModelDto).GetProperties(), property => property.Name == "Stages");
    }

    [Fact]
    public async Task Product_model_registration_and_updates_have_no_production_line_contract()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();

        var created = await fixture.Service.CreateModelAsync(
            new CreateProductModelRequest
            {
                Code = "MODEL-WITHOUT-LINE",
                Name = "Line-independent model",
                Description = "Created without selecting a production line."
            },
            fixture.ActorUserId);
        var updated = await fixture.Service.UpdateModelAsync(
            created.Value!.Id,
            new UpdateProductModelRequest { Name = "Updated line-independent model" },
            fixture.ActorUserId);

        Assert.True(created.IsSuccess);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Updated line-independent model", updated.Value!.Name);
        Assert.DoesNotContain(typeof(ProductModel).GetProperties(), property => property.Name == "ProductionLineId");
        Assert.DoesNotContain(typeof(CreateProductModelRequest).GetProperties(), property => property.Name == "ProductionLineId");
        Assert.DoesNotContain(typeof(UpdateProductModelRequest).GetProperties(), property => property.Name == "ProductionLineId");
        Assert.DoesNotContain(typeof(ProductModelDto).GetProperties(), property => property.Name == "ProductionLineId");
    }

    [Fact]
    public async Task Deleting_an_unlinked_model_soft_deactivates_it_without_removing_the_row()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();

        var result = await fixture.Service.DeleteModelAsync(fixture.Model.Id, fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var persisted = await fixture.DbContext.ProductModels.AsNoTracking().SingleAsync(x => x.Id == fixture.Model.Id);
        Assert.False(persisted.IsActive);
    }

    [Fact]
    public async Task Deleting_a_model_with_stages_or_production_history_is_blocked_by_the_service()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        fixture.AddStage(1, 1m, null, null);
        fixture.DbContext.ProductionOrders.Add(new ProductionOrder(
            Guid.NewGuid(), "MODEL-DELETE-BLOCKER", fixture.Model.Id, null,
            DateOnly.FromDateTime(DateTime.UtcNow), 1m, null, fixture.ActorUserId, DateTime.UtcNow));
        await fixture.DbContext.SaveChangesAsync();

        var eligibility = await fixture.Service.GetModelDeleteEligibilityAsync(fixture.Model.Id);
        var result = await fixture.Service.DeleteModelAsync(fixture.Model.Id, fixture.ActorUserId);

        Assert.True(eligibility.IsSuccess);
        Assert.False(eligibility.Value!.CanDelete);
        Assert.Contains("تشغيل إنتاج", eligibility.Value.MessageAr, StringComparison.Ordinal);
        Assert.True(result.IsFailure);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("مرحلة موديل", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("تشغيل إنتاج", result.Error.Message, StringComparison.Ordinal);
        Assert.True((await fixture.DbContext.ProductModels.AsNoTracking().SingleAsync(x => x.Id == fixture.Model.Id)).IsActive);
    }

    [Fact]
    public async Task Updating_a_model_rejects_code_mutation_without_persisting_any_other_change()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        var originalName = fixture.Model.Name;

        var result = await fixture.Service.UpdateModelAsync(
            fixture.Model.Id,
            new UpdateProductModelRequest { Code = "MUTATED", Name = "Must not persist" },
            fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("ValidationError", result.Error!.Code);
        var persisted = await fixture.DbContext.ProductModels.AsNoTracking().SingleAsync(x => x.Id == fixture.Model.Id);
        Assert.Equal("MODEL", persisted.Code);
        Assert.Equal(originalName, persisted.Name);
    }

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
            fixture.Line.Id,
            stage.Id,
            new UpsertProductModelStageRequest { PiecePrice = 8m },
            fixture.ActorUserId);

        Assert.True(updated.IsSuccess);
        Assert.Equal(30m, updated.Value!.StandardSeconds);
        Assert.Equal(originalEffectiveFrom, updated.Value.EffectiveFrom);

        var cleared = await fixture.Service.UpdateModelStageAsync(
            fixture.Model.Id,
            fixture.Line.Id,
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
            fixture.Line.Id,
            stage.Id,
            new UpsertProductModelStageRequest { StandardSeconds = 0m, HasStandardSeconds = true },
            fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("ValidationError", result.Error!.Code);
    }

    [Fact]
    public async Task Patch_stage_activation_updates_only_the_requested_model_relationship_and_supports_reactivation()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        var firstRelationship = fixture.AddStage(1, 5m, null, null);
        var otherModel = new ProductModel(Guid.NewGuid(), "OTHER", "Other model");
        var otherRelationship = new ProductModelStage(
            Guid.NewGuid(),
            otherModel.Id,
            fixture.Line.Id,
            fixture.SubStages[0].Id,
            1,
            5m,
            null,
            CompensationMode.FixedAmount);
        fixture.DbContext.ProductModels.Add(otherModel);
        fixture.DbContext.ProductModelStages.Add(otherRelationship);
        await fixture.DbContext.SaveChangesAsync();

        var deactivated = await fixture.Service.UpdateModelStageAsync(
            fixture.Model.Id,
            fixture.Line.Id,
            firstRelationship.Id,
            new UpsertProductModelStageRequest { IsActive = false },
            fixture.ActorUserId);

        Assert.True(deactivated.IsSuccess);
        Assert.False(deactivated.Value!.IsActive);
        fixture.DbContext.ChangeTracker.Clear();
        Assert.False((await fixture.DbContext.ProductModelStages.SingleAsync(x => x.Id == firstRelationship.Id)).IsActive);
        Assert.True((await fixture.DbContext.ProductModelStages.SingleAsync(x => x.Id == otherRelationship.Id)).IsActive);

        var reactivated = await fixture.Service.UpdateModelStageAsync(
            fixture.Model.Id,
            fixture.Line.Id,
            firstRelationship.Id,
            new UpsertProductModelStageRequest { IsActive = true },
            fixture.ActorUserId);

        Assert.True(reactivated.IsSuccess);
        Assert.True(reactivated.Value!.IsActive);
        fixture.DbContext.ChangeTracker.Clear();
        Assert.True((await fixture.DbContext.ProductModelStages.SingleAsync(x => x.Id == firstRelationship.Id)).IsActive);
        Assert.True((await fixture.DbContext.ProductModelStages.SingleAsync(x => x.Id == otherRelationship.Id)).IsActive);
    }

    [Fact]
    public async Task Patch_stage_activation_reports_not_found_for_a_relationship_outside_the_selected_model()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        var otherModel = new ProductModel(Guid.NewGuid(), "OTHER", "Other model");
        var otherRelationship = new ProductModelStage(
            Guid.NewGuid(),
            otherModel.Id,
            fixture.Line.Id,
            fixture.SubStages[0].Id,
            1,
            5m,
            null,
            CompensationMode.FixedAmount);
        fixture.DbContext.ProductModels.Add(otherModel);
        fixture.DbContext.ProductModelStages.Add(otherRelationship);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.UpdateModelStageAsync(
            fixture.Model.Id,
            fixture.Line.Id,
            otherRelationship.Id,
            new UpsertProductModelStageRequest { IsActive = false },
            fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
        fixture.DbContext.ChangeTracker.Clear();
        Assert.True((await fixture.DbContext.ProductModelStages.SingleAsync(x => x.Id == otherRelationship.Id)).IsActive);
    }

    [Fact]
    public async Task Stage_order_conflicts_include_inactive_configurations_and_copy_is_prevalidated()
    {
        await using var fixture = await ProductModelFixture.CreateAsync();
        fixture.AddStage(2, 1m, null, null, isActive: false);

        var add = await fixture.Service.AddModelStageAsync(
            fixture.Model.Id,
            fixture.Line.Id,
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
    public async Task Employee_local_name_update_has_no_external_compensation_path()
    {
        var worker = new Worker(Guid.NewGuid(), "W-1", "Local Original", "111", attendanceDepartmentId: 1);
        await using var dbContext = ProductModelFixture.CreateContext();
        dbContext.Workers.Add(worker);
        await dbContext.SaveChangesAsync();
        var service = new EmployeeMasterDataService(dbContext, new RecordingAuditEngine());

        var result = await service.UpdateMasterIdentityAsync(worker.Id, new UpdateWorkerRequest { FullName = "New Local Name" }, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal("New Local Name", (await dbContext.Workers.AsNoTracking().SingleAsync()).FullName);
        Assert.Equal(1, (await dbContext.Workers.AsNoTracking().SingleAsync()).AttendanceDepartmentId);
    }

    [Fact]
    public async Task Source_observed_department_and_external_department_mutations_are_blocked()
    {
        var worker = new Worker(Guid.NewGuid(), "W-1", "Local Original", "111", attendanceDepartmentId: 1);
        await using var dbContext = ProductModelFixture.CreateContext();
        dbContext.Workers.Add(worker);
        await dbContext.SaveChangesAsync();
        var departments = new FakeAttendanceDepartmentReader(new Dictionary<int, AttendanceDepartmentRecord> { [2] = new(2, "Quality") });
        var employeeService = new EmployeeMasterDataService(dbContext, new RecordingAuditEngine());
        var departmentService = new DepartmentAdministrationService(dbContext, departments);

        var identityUpdate = await employeeService.UpdateMasterIdentityAsync(worker.Id, new UpdateWorkerRequest { AttendanceDepartmentId = 2 }, Guid.NewGuid());
        var departmentMove = await departmentService.MoveWorkerToDepartmentAsync(worker.Id, 2, Guid.NewGuid());

        Assert.Equal("SourceObservedOnly", identityUpdate.Error!.Code);
        Assert.Equal("ExternalSourceReadOnly", departmentMove.Error!.Code);
        Assert.Equal("Local Original", (await dbContext.Workers.AsNoTracking().SingleAsync()).FullName);
        Assert.Equal(1, (await dbContext.Workers.AsNoTracking().SingleAsync()).AttendanceDepartmentId);
    }

    private sealed class TestableManufacturingMigration : AddManufacturingMasterDataFoundation
    {
        public void BuildUp(MigrationBuilder builder) => Up(builder);
    }

    private sealed class ProductModelFixture : IAsyncDisposable
    {
        private ProductModelFixture(AppDbContext dbContext, ProductModel model, ProductionLine line, SubStage[] subStages)
        {
            DbContext = dbContext;
            Model = model;
            Line = line;
            SubStages = subStages;
            Service = new ProductModelService(dbContext, new RecordingAuditEngine());
        }

        public AppDbContext DbContext { get; }
        public ProductModel Model { get; }
        public ProductionLine Line { get; }
        public SubStage[] SubStages { get; }
        public ProductModelService Service { get; }
        public Guid ActorUserId { get; } = Guid.NewGuid();

        public static AppDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);

        public static async Task<ProductModelFixture> CreateAsync()
        {
            var dbContext = CreateContext();
            var model = new ProductModel(Guid.NewGuid(), "MODEL", "Model");
            var factory = new Factory(Guid.NewGuid(), "Factory", "FAC");
            var department = new Department(Guid.NewGuid(), factory.Id, "OPS", "التشغيل", "Operations", 1);
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1, departmentId: department.Id);
            var mainStage = new MainStage(Guid.NewGuid(), department.Id, "Main", 1);
            var subStages = new[]
            {
                new SubStage(Guid.NewGuid(), mainStage.Id, "Cut", "CUT", 1, 1, departmentId: department.Id),
                new SubStage(Guid.NewGuid(), mainStage.Id, "Sew", "SEW", 1, 2, departmentId: department.Id),
                new SubStage(Guid.NewGuid(), mainStage.Id, "Pack", "PACK", 1, 3, departmentId: department.Id)
            };
            dbContext.AddRange(factory, department, line, mainStage, model);
            dbContext.SubStages.AddRange(subStages);
            await dbContext.SaveChangesAsync();
            return new ProductModelFixture(dbContext, model, line, subStages);
        }

        public ProductModelStage AddStage(int order, decimal piecePrice, decimal? standardSeconds, DateTime? effectiveFrom, bool isActive = true)
        {
            var stage = new ProductModelStage(Guid.NewGuid(), Model.Id, Line.Id, SubStages[0].Id, order, piecePrice, standardSeconds, CompensationMode.FixedAmount, isActive: isActive, effectiveFrom: effectiveFrom);
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
