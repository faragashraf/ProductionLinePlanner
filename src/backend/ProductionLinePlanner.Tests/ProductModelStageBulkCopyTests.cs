using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Realtime;

namespace ProductionLinePlanner.Tests;

public sealed class ProductModelStageBulkCopyTests
{
    [Fact]
    public async Task Copies_selected_stages_into_target_department_and_preserves_operational_values()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();
        fixture.Db.ProductModelStages.Add(new ProductModelStage(
            Guid.NewGuid(), fixture.TargetModel.Id, fixture.TargetLine.Id, fixture.TargetStages[2].Id, 2, 9m, 90m,
            CompensationMode.FixedAmount));
        await fixture.Db.SaveChangesAsync();
        fixture.ResetObservations();

        var result = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.Request(fixture.SourceRelations[0].Id, fixture.SourceRelations[1].Id),
            fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.RequestedCount);
        Assert.Equal(2, result.Value.AddedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        Assert.Equal([3, 4], result.Value.PlannedStages.Select(stage => stage.StageOrder));

        var sourceCodes = fixture.SourceStages.Select(stage => stage.Code).ToArray();
        var copied = await fixture.Db.ProductModelStages.AsNoTracking()
            .Include(stage => stage.SubStage)
            .Where(stage => stage.ProductModelId == fixture.TargetModel.Id
                && stage.ProductionLineId == fixture.TargetLine.Id
                && stage.SubStage != null
                && stage.SubStage.DepartmentId == fixture.TargetDepartment.Id
                && sourceCodes.Contains(stage.SubStage.Code))
            .OrderBy(stage => stage.StageOrder)
            .ToArrayAsync();
        Assert.Equal(2, copied.Length);
        Assert.Equal(fixture.SourceRelations[0].PiecePrice, copied[0].PiecePrice);
        Assert.Equal(fixture.SourceRelations[0].StandardSeconds, copied[0].StandardSeconds);
        Assert.Equal(fixture.SourceRelations[0].CompensationMode, copied[0].CompensationMode);
        Assert.Equal(fixture.SourceRelations[0].IsRequired, copied[0].IsRequired);
        Assert.Equal(fixture.SourceRelations[0].IsActive, copied[0].IsActive);
        Assert.Equal(fixture.SourceRelations[0].EffectiveFrom, copied[0].EffectiveFrom);
        Assert.NotEqual(fixture.SourceRelations[0].Id, copied[0].Id);
        Assert.Equal(2, fixture.Publisher.Changes.Count(change => change.EntityType == ManufacturingEntityType.ProductModelStage && change.ProductionLineId == fixture.TargetLine.Id));
    }

    [Fact]
    public async Task Preview_and_repeated_execution_skip_existing_relationship_without_duplicates()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();
        var targetMainStageId = await fixture.Db.MainStages
            .Where(stage => stage.DepartmentId == fixture.TargetDepartment.Id)
            .Select(stage => stage.Id)
            .SingleAsync();
        var equivalentTargetStage = new SubStage(
            Guid.NewGuid(), targetMainStageId, fixture.SourceStages[0].Name, fixture.SourceStages[0].Code,
            fixture.SourceStages[0].Capacity, 4, fixture.SourceStages[0].IsActive,
            departmentId: fixture.TargetDepartment.Id);
        fixture.Db.SubStages.Add(equivalentTargetStage);
        fixture.Db.ProductModelStages.Add(new ProductModelStage(
            Guid.NewGuid(), fixture.TargetModel.Id, fixture.TargetLine.Id, equivalentTargetStage.Id, 9, 1m, null,
            CompensationMode.FixedAmount));
        await fixture.Db.SaveChangesAsync();
        fixture.ResetObservations();
        var request = fixture.Request(fixture.SourceRelations[0].Id, fixture.SourceRelations[1].Id);

        var preview = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            request with { PreviewOnly = true },
            fixture.ActorUserId);
        var first = await fixture.Service.CopyModelStagesAsync(fixture.SourceModel.Id, request, fixture.ActorUserId);
        var second = await fixture.Service.CopyModelStagesAsync(fixture.SourceModel.Id, request, fixture.ActorUserId);

        Assert.True(preview.IsSuccess);
        Assert.True(preview.Value!.IsPreview);
        Assert.Equal(1, preview.Value.AddedCount);
        Assert.Equal("AlreadyLinked", Assert.Single(preview.Value.SkippedStages).ReasonCode);
        Assert.Equal(1, first.Value!.AddedCount);
        Assert.Equal(2, second.Value!.SkippedCount);
        Assert.Equal(2, await fixture.Db.ProductModelStages.CountAsync(stage =>
            stage.ProductModelId == fixture.TargetModel.Id &&
            stage.ProductionLineId == fixture.TargetLine.Id &&
            stage.SubStage != null
            && stage.SubStage.DepartmentId == fixture.TargetDepartment.Id
            && new[] { fixture.SourceStages[0].Code, fixture.SourceStages[1].Code }.Contains(stage.SubStage.Code)));
    }

    [Fact]
    public async Task Rejects_same_context_and_unauthenticated_actor()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();
        var sameContext = fixture.Request(fixture.SourceRelations[0].Id) with
        {
            TargetModelId = fixture.SourceModel.Id,
            TargetFactoryId = fixture.Factory.Id,
            TargetDepartmentId = fixture.SourceDepartment.Id,
            TargetProductionLineId = fixture.SourceLine.Id
        };

        var same = await fixture.Service.CopyModelStagesAsync(fixture.SourceModel.Id, sameContext, fixture.ActorUserId);
        var unauthorized = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.Request(fixture.SourceRelations[0].Id),
            Guid.Empty);

        Assert.Equal("ValidationError", same.Error?.Code);
        Assert.Equal("Unauthorized", unauthorized.Error?.Code);
        Assert.Empty(fixture.Audit.Entries);
    }

    [Fact]
    public async Task Accepts_one_selected_stage_and_rejects_only_an_empty_selection()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();

        var oneStage = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.Request(fixture.SourceRelations[0].Id),
            fixture.ActorUserId);
        var empty = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.Request(),
            fixture.ActorUserId);

        Assert.True(oneStage.IsSuccess);
        Assert.Equal(1, oneStage.Value!.RequestedCount);
        Assert.Equal(1, oneStage.Value.AddedCount);
        Assert.Equal("ValidationError", empty.Error?.Code);
    }

    [Fact]
    public async Task Same_model_can_have_independent_assignments_on_lines_in_different_departments()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();

        var result = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.RequestForTarget(fixture.SourceModel.Id, fixture.TargetDepartment.Id, fixture.SourceRelations[0].Id),
            fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.AddedCount);
        Assert.True(await fixture.Db.ProductModelStages.AsNoTracking().AnyAsync(stage =>
            stage.ProductModelId == fixture.SourceModel.Id
            && stage.ProductionLineId == fixture.TargetLine.Id
            && stage.SubStage != null
            && stage.SubStage.DepartmentId == fixture.TargetDepartment.Id));
    }

    [Fact]
    public async Task Different_model_different_line_copies_one_stage_with_new_catalog_identity()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();

        var result = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.RequestForTarget(fixture.TargetModel.Id, fixture.TargetDepartment.Id, fixture.SourceRelations[0].Id),
            fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.AddedCount);
        var plan = Assert.Single(result.Value.PlannedStages);
        Assert.True(plan.CreatesTargetStage);
        Assert.Contains("ستُنشأ", plan.StatusLabel);

        var copiedStage = await fixture.Db.SubStages.AsNoTracking().SingleAsync(stage =>
            stage.DepartmentId == fixture.TargetDepartment.Id && stage.Code == fixture.SourceStages[0].Code);
        Assert.NotEqual(fixture.SourceStages[0].Id, copiedStage.Id);
        Assert.True(await fixture.Db.ProductModelStages.AsNoTracking().AnyAsync(stage =>
            stage.ProductModelId == fixture.TargetModel.Id && stage.ProductionLineId == fixture.TargetLine.Id && stage.SubStageId == copiedStage.Id));
    }

    [Fact]
    public async Task Existing_equivalent_stage_linked_on_target_context_is_skipped_but_source_relationship_alone_is_not()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();

        var sourceOnlyPreview = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.RequestForTarget(fixture.TargetModel.Id, fixture.TargetDepartment.Id, fixture.SourceRelations[0].Id) with { PreviewOnly = true },
            fixture.ActorUserId);
        Assert.Equal(1, sourceOnlyPreview.Value!.AddedCount);
        Assert.Empty(sourceOnlyPreview.Value.SkippedStages);

        var targetMainStageId = await fixture.Db.MainStages
            .Where(stage => stage.DepartmentId == fixture.TargetDepartment.Id)
            .Select(stage => stage.Id)
            .SingleAsync();
        var equivalentTargetStage = new SubStage(
            Guid.NewGuid(), targetMainStageId, fixture.SourceStages[0].Name, fixture.SourceStages[0].Code,
            fixture.SourceStages[0].Capacity, 4, fixture.SourceStages[0].IsActive,
            departmentId: fixture.TargetDepartment.Id);
        fixture.Db.SubStages.Add(equivalentTargetStage);
        fixture.Db.ProductModelStages.Add(new ProductModelStage(
            Guid.NewGuid(), fixture.TargetModel.Id, fixture.TargetLine.Id, equivalentTargetStage.Id, 1, 1m, 10m,
            CompensationMode.FixedAmount));
        await fixture.Db.SaveChangesAsync();
        fixture.ResetObservations();

        var result = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.RequestForTarget(fixture.TargetModel.Id, fixture.TargetDepartment.Id, fixture.SourceRelations[0].Id),
            fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.AddedCount);
        var skipped = Assert.Single(result.Value.SkippedStages);
        Assert.Equal("AlreadyLinked", skipped.ReasonCode);
        Assert.Contains("الموديل وخط الإنتاج الهدف", skipped.Reason);
        Assert.Equal(1, await fixture.Db.SubStages.CountAsync(stage =>
            stage.DepartmentId == fixture.TargetDepartment.Id && stage.Code == fixture.SourceStages[0].Code));
    }

    [Fact]
    public async Task Equivalent_target_catalog_stage_without_relationship_is_reused_and_linked()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();
        var targetMainStageId = await fixture.Db.MainStages
            .Where(stage => stage.DepartmentId == fixture.TargetDepartment.Id)
            .Select(stage => stage.Id)
            .SingleAsync();
        var equivalentTargetStage = new SubStage(
            Guid.NewGuid(), targetMainStageId, fixture.SourceStages[0].Name, fixture.SourceStages[0].Code,
            fixture.SourceStages[0].Capacity, 4, fixture.SourceStages[0].IsActive,
            departmentId: fixture.TargetDepartment.Id);
        fixture.Db.SubStages.Add(equivalentTargetStage);
        await fixture.Db.SaveChangesAsync();
        fixture.ResetObservations();

        var result = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.RequestForTarget(fixture.TargetModel.Id, fixture.TargetDepartment.Id, fixture.SourceRelations[0].Id),
            fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.AddedCount);
        Assert.False(Assert.Single(result.Value.PlannedStages).CreatesTargetStage);
        Assert.True(await fixture.Db.ProductModelStages.AnyAsync(stage =>
            stage.ProductModelId == fixture.TargetModel.Id && stage.ProductionLineId == fixture.TargetLine.Id && stage.SubStageId == equivalentTargetStage.Id));
        Assert.Equal(1, await fixture.Db.SubStages.CountAsync(stage =>
            stage.DepartmentId == fixture.TargetDepartment.Id && stage.Code == fixture.SourceStages[0].Code));
    }

    [Fact]
    public async Task Target_line_code_conflict_is_reported_without_overwrite_or_partial_copy()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();
        var targetMainStageId = await fixture.Db.MainStages
            .Where(stage => stage.DepartmentId == fixture.TargetDepartment.Id)
            .Select(stage => stage.Id)
            .SingleAsync();
        var conflictingStage = new SubStage(
            Guid.NewGuid(), targetMainStageId, "مرحلة مختلفة", fixture.SourceStages[0].Code,
            fixture.SourceStages[0].Capacity, 4, true, departmentId: fixture.TargetDepartment.Id);
        fixture.Db.SubStages.Add(conflictingStage);
        await fixture.Db.SaveChangesAsync();
        fixture.ResetObservations();
        var request = fixture.RequestForTarget(
            fixture.TargetModel.Id,
            fixture.TargetDepartment.Id,
            fixture.SourceRelations[0].Id,
            fixture.SourceRelations[1].Id);

        var preview = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            request with { PreviewOnly = true },
            fixture.ActorUserId);
        var execution = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            request,
            fixture.ActorUserId);

        Assert.True(preview.IsSuccess);
        Assert.Equal(1, preview.Value!.AddedCount);
        var failure = Assert.Single(preview.Value.FailedStages);
        Assert.Equal("TargetStageCodeConflict", failure.ReasonCode);
        Assert.Contains(fixture.SourceStages[0].Code, failure.Reason);
        Assert.Equal(1, execution.Value!.FailedCount);
        Assert.Equal(0, execution.Value.AddedCount);
        Assert.False(await fixture.Db.SubStages.AnyAsync(stage =>
            stage.DepartmentId == fixture.TargetDepartment.Id && stage.Code == fixture.SourceStages[1].Code));
        Assert.Empty(await fixture.Db.ProductModelStages.Where(stage => stage.ProductModelId == fixture.TargetModel.Id).ToArrayAsync());
        Assert.Equal("مرحلة مختلفة", (await fixture.Db.SubStages.SingleAsync(stage => stage.Id == conflictingStage.Id)).Name);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.Publisher.Changes);
    }

    [Fact]
    public async Task Unexpected_save_failure_rolls_back_all_stages_and_publishes_no_realtime_event()
    {
        var failure = new ThrowOnBulkStageSaveInterceptor();
        await using var fixture = await BulkCopyFixture.CreateAsync(failure);
        fixture.ResetObservations();
        failure.Enabled = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            fixture.RequestForTarget(fixture.TargetModel.Id, fixture.TargetDepartment.Id, fixture.SourceRelations[0].Id, fixture.SourceRelations[1].Id),
            fixture.ActorUserId));

        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.ProductModelStages.AsNoTracking()
            .Where(stage => stage.ProductModelId == fixture.TargetModel.Id)
            .ToArrayAsync());
        Assert.False(await fixture.Db.SubStages.AsNoTracking().AnyAsync(stage =>
            stage.DepartmentId == fixture.TargetDepartment.Id
            && new[] { fixture.SourceStages[0].Code, fixture.SourceStages[1].Code }.Contains(stage.Code)));
        Assert.Empty(fixture.Publisher.Changes);
    }

    [Fact]
    public async Task Successful_copy_records_one_aggregate_audit_after_preview_does_not()
    {
        await using var fixture = await BulkCopyFixture.CreateAsync();
        var request = fixture.Request(fixture.SourceRelations[0].Id);

        await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            request with { PreviewOnly = true },
            fixture.ActorUserId);
        var result = await fixture.Service.CopyModelStagesAsync(
            fixture.SourceModel.Id,
            request,
            fixture.ActorUserId,
            "POST /api/product-models/source/stages/copy");

        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(fixture.ActorUserId, audit.ActorUserId);
        Assert.Equal(AuditActionType.Create, audit.ActionType);
        Assert.Equal(nameof(ProductModelStage), audit.EntityType);
        Assert.Equal(fixture.TargetModel.Id.ToString(), audit.EntityId);
        Assert.Equal("POST /api/product-models/source/stages/copy", audit.RequestMeta);
        Assert.Equal(1, result.Value!.AddedCount);
    }

    private sealed class BulkCopyFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private BulkCopyFixture(
            SqliteConnection connection,
            AppDbContext db,
            RecordingAuditEngine audit,
            RecordingPublisher publisher,
            Factory factory,
            Department sourceDepartment,
            Department targetDepartment,
            ProductionLine sourceLine,
            ProductionLine targetLine,
            ProductModel sourceModel,
            ProductModel targetModel,
            SubStage[] sourceStages,
            SubStage[] targetStages,
            ProductModelStage[] sourceRelations)
        {
            this.connection = connection;
            Db = db;
            Audit = audit;
            Publisher = publisher;
            Factory = factory;
            SourceDepartment = sourceDepartment;
            TargetDepartment = targetDepartment;
            SourceLine = sourceLine;
            TargetLine = targetLine;
            SourceModel = sourceModel;
            TargetModel = targetModel;
            SourceStages = sourceStages;
            TargetStages = targetStages;
            SourceRelations = sourceRelations;
            Service = new ProductModelService(db, audit);
        }

        public Guid ActorUserId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; }
        public RecordingAuditEngine Audit { get; }
        public RecordingPublisher Publisher { get; }
        public ProductModelService Service { get; }
        public Factory Factory { get; }
        public Department SourceDepartment { get; }
        public Department TargetDepartment { get; }
        public ProductionLine SourceLine { get; }
        public ProductionLine TargetLine { get; }
        public ProductModel SourceModel { get; }
        public ProductModel TargetModel { get; }
        public SubStage[] SourceStages { get; }
        public SubStage[] TargetStages { get; }
        public ProductModelStage[] SourceRelations { get; }

        public static async Task<BulkCopyFixture> CreateAsync(params IInterceptor[] additionalInterceptors)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateCollation(
                "SQL_Latin1_General_CP1_CI_AS",
                (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
            var publisher = new RecordingPublisher();
            var coordinator = new ManufacturingDataChangeTransactionCoordinator(
                publisher,
                NullLogger<ManufacturingDataChangeTransactionCoordinator>.Instance);
            var interceptors = new IInterceptor[]
            {
                new ManufacturingDataChangeSaveChangesInterceptor(
                    publisher,
                    new CurrentUserStub(Guid.NewGuid()),
                    new CorrelationStub(),
                    coordinator,
                    NullLogger<ManufacturingDataChangeSaveChangesInterceptor>.Instance),
                new ManufacturingDataChangeTransactionInterceptor(coordinator)
            }.Concat(additionalInterceptors).ToArray();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptors)
                .Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var factory = new Factory(Guid.NewGuid(), "Factory", "F-1");
            var sourceDepartment = new Department(Guid.NewGuid(), factory.Id, "D-1", "قسم المصدر", null, 1);
            var targetDepartment = new Department(Guid.NewGuid(), factory.Id, "D-2", "قسم الهدف", null, 2);
            var sourceLine = new ProductionLine(Guid.NewGuid(), factory.Id, "Source", 1, "L-1", departmentId: sourceDepartment.Id);
            var targetLine = new ProductionLine(Guid.NewGuid(), factory.Id, "Target", 2, "L-2", departmentId: targetDepartment.Id);
            var sourceMain = new MainStage(Guid.NewGuid(), sourceDepartment.Id, "Source main", 1);
            var targetMain = new MainStage(Guid.NewGuid(), targetDepartment.Id, "Target main", 1);
            var sourceStages = new[]
            {
                new SubStage(Guid.NewGuid(), sourceMain.Id, "Source one", "SRC-1", 10, 1, departmentId: sourceDepartment.Id),
                new SubStage(Guid.NewGuid(), sourceMain.Id, "Source two", "SRC-2", 10, 2, departmentId: sourceDepartment.Id)
            };
            var targetStages = new[]
            {
                new SubStage(Guid.NewGuid(), targetMain.Id, "Target one", "TGT-1", 10, 1, departmentId: targetDepartment.Id),
                new SubStage(Guid.NewGuid(), targetMain.Id, "Target two", "TGT-2", 10, 2, departmentId: targetDepartment.Id),
                new SubStage(Guid.NewGuid(), targetMain.Id, "Existing", "TGT-3", 10, 3, departmentId: targetDepartment.Id)
            };
            var sourceModel = new ProductModel(Guid.NewGuid(), "SOURCE", "Source model");
            var targetModel = new ProductModel(Guid.NewGuid(), "TARGET", "Target model");
            var effectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var sourceRelations = new[]
            {
                new ProductModelStage(Guid.NewGuid(), sourceModel.Id, sourceLine.Id, sourceStages[0].Id, 1, 1.25m, 12.5m, CompensationMode.SharedPercentage, true, true, effectiveFrom),
                new ProductModelStage(Guid.NewGuid(), sourceModel.Id, sourceLine.Id, sourceStages[1].Id, 2, 2.50m, 25m, CompensationMode.FullRatePerWorker, false, false)
            };
            db.AddRange(factory, sourceDepartment, targetDepartment, sourceLine, targetLine, sourceMain, targetMain, sourceModel, targetModel);
            db.SubStages.AddRange(sourceStages.Concat(targetStages));
            db.ProductModelStages.AddRange(sourceRelations);
            await db.SaveChangesAsync();
            publisher.Changes.Clear();
            var audit = new RecordingAuditEngine();
            return new BulkCopyFixture(connection, db, audit, publisher, factory, sourceDepartment, targetDepartment, sourceLine, targetLine, sourceModel, targetModel, sourceStages, targetStages, sourceRelations);
        }

        public CopyProductModelStagesRequest Request(params Guid[] sourceProductModelStageIds) =>
            RequestForDepartment(TargetDepartment.Id, sourceProductModelStageIds);

        public CopyProductModelStagesRequest RequestForDepartment(Guid targetDepartmentId, params Guid[] sourceProductModelStageIds) =>
            RequestForTarget(TargetModel.Id, targetDepartmentId, sourceProductModelStageIds);

        public CopyProductModelStagesRequest RequestForTarget(Guid targetModelId, Guid targetDepartmentId, params Guid[] sourceProductModelStageIds) => new()
        {
            SourceFactoryId = Factory.Id,
            SourceDepartmentId = SourceDepartment.Id,
            SourceProductionLineId = SourceLine.Id,
            TargetModelId = targetModelId,
            TargetFactoryId = Factory.Id,
            TargetDepartmentId = targetDepartmentId,
            TargetProductionLineId = targetDepartmentId == SourceDepartment.Id ? SourceLine.Id : TargetLine.Id,
            SourceProductModelStageIds = sourceProductModelStageIds
        };

        public void ResetObservations()
        {
            Publisher.Changes.Clear();
            Audit.Entries.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class RecordingAuditEngine : IAuditEngine
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task<Result> RecordAsync(
            Guid actorUserId,
            AuditActionType actionType,
            string entityType,
            string entityId,
            object? before = null,
            object? after = null,
            string? requestMeta = null,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(new AuditEntry(actorUserId, actionType, entityType, entityId, after, requestMeta));
            return Task.FromResult(Result.Success());
        }
    }

    private sealed record AuditEntry(
        Guid ActorUserId,
        AuditActionType ActionType,
        string EntityType,
        string EntityId,
        object? After,
        string? RequestMeta);

    private sealed class RecordingPublisher : IManufacturingDataChangePublisher
    {
        public List<ManufacturingDataChanged> Changes { get; } = [];
        public Task PublishAsync(ManufacturingDataChanged change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }

    private sealed class CurrentUserStub(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? UserName => null;
        public bool IsAuthenticated => true;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class CorrelationStub : IManufacturingRealtimeCorrelationContext
    {
        public string? CorrelationId => null;
    }

    private sealed class ThrowOnBulkStageSaveInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ThrowWhenEnabled(eventData.Context);

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowWhenEnabled(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ThrowWhenEnabled(DbContext? context)
        {
            if (Enabled && context?.ChangeTracker.Entries<ProductModelStage>()
                .Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new InvalidOperationException("Simulated persistence failure.");
            }
        }
    }
}
