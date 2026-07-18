using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Reports.Quantities;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class ProductionQuantitiesReportServiceTests
{
    [Fact]
    public async Task Summary_uses_stage_record_grain_and_worker_views_keep_distinct_workers_and_sources()
    {
        await using var fixture = await QuantitiesFixture.CreateAsync();
        await fixture.AddRecordAsync(fixture.Primary, fixture.Today, StageProductionRecordStatus.Approved, 500m, 450m, 50m, [fixture.WorkerA, fixture.WorkerB, fixture.WorkerC]);
        var secondary = await fixture.CreateScopeAsync("SECONDARY");
        await fixture.AddRecordAsync(secondary, fixture.Today, StageProductionRecordStatus.Approved, 100m, 100m, 0m, [fixture.WorkerA]);
        var service = new ProductionQuantitiesReportService(fixture.Db);

        var details = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today,
            To = fixture.Today,
            View = QuantitiesReportView.Details
        });

        Assert.True(details.IsSuccess);
        Assert.Equal(600m, details.Value!.Summary.TotalStageProducedQuantity);
        Assert.Equal(550m, details.Value.Summary.TotalAcceptedQuantity);
        Assert.Equal(50m, details.Value.Summary.TotalRejectedQuantity);
        Assert.Equal(2, details.Value.Summary.RecordCount);
        Assert.Equal(2, details.Value.Summary.StageCount);
        Assert.Equal(3, details.Value.Summary.WorkerCount);

        var stageWorkers = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today,
            To = fixture.Today,
            View = QuantitiesReportView.StageWorkers
        });

        Assert.True(stageWorkers.IsSuccess);
        Assert.Equal(4, stageWorkers.Value!.Rows.Count);
        Assert.Equal(600m, stageWorkers.Value.Summary.TotalStageProducedQuantity);
        Assert.All(stageWorkers.Value.Rows, row =>
        {
            Assert.NotNull(row.Source.StageProductionRecordId);
            Assert.NotNull(row.Source.StageProductionWorkerAllocationId);
            Assert.NotNull(row.Source.WorkerId);
        });

        var workers = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today,
            To = fixture.Today,
            View = QuantitiesReportView.ByWorker
        });

        Assert.True(workers.IsSuccess);
        Assert.Equal(3, workers.Value!.Rows.Count);
        var workerA = Assert.Single(workers.Value.Rows, row => row.Source.WorkerId == fixture.WorkerA.Id);
        Assert.Equal(2, workerA.RecordCount);
        Assert.Equal(2, workerA.StageCount);
    }

    [Fact]
    public async Task Filters_status_pagination_and_sorting_are_bounded_and_empty_periods_are_safe()
    {
        await using var fixture = await QuantitiesFixture.CreateAsync();
        await fixture.AddRecordAsync(fixture.Primary, fixture.Today, StageProductionRecordStatus.Approved, 500m, 500m, 0m, [fixture.WorkerA]);
        await fixture.AddRecordAsync(fixture.Primary, fixture.Today, StageProductionRecordStatus.Draft, 50m, 50m, 0m, [fixture.WorkerB]);
        await fixture.AddRecordAsync(fixture.Primary, fixture.Today, StageProductionRecordStatus.Cancelled, 25m, 25m, 0m, [fixture.WorkerC]);
        var secondary = await fixture.CreateScopeAsync("SECONDARY");
        await fixture.AddRecordAsync(secondary, fixture.Today.AddDays(1), StageProductionRecordStatus.Approved, 100m, 100m, 0m, [fixture.WorkerA]);
        var service = new ProductionQuantitiesReportService(fixture.Db);

        var defaultStatus = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today,
            To = fixture.Today.AddDays(1),
            View = QuantitiesReportView.Details,
            SortBy = QuantitiesReportSortBy.ProducedQuantity,
            Page = 1,
            PageSize = 1
        });
        Assert.True(defaultStatus.IsSuccess);
        Assert.Equal("Approved", defaultStatus.Value!.AppliedStatus);
        Assert.Equal(2, defaultStatus.Value.TotalCount);
        Assert.Equal(100m, defaultStatus.Value.Rows.Single().ProducedQuantity);

        var draft = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today,
            To = fixture.Today,
            Status = StageProductionRecordStatus.Draft,
            View = QuantitiesReportView.Details
        });
        Assert.True(draft.IsSuccess);
        Assert.Single(draft.Value!.Rows);
        Assert.Equal("Draft", draft.Value.Rows.Single().Status);

        var cancelled = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today,
            To = fixture.Today,
            Status = StageProductionRecordStatus.Cancelled,
            View = QuantitiesReportView.Details
        });
        Assert.True(cancelled.IsSuccess);
        Assert.Single(cancelled.Value!.Rows);
        Assert.Equal("Cancelled", cancelled.Value.Rows.Single().Status);

        var primaryOnly = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today,
            To = fixture.Today.AddDays(1),
            FactoryId = fixture.Primary.FactoryId,
            ProductionLineId = fixture.Primary.ProductionLineId,
            ProductModelId = fixture.Primary.ProductModelId,
            ProductModelStageId = fixture.Primary.ProductModelStageId,
            WorkerId = fixture.WorkerA.Id,
            View = QuantitiesReportView.Details
        });
        Assert.True(primaryOnly.IsSuccess);
        Assert.Single(primaryOnly.Value!.Rows);
        Assert.Equal(500m, primaryOnly.Value.Rows.Single().ProducedQuantity);

        var empty = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today.AddDays(10),
            To = fixture.Today.AddDays(10),
            View = QuantitiesReportView.Details
        });
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Value!.Rows);
        Assert.Equal(0, empty.Value.Summary.RecordCount);

        var oversizedPage = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today,
            To = fixture.Today,
            View = QuantitiesReportView.Details,
            Page = int.MaxValue,
            PageSize = 200
        });
        Assert.True(oversizedPage.IsSuccess);
        Assert.Empty(oversizedPage.Value!.Rows);

        var rejectedSort = await service.QueryAsync(new QuantitiesReportFilterRequest
        {
            From = fixture.Today,
            To = fixture.Today,
            View = QuantitiesReportView.Details,
            SortBy = QuantitiesReportSortBy.WorkerCode
        });
        Assert.True(rejectedSort.IsFailure);
        Assert.Equal("ValidationError", rejectedSort.Error!.Code);
    }

    [Fact]
    public void Quantities_contract_does_not_expose_financial_or_salary_properties()
    {
        var forbidden = new[] { "Salary", "Price", "Cost", "Earning", "Entitlement", "Currency", "Compensation", "FixedAmount" };
        var types = new[]
        {
            typeof(QuantitiesReportSummaryDto),
            typeof(QuantitiesReportRowDto),
            typeof(QuantitiesReportResultDto),
            typeof(ReportSourceReferenceDto)
        };

        Assert.DoesNotContain(types.SelectMany(type => type.GetProperties()), property =>
            forbidden.Any(fragment => property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class QuantitiesFixture : IAsyncDisposable
    {
        private QuantitiesFixture(SqliteConnection connection, AppDbContext db, Guid actorId, Worker workerA, Worker workerB, Worker workerC)
        {
            Connection = connection;
            Db = db;
            ActorId = actorId;
            WorkerA = workerA;
            WorkerB = workerB;
            WorkerC = workerC;
        }

        private SqliteConnection Connection { get; }
        public AppDbContext Db { get; }
        public Guid ActorId { get; }
        public Worker WorkerA { get; }
        public Worker WorkerB { get; }
        public Worker WorkerC { get; }
        public DateOnly Today { get; } = new(2026, 7, 13);
        public ReportScope Primary { get; private set; } = null!;
        private readonly Dictionary<(Guid ProductionLineId, Guid ProductModelId, DateOnly ProductionDate), ProductionOrder> _orders = [];
        private int _additionalStageSequence;

        public static async Task<QuantitiesFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var actorId = Guid.NewGuid();
            var workerA = new Worker(Guid.NewGuid(), "A", "Worker A");
            var workerB = new Worker(Guid.NewGuid(), "B", "Worker B");
            var workerC = new Worker(Guid.NewGuid(), "C", "Worker C");
            db.AddRange(new AppUser(actorId, "Reports Test User", "reports-test@example.test", "hash"), workerA, workerB, workerC);
            await db.SaveChangesAsync();
            var fixture = new QuantitiesFixture(connection, db, actorId, workerA, workerB, workerC);
            fixture.Primary = await fixture.CreateScopeAsync("PRIMARY");
            return fixture;
        }

        public async Task<ReportScope> CreateScopeAsync(string suffix)
        {
            var factory = new Factory(Guid.NewGuid(), $"Factory {suffix}", $"F-{suffix}");
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, $"Line {suffix}", 1, $"L-{suffix}");
            var mainStage = new MainStage(Guid.NewGuid(), line.Id, $"Main {suffix}", 1);
            var subStage = new SubStage(Guid.NewGuid(), mainStage.Id, $"Stage {suffix}", $"S-{suffix}", 1, 1);
            var model = new ProductModel(Guid.NewGuid(), $"M-{suffix}", $"Model {suffix}");
            var productModelStage = new ProductModelStage(Guid.NewGuid(), model.Id, subStage.Id, 1, 0m, null, CompensationMode.SharedPercentage);
            Db.AddRange(factory, line, mainStage, subStage, model, productModelStage);
            await Db.SaveChangesAsync();
            return new ReportScope(factory, line, mainStage, subStage, model, productModelStage);
        }

        public async Task AddRecordAsync(
            ReportScope scope,
            DateOnly productionDate,
            StageProductionRecordStatus status,
            decimal produced,
            decimal accepted,
            decimal rejected,
            IReadOnlyCollection<Worker> workers)
        {
            var orderKey = (scope.ProductionLineId, scope.ProductModelId, productionDate);
            if (!_orders.TryGetValue(orderKey, out var order))
            {
                order = new ProductionOrder(
                    Guid.NewGuid(),
                    $"ORD-{Guid.NewGuid():N}",
                    scope.ProductModelId,
                    scope.ProductionLineId,
                    productionDate,
                    produced,
                    null,
                    ActorId,
                    DateTime.UtcNow);
                _orders.Add(orderKey, order);
                Db.Add(order);
            }

            var productModelStageId = scope.ProductModelStageId;
            var stageCode = scope.SubStage.Code;
            var stageName = scope.SubStage.Name;
            if (await Db.Set<StageProductionRecord>().AnyAsync(existing =>
                    existing.ProductionOrderId == order.Id && existing.ProductModelStageId == productModelStageId))
            {
                _additionalStageSequence++;
                var subStage = new SubStage(
                    Guid.NewGuid(),
                    scope.MainStage.Id,
                    $"Stage {scope.Factory.Code}-{_additionalStageSequence}",
                    $"S-{scope.Factory.Code}-{_additionalStageSequence}",
                    1,
                    _additionalStageSequence + 1);
                var productModelStage = new ProductModelStage(
                    Guid.NewGuid(),
                    scope.ProductModelId,
                    subStage.Id,
                    _additionalStageSequence + 1,
                    0m,
                    null,
                    CompensationMode.SharedPercentage);
                Db.AddRange(subStage, productModelStage);
                await Db.SaveChangesAsync();
                productModelStageId = productModelStage.Id;
                stageCode = subStage.Code;
                stageName = subStage.Name;
            }
            var record = new StageProductionRecord(
                Guid.NewGuid(),
                order.Id,
                productModelStageId,
                productionDate,
                produced,
                accepted,
                rejected,
                stageCode,
                stageName,
                0m,
                null,
                CompensationMode.SharedPercentage,
                scope.ProductModel.Code,
                scope.ProductModel.Name,
                scope.Factory.Code,
                scope.Factory.Name,
                scope.ProductionLine.LineCode ?? $"L-{scope.Factory.Code}",
                scope.ProductionLine.Name,
                scope.MainStage.Name,
                Guid.NewGuid(),
                null,
                ActorId,
                DateTime.UtcNow);
            var equivalentQuantity = workers.Count == 0 ? 0m : accepted / workers.Count;
            var allocations = workers.Select(worker =>
            {
                var allocation = new StageProductionWorkerAllocation(Guid.NewGuid(), worker.Id, worker.EmployeeCode, worker.FullName, null, null, null);
                allocation.SetCalculatedAmounts(equivalentQuantity, 0m);
                return allocation;
            }).ToArray();
            record.ReplaceAllocations(allocations);
            if (status is StageProductionRecordStatus.Approved or StageProductionRecordStatus.Cancelled)
            {
                record.Approve(ActorId, DateTime.UtcNow);
                if (status == StageProductionRecordStatus.Cancelled)
                    record.CancelProductionApproval("Test cancellation", ActorId, DateTime.UtcNow);
            }

            Db.Add(record);
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed record ReportScope(
        Factory Factory,
        ProductionLine ProductionLine,
        MainStage MainStage,
        SubStage SubStage,
        ProductModel ProductModel,
        ProductModelStage ProductModelStage)
    {
        public Guid FactoryId => Factory.Id;
        public Guid ProductionLineId => ProductionLine.Id;
        public Guid ProductModelId => ProductModel.Id;
        public Guid ProductModelStageId => ProductModelStage.Id;
    }
}
