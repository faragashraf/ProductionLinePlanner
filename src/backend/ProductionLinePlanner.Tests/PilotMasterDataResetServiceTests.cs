using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Bootstrap;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

/// <summary>Synthetic application data only. No ZKTime database is configured or accessed.</summary>
public sealed class PilotMasterDataResetServiceTests
{
    [Fact]
    public async Task Reset_removes_operational_and_master_data_but_preserves_security_and_worker_projection()
    {
        await using var fixture = await ResetFixture.CreateAsync();

        var preview = await fixture.Service.PreviewAsync();

        Assert.True(preview.TotalRecordsToDelete > 0);
        Assert.Equal(1, preview.WorkersPreserved);
        Assert.Equal(1, preview.UsersPreserved);
        Assert.Equal(1, preview.ActiveSuperAdminsPreserved);

        var applied = await fixture.Service.ApplyAsync(fixture.ActorId, confirmed: true);

        Assert.False(applied.WasAlreadyReset);
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Factories.ToArrayAsync());
        Assert.Empty(await fixture.Db.ProductionLines.ToArrayAsync());
        Assert.Empty(await fixture.Db.MainStages.ToArrayAsync());
        Assert.Empty(await fixture.Db.SubStages.ToArrayAsync());
        Assert.Empty(await fixture.Db.ProductModels.ToArrayAsync());
        Assert.Empty(await fixture.Db.ProductModelStages.ToArrayAsync());
        Assert.Empty(await fixture.Db.Set<ProductionOrder>().ToArrayAsync());
        Assert.Empty(await fixture.Db.Set<StageProductionRecord>().ToArrayAsync());
        Assert.Empty(await fixture.Db.Set<StageProductionWorkerAllocation>().ToArrayAsync());
        Assert.Empty(await fixture.Db.WorkerDefaultAssignments.ToArrayAsync());
        Assert.Empty(await fixture.Db.WorkerSalaryHistories.ToArrayAsync());
        var worker = await fixture.Db.Workers.SingleAsync();
        Assert.Equal("1001", worker.EmployeeCode);
        Assert.Equal("zk-1001", worker.AttendanceUserId);
        Assert.Equal(41, worker.AttendanceDepartmentId);
        Assert.Single(await fixture.Db.AppUsers.ToArrayAsync());
        Assert.Single(await fixture.Db.AppRoles.ToArrayAsync());
    }

    [Fact]
    public async Task Reset_rerun_is_safe_and_does_not_need_zktime()
    {
        await using var fixture = await ResetFixture.CreateAsync();

        await fixture.Service.ApplyAsync(fixture.ActorId, confirmed: true);
        var rerun = await fixture.Service.ApplyAsync(fixture.ActorId, confirmed: true);

        Assert.True(rerun.WasAlreadyReset);
        Assert.Single(await fixture.Db.Workers.ToArrayAsync());
        Assert.Single(await fixture.Db.AppUsers.ToArrayAsync());
    }

    [Fact]
    public async Task Reset_rolls_back_when_audit_fails()
    {
        await using var fixture = await ResetFixture.CreateAsync(throwingAudit: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyAsync(fixture.ActorId, confirmed: true));

        fixture.Db.ChangeTracker.Clear();
        Assert.Single(await fixture.Db.Factories.ToArrayAsync());
        Assert.Single(await fixture.Db.ProductModels.ToArrayAsync());
        Assert.Single(await fixture.Db.Set<ProductionOrder>().ToArrayAsync());
        Assert.Single(await fixture.Db.Workers.ToArrayAsync());
        Assert.Single(await fixture.Db.AppUsers.ToArrayAsync());
    }

    private sealed class ResetFixture : IAsyncDisposable
    {
        private ResetFixture(SqliteConnection connection, AppDbContext db, PilotMasterDataResetService service, Guid actorId)
        {
            Connection = connection;
            Db = db;
            Service = service;
            ActorId = actorId;
        }

        public SqliteConnection Connection { get; }
        public AppDbContext Db { get; }
        public PilotMasterDataResetService Service { get; }
        public Guid ActorId { get; }

        public static async Task<ResetFixture> CreateAsync(bool throwingAudit = false)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) =>
                string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow.AddMinutes(-5);
            var actorId = Guid.NewGuid();
            var role = new AppRole(Guid.NewGuid(), UserRole.SuperAdmin, UserRole.SuperAdmin.ToString(), isSystemRole: true);
            var actor = new AppUser(actorId, "Reset test actor", "reset@example.test", "hash");
            actor.AssignRole(role);
            var worker = new Worker(Guid.NewGuid(), "1001", "Projected worker", "zk-1001", attendanceDepartmentId: 41);
            var factory = new Factory(Guid.NewGuid(), "Test factory", "TEST", createdAtUtc: now);
            var department = new Department(Guid.NewGuid(), factory.Id, "OPS", "التشغيل", "Operations", 0, createdAtUtc: now);
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Test line", 0, "TEST-LINE", createdAtUtc: now, departmentId: department.Id);
            var main = new MainStage(Guid.NewGuid(), department.Id, "Test main", 0, createdAtUtc: now);
            var stage = new SubStage(Guid.NewGuid(), main.Id, "Test stage", "TEST-STG", 0, 1, createdAtUtc: now);
            var product = new ProductModel(Guid.NewGuid(), "TEST-PRODUCT", "Test product", createdAtUtc: now);
            var mapping = new ProductModelStage(Guid.NewGuid(), product.Id, line.Id, stage.Id, 1, 1m, null, CompensationMode.SharedPercentage, createdAtUtc: now);
            var order = new ProductionOrder(Guid.NewGuid(), "TEST-ORDER", product.Id, line.Id, DateOnly.FromDateTime(now), 10m, null, actorId, now);
            var record = new StageProductionRecord(Guid.NewGuid(), order.Id, mapping.Id, DateOnly.FromDateTime(now), 10m, 10m, 0m,
                stage.Code, stage.Name, 1m, null, CompensationMode.SharedPercentage, product.Code, product.Name,
                factory.Code, factory.Name, line.LineCode!, line.Name, main.Name, Guid.NewGuid(), null, actorId, now);
            var allocation = new StageProductionWorkerAllocation(Guid.NewGuid(), worker.Id, worker.EmployeeCode, worker.FullName, 100m, null, null, inputQuantity: 10m)
            {
                StageProductionRecord = record
            };

            db.AddRange(actor, worker, factory, department, line, main, stage, product, mapping, order, record, allocation,
                new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, stage.Id, actorId, now, productionLineId: line.Id),
                new AssignmentTimelineEntry(Guid.NewGuid(), worker.Id, null, stage.Id, "Default", "Assign", null, now, null, actorId, false),
                new StageReadinessSnapshot(Guid.NewGuid(), "SubStage", stage.Id, 1, 1, 0, 0, 0, now),
                new WorkerSalaryHistory(Guid.NewGuid(), worker.Id, 100m, "EGP", now, null, null, actorId, actorId, now));
            await db.SaveChangesAsync();

            IAuditEngine audit = throwingAudit ? new ThrowingAuditEngine() : new NoOpAuditEngine();
            return new ResetFixture(connection, db, new PilotMasterDataResetService(db, audit), actorId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class NoOpAuditEngine : IAuditEngine
    {
        public Task<Result> RecordAsync(Guid actorUserId, AuditActionType actionType, string entityType, string entityId, object? before = null, object? after = null, string? requestMeta = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class ThrowingAuditEngine : IAuditEngine
    {
        public Task<Result> RecordAsync(Guid actorUserId, AuditActionType actionType, string entityType, string entityId, object? before = null, object? after = null, string? requestMeta = null, CancellationToken cancellationToken = default) =>
            Task.FromException<Result>(new InvalidOperationException("Audit write failed."));
    }
}
