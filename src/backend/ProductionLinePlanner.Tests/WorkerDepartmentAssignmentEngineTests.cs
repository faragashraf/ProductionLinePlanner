using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class WorkerDepartmentAssignmentEngineTests
{
    [Fact]
    public async Task Valid_active_department_assignment_updates_only_the_local_relation_and_audits_before_after()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Engine.AssignAsync(
            fixture.Worker.Id,
            fixture.Department.Id,
            fixture.Worker.OrganizationalDepartmentConcurrencyToken,
            fixture.ActorUserId,
            "test");

        Assert.True(result.IsSuccess, result.Error?.Message);
        var worker = await fixture.Db.Workers.AsNoTracking().SingleAsync();
        Assert.Equal(fixture.Department.Id, worker.OrganizationalDepartmentId);
        Assert.NotEqual(fixture.OriginalConcurrencyToken, worker.OrganizationalDepartmentConcurrencyToken);
        Assert.Equal(1, await fixture.Db.AuditLogs.CountAsync(log => log.EntityType == nameof(Worker)));
        Assert.Equal(7, worker.AttendanceDepartmentId);
        Assert.Equal("Planner import label", worker.LocalDepartmentName);
        Assert.Single(await fixture.Db.WorkerDefaultAssignments.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Assigning_the_same_department_creates_no_duplicate_or_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Engine.AssignAsync(
            fixture.Worker.Id,
            fixture.Department.Id,
            fixture.OriginalConcurrencyToken,
            fixture.ActorUserId);
        fixture.Db.ChangeTracker.Clear();

        var replay = await fixture.Engine.AssignAsync(
            fixture.Worker.Id,
            fixture.Department.Id,
            first.Value!.ConcurrencyToken,
            fixture.ActorUserId);

        Assert.True(replay.IsFailure);
        Assert.Equal("Conflict", replay.Error?.Code);
        Assert.Single(await fixture.Db.AuditLogs.ToArrayAsync());
        Assert.Equal(fixture.Department.Id, (await fixture.Db.Workers.SingleAsync()).OrganizationalDepartmentId);
    }

    [Fact]
    public async Task Inactive_department_and_missing_worker_are_rejected()
    {
        await using var fixture = await Fixture.CreateAsync(departmentActive: false);

        var inactive = await fixture.Engine.AssignAsync(
            fixture.Worker.Id,
            fixture.Department.Id,
            fixture.OriginalConcurrencyToken,
            fixture.ActorUserId);
        var missing = await fixture.Engine.AssignAsync(
            Guid.NewGuid(),
            fixture.Department.Id,
            fixture.OriginalConcurrencyToken,
            fixture.ActorUserId);

        Assert.Equal("ValidationError", inactive.Error?.Code);
        Assert.Equal("NotFound", missing.Error?.Code);
        Assert.Empty(await fixture.Db.AuditLogs.ToArrayAsync());
    }

    [Fact]
    public async Task Missing_management_permission_is_forbidden_in_the_engine()
    {
        await using var fixture = await Fixture.CreateAsync(permissions: ["workers.manage"]);

        var result = await fixture.Engine.AssignAsync(
            fixture.Worker.Id,
            fixture.Department.Id,
            fixture.OriginalConcurrencyToken,
            fixture.ActorUserId);

        Assert.Equal("Forbidden", result.Error?.Code);
        Assert.Null((await fixture.Db.Workers.SingleAsync()).OrganizationalDepartmentId);
    }

    [Fact]
    public async Task Stale_worker_token_returns_an_explicit_concurrency_conflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        var staleToken = fixture.OriginalConcurrencyToken;
        var first = await fixture.Engine.AssignAsync(
            fixture.Worker.Id,
            fixture.Department.Id,
            staleToken,
            fixture.ActorUserId);
        Assert.True(first.IsSuccess);

        var secondDepartment = new Department(Guid.NewGuid(), fixture.Factory.Id, "D-002", "قسم آخر", null, 2);
        fixture.Db.Departments.Add(secondDepartment);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Engine.AssignAsync(
            fixture.Worker.Id,
            secondDepartment.Id,
            staleToken,
            fixture.ActorUserId);

        Assert.Equal("ConcurrencyConflict", result.Error?.Code);
        Assert.Equal(fixture.Department.Id, (await fixture.Db.Workers.SingleAsync()).OrganizationalDepartmentId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db, Guid actorUserId, Factory factory, Department department, Worker worker, IReadOnlyCollection<string> permissions)
        {
            Db = db;
            ActorUserId = actorUserId;
            Factory = factory;
            Department = department;
            Worker = worker;
            OriginalConcurrencyToken = worker.OrganizationalDepartmentConcurrencyToken;
            Engine = new WorkerDepartmentAssignmentEngine(db, new PermissionStub(permissions), new AuditEngine(db));
        }

        public AppDbContext Db { get; }
        public WorkerDepartmentAssignmentEngine Engine { get; }
        public Guid ActorUserId { get; }
        public Factory Factory { get; }
        public Department Department { get; }
        public Worker Worker { get; }
        public Guid OriginalConcurrencyToken { get; }

        public static async Task<Fixture> CreateAsync(bool departmentActive = true, IReadOnlyCollection<string>? permissions = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new AppDbContext(options);
            var actor = Guid.NewGuid();
            var factory = new Factory(Guid.NewGuid(), "Factory", "F-001");
            var department = new Department(Guid.NewGuid(), factory.Id, "D-001", "قسم التشغيل", null, 1, departmentActive);
            var worker = new Worker(Guid.NewGuid(), "W-001", "اسم محلي", "zk-1", "B-1", attendanceDepartmentId: 7);
            worker.SetLocalDepartmentName("Planner import label");
            var stage = new SubStage(Guid.NewGuid(), Guid.NewGuid(), "Stage", "S-001", 1, 1, productionLineId: Guid.NewGuid());
            var assignment = new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, stage.Id, actor, DateTime.UtcNow);
            db.AddRange(factory, department, worker, assignment);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(db, actor, factory, department, worker, permissions ?? ["workers.manage", "departments.manage"]);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class PermissionStub(IReadOnlyCollection<string> permissions) : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permissions);

        public Task<ProductionLinePlanner.Application.DTOs.PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<ProductionLinePlanner.Application.DTOs.PermissionCatalogItemDto>());
    }
}
