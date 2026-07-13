using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class ProductionCostRecordingServiceTests
{
    [Fact]
    public async Task Shared_percentage_keeps_production_at_500_and_splits_earnings()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var record = await fixture.CreateAndApproveAsync(500m, [fixture.Allocation(fixture.WorkerA.Id, 50m), fixture.Allocation(fixture.WorkerB.Id, 50m)]);
        var report = await fixture.Service.DailyReportAsync(fixture.Today, fixture.Today, null, null, null, default);

        Assert.Equal(500m, record.ProducedQuantity);
        Assert.Equal(250m, record.Workers.First().EquivalentQuantity);
        Assert.Equal(125m, record.Workers.First().CalculatedEarning);
        Assert.Equal(250m, record.TotalWorkerEarnings);
        Assert.Single(report);
        Assert.Equal(500m, report.Single().ProducedQuantity);
        Assert.Equal(250m, report.Single().StageCost);
        Assert.Equal(250m, report.Single().Workers.Sum(x => x.CalculatedEarning));
    }

    [Fact]
    public async Task Full_rate_per_worker_pays_each_worker_without_inflating_production()
    {
        await using var fixture = await Fixture.CreateAsync("FullRatePerWorker", 0.50m, 17m);
        var record = await fixture.CreateAndApproveAsync(500m, [fixture.Allocation(fixture.WorkerA.Id), fixture.Allocation(fixture.WorkerB.Id)]);

        Assert.Equal(500m, record.ProducedQuantity);
        Assert.All(record.Workers, worker => { Assert.Equal(0m, worker.EquivalentQuantity); Assert.Equal(250m, worker.CalculatedEarning); });
        Assert.Equal(500m, record.TotalWorkerEarnings);
    }

    [Fact]
    public async Task Fixed_amount_does_not_multiply_by_quantity()
    {
        await using var fixture = await Fixture.CreateAsync("FixedAmount", 0.50m, 17m);
        var record = await fixture.CreateAndApproveAsync(500m, [fixture.Allocation(fixture.WorkerA.Id, fixedAmount: 37m), fixture.Allocation(fixture.WorkerB.Id, fixedAmount: 61m)]);

        Assert.Equal(500m, record.ProducedQuantity);
        Assert.Equal(98m, record.TotalWorkerEarnings);
        Assert.Equal([37m, 61m], record.Workers.Select(x => x.CalculatedEarning).Order().ToArray());
    }

    [Theory]
    [InlineData(40, 40, 30)]
    [InlineData(40, 40, 0)]
    public async Task Shared_percentage_rejects_totals_other_than_100(decimal a, decimal b, decimal c)
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var allocations = new[] { fixture.Allocation(fixture.WorkerA.Id, a), fixture.Allocation(fixture.WorkerB.Id, b), fixture.Allocation(fixture.WorkerC.Id, c) };
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.CreateDraftAsync(500m, 500m, 0m, allocations));
    }

    [Fact]
    public async Task Validation_rejects_duplicate_left_employment_invalid_quantities_and_wrong_mode_fields()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 50m), fixture.Allocation(fixture.WorkerA.Id, 50m)]));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.LeftWorker.Id, 100m)]));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.CreateDraftAsync(500m, 490m, 20m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.CreateDraftAsync(-1m, 0m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]));

        await using var fullRateFixture = await Fixture.CreateAsync("FullRatePerWorker", 0.50m, 17m);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fullRateFixture.CreateDraftAsync(500m, 500m, 0m, [fullRateFixture.Allocation(fullRateFixture.WorkerA.Id, 50m)]));
        await using var fixedFixture = await Fixture.CreateAsync("FixedAmount", 0.50m, 17m);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixedFixture.CreateDraftAsync(500m, 500m, 0m, [fixedFixture.Allocation(fixedFixture.WorkerA.Id)]));
    }

    [Fact]
    public async Task Snapshot_uses_model_specific_rate_and_is_unchanged_after_configuration_edit()
    {
        await using var fixture = await Fixture.CreateAsync("FullRatePerWorker", 0.50m, 17m);
        var first = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id)]);
        fixture.Stage.Update(fixture.SubStage.Id, 1, 0.70m, 25m, fixture.Stage.CompensationMode, true, true, null);
        await fixture.Db.SaveChangesAsync();
        var second = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id)]);

        Assert.Equal(0.50m, first.PiecePrice); Assert.Equal(17m, first.StandardSeconds);
        Assert.Equal(0.70m, second.PiecePrice); Assert.Equal(25m, second.StandardSeconds);
        var persistedFirst = await fixture.Service.GetRecordAsync(first.Id, default);
        Assert.Equal(0.50m, persistedFirst.PiecePrice); Assert.Equal(17m, persistedFirst.StandardSeconds);
    }

    [Fact]
    public async Task Lifecycle_locks_approved_excludes_cancelled_and_rejects_closed_orders()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var draft = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        var approved = await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Service.UpdateDraftAsync(draft.Id, new UpdateStageProductionRecordRequest(fixture.Today, 400m, 400m, 0m, approved.ConcurrencyToken, null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]), fixture.ActorId, default));
        await fixture.Service.CancelAsync(draft.Id, approved.ConcurrencyToken, fixture.ActorId, default);
        Assert.Empty(await fixture.Service.DailyReportAsync(fixture.Today, fixture.Today, null, null, null, default));
        await fixture.Service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Cancelled, fixture.ActorId, default);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.CreateDraftAsync(1m, 1m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]));
    }

    [Fact]
    public async Task Audit_records_business_events_and_audit_failure_blocks_operation()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m, useRealAudit: true);
        var draft = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        var approved = await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);
        await fixture.Service.CancelAsync(draft.Id, approved.ConcurrencyToken, fixture.ActorId, default);
        var logs = await fixture.Db.AuditLogs.ToListAsync();
        Assert.Contains(logs, x => x.EntityType == "StageProductionRecord" && x.ActionType == AuditActionType.Create);
        Assert.Contains(logs, x => x.EntityType == "StageProductionWorkerAllocation");
        Assert.Contains(logs, x => x.EntityType == "StageProductionRecord" && x.ActionType == AuditActionType.Cancel);
        Assert.All(logs, x => Assert.Equal(fixture.ActorId, x.ActorUserId));

        await using var failed = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var failingService = new ProductionCostRecordingService(failed.Db, new FailingAuditEngine());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.CreateOrderAsync(new CreateProductionOrderRequest("AUDIT-FAIL", failed.Model.Id, null, failed.Today, 1m, null), failed.ActorId, default));
    }

    [Fact]
    public async Task Approved_quantity_is_capped_idempotent_and_cancelled_records_release_capacity()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var first = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        var approved = await fixture.Service.ApproveAsync(first.Id, first.ConcurrencyToken, fixture.ActorId, default);
        var repeated = await fixture.Service.ApproveAsync(first.Id, approved.ConcurrencyToken, fixture.ActorId, default);
        Assert.Equal(approved.Id, repeated.Id);
        var extra = await fixture.CreateDraftAsync(1m, 1m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Service.ApproveAsync(extra.Id, extra.ConcurrencyToken, fixture.ActorId, default));
        var cancelled = await fixture.Service.CancelAsync(first.Id, approved.ConcurrencyToken, fixture.ActorId, default);
        Assert.Equal("Cancelled", cancelled.Status);
        var accepted = await fixture.Service.ApproveAsync(extra.Id, extra.ConcurrencyToken, fixture.ActorId, default);
        Assert.Equal("Approved", accepted.Status);
    }

    [Fact]
    public async Task Relational_clients_allow_first_mutation_reject_stale_requests_and_allow_retry_after_refresh()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m, useRealAudit: true);
        var draft = await fixture.CreateDraftAsync(10m, 10m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        StageProductionRecordDto aRead;
        await using (var clientARead = fixture.CreateClient())
            aRead = await clientARead.Service.GetRecordAsync(draft.Id, default);
        await using var clientA = fixture.CreateClient();
        await using var clientB = fixture.CreateClient();
        var bRead = await clientB.Service.GetRecordAsync(draft.Id, default);
        var bUpdated = await clientB.Service.UpdateDraftAsync(draft.Id, new UpdateStageProductionRecordRequest(fixture.Today, 9m, 9m, 0m, bRead.ConcurrencyToken, null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]), fixture.ActorId, default);
        Assert.NotEqual(bRead.ConcurrencyToken, bUpdated.ConcurrencyToken);

        await Assert.ThrowsAsync<ProductionConflictException>(() => clientA.Service.UpdateDraftAsync(draft.Id, new UpdateStageProductionRecordRequest(fixture.Today, 8m, 8m, 0m, aRead.ConcurrencyToken, null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]), fixture.ActorId, default));
        await Assert.ThrowsAsync<ProductionConflictException>(() => clientA.Service.ApproveAsync(draft.Id, aRead.ConcurrencyToken, fixture.ActorId, default));
        Assert.Equal("Draft", (await fixture.Service.GetRecordAsync(draft.Id, default)).Status);
        Assert.DoesNotContain(await fixture.Db.AuditLogs.ToListAsync(), x => x.EntityType == "StageProductionRecord" && x.EntityAfterJson!.Contains("Approved"));

        var refreshed = await clientA.Service.GetRecordAsync(draft.Id, default);
        var approved = await clientA.Service.ApproveAsync(draft.Id, refreshed.ConcurrencyToken, fixture.ActorId, default);
        await using var clientC = fixture.CreateClient();
        var approvalAuditCount = (await fixture.Db.AuditLogs.ToListAsync()).Count(x => x.EntityType == "StageProductionRecord" && x.EntityAfterJson!.Contains("\"Status\":1"));
        await Assert.ThrowsAsync<ProductionConflictException>(() => clientC.Service.ApproveAsync(draft.Id, refreshed.ConcurrencyToken, fixture.ActorId, default));
        Assert.Equal(approvalAuditCount, (await fixture.Db.AuditLogs.ToListAsync()).Count(x => x.EntityType == "StageProductionRecord" && x.EntityAfterJson!.Contains("\"Status\":1")));
        var repeatedApproval = await clientC.Service.ApproveAsync(draft.Id, approved.ConcurrencyToken, fixture.ActorId, default);
        Assert.Equal(approved.TotalWorkerEarnings, repeatedApproval.TotalWorkerEarnings);
        await Assert.ThrowsAsync<ProductionConflictException>(() => clientC.Service.CancelAsync(draft.Id, refreshed.ConcurrencyToken, fixture.ActorId, default));
        var cancelled = await clientC.Service.CancelAsync(draft.Id, approved.ConcurrencyToken, fixture.ActorId, default);
        Assert.Equal("Cancelled", cancelled.Status);
    }

    [Fact]
    public async Task Historical_snapshot_and_financial_audit_remain_reviewable()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m, useRealAudit: true);
        var draft = await fixture.CreateDraftAsync(10m, 10m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);
        fixture.Db.Entry(fixture.Model).Property(nameof(ProductModel.Code)).CurrentValue = "MODEL-CHANGED";
        fixture.Db.Entry(fixture.Model).Property(nameof(ProductModel.Name)).CurrentValue = "Changed model";
        await fixture.Db.SaveChangesAsync();
        var report = await fixture.Service.DailyReportAsync(fixture.Today, fixture.Today, null, null, null, default);
        Assert.Equal("MODEL-A", report.Single().ModelCode);
        var approval = (await fixture.Db.AuditLogs.ToListAsync()).Single(x => x.EntityType == "StageProductionRecord" && x.ActionType == AuditActionType.Update && x.EntityAfterJson!.Contains("Allocations"));
        Assert.Contains("CalculatedEarning", approval.EntityAfterJson);
        Assert.Contains(fixture.WorkerA.EmployeeCode, approval.EntityAfterJson);
    }

    [Fact]
    public async Task Order_lifecycle_blocks_completion_with_drafts_and_cancellation_with_approved_records()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var draft = await fixture.CreateDraftAsync(10m, 10m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Completed, fixture.ActorId, default));
        var approved = await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Cancelled, fixture.ActorId, default));
        await fixture.Service.CancelAsync(draft.Id, approved.ConcurrencyToken, fixture.ActorId, default);
        var cancelledOrder = await fixture.Service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Cancelled, fixture.ActorId, default);
        Assert.Equal("Cancelled", cancelledOrder.Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(SqliteConnection connection, AppDbContext db, ProductModel model, SubStage subStage, ProductModelStage stage, Worker a, Worker b, Worker c, Worker left, ProductionCostRecordingService service, Guid actorId, DateOnly today)
        { Connection = connection; Db = db; Model = model; SubStage = subStage; Stage = stage; WorkerA = a; WorkerB = b; WorkerC = c; LeftWorker = left; Service = service; ActorId = actorId; Today = today; }
        private SqliteConnection Connection { get; }
        public AppDbContext Db { get; } public ProductModel Model { get; } public SubStage SubStage { get; } public ProductModelStage Stage { get; } public Worker WorkerA { get; } public Worker WorkerB { get; } public Worker WorkerC { get; } public Worker LeftWorker { get; } public ProductionCostRecordingService Service { get; } public Guid ActorId { get; } public DateOnly Today { get; } public ProductionOrderDto Order { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync(string mode, decimal price, decimal seconds, IAuditEngine? audit = null, bool useRealAudit = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase)); await connection.OpenAsync(); var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
            var factory = new Factory(Guid.NewGuid(), "Fixture Factory", "FIX"); var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Fixture Line", 1); var mainStage = new MainStage(Guid.NewGuid(), line.Id, "Fixture Main", 1); var model = new ProductModel(Guid.NewGuid(), "MODEL-A", "Model A"); var subStage = new SubStage(Guid.NewGuid(), mainStage.Id, "Sew", "SEW", 1, 1);
            var stage = new ProductModelStage(Guid.NewGuid(), model.Id, subStage.Id, 1, price, seconds, Enum.Parse<CompensationMode>(mode));
            var a = new Worker(Guid.NewGuid(), "A", "Worker A"); var b = new Worker(Guid.NewGuid(), "B", "Worker B"); var c = new Worker(Guid.NewGuid(), "C", "Worker C"); var left = new Worker(Guid.NewGuid(), "L", "Left Worker", employmentStatus: EmploymentStatus.LeftEmployment);
            db.AddRange(factory, line, mainStage, model, subStage, stage, a, b, c, left); await db.SaveChangesAsync(); var actor = Guid.NewGuid(); if (useRealAudit) { db.Add(new AppUser(actor, "Audit User", $"audit-{actor:N}@example.test", "hash")); await db.SaveChangesAsync(); }
            var service = new ProductionCostRecordingService(db, audit ?? (useRealAudit ? new AuditEngine(db) : new RecordingAuditEngine()));
            var fixture = new Fixture(connection, db, model, subStage, stage, a, b, c, left, service, actor, DateOnly.FromDateTime(DateTime.UtcNow));
            fixture.Order = await service.CreateOrderAsync(new CreateProductionOrderRequest("PO-" + Guid.NewGuid().ToString("N"), model.Id, null, fixture.Today, 500m, null), actor, default);
            await service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Active, actor, default);
            return fixture;
        }
        public WorkerAllocationRequest Allocation(Guid workerId, decimal? percentage = null, decimal? fixedAmount = null) => new(workerId, percentage, fixedAmount, null);
        public Client CreateClient()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
            return new Client(context, new ProductionCostRecordingService(context, new AuditEngine(context)));
        }
        public Task<StageProductionRecordDto> CreateDraftAsync(decimal produced, decimal accepted, decimal rejected, IReadOnlyCollection<WorkerAllocationRequest> workers) => Service.CreateDraftAsync(new CreateStageProductionRecordRequest(Order.Id, Stage.Id, Today, produced, accepted, rejected, Guid.NewGuid(), null, workers), ActorId, default);
        public async Task<StageProductionRecordDto> CreateAndApproveAsync(decimal accepted, IReadOnlyCollection<WorkerAllocationRequest> workers) { var draft = await CreateDraftAsync(accepted, accepted, 0m, workers); return await Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, ActorId, default); }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    private sealed class Client(AppDbContext db, ProductionCostRecordingService service) : IAsyncDisposable
    {
        public ProductionCostRecordingService Service { get; } = service;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
    private sealed class FailingAuditEngine : IAuditEngine
    { public Task<Result> RecordAsync(Guid actorUserId, AuditActionType actionType, string entityType, string entityId, object? before = null, object? after = null, string? requestMeta = null, CancellationToken cancellationToken = default) => Task.FromResult(Result.Failure(new Error("AuditFailed", "Audit persistence failed."))); }
}
