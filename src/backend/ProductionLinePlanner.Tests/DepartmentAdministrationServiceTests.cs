using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class DepartmentAdministrationServiceTests
{
    [Fact]
    public async Task GetDepartmentsAsync_returns_source_observed_departments_in_order()
    {
        await using var fixture = await Fixture.CreateAsync(
            [new(2, "Quality"), new(1, "Operations")]);

        var result = await fixture.Service.GetDepartmentsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(["Operations", "Quality"], result.Value!.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task Create_and_rename_are_blocked_by_external_source_read_only_policy()
    {
        await using var fixture = await Fixture.CreateAsync([new(1, "Operations")]);

        var create = await fixture.Service.CreateDepartmentAsync("Packaging", fixture.ActorUserId);
        var rename = await fixture.Service.UpdateDepartmentNameAsync(1, "Packaging", fixture.ActorUserId);

        Assert.Equal("ExternalSourceReadOnly", create.Error!.Code);
        Assert.Equal("ExternalSourceReadOnly", rename.Error!.Code);
        Assert.Equal("Operations", fixture.Reader.Departments[1].Name);
    }

    [Fact]
    public async Task Move_worker_is_blocked_and_does_not_change_local_source_observation()
    {
        var worker = new Worker(Guid.NewGuid(), "001", "Local", "1001", "001", attendanceDepartmentId: 1);
        await using var fixture = await Fixture.CreateAsync([new(1, "Operations"), new(2, "Quality")], [worker]);

        var result = await fixture.Service.MoveWorkerToDepartmentAsync(worker.Id, 2, fixture.ActorUserId);

        Assert.Equal("ExternalSourceReadOnly", result.Error!.Code);
        Assert.Equal(1, (await fixture.Db.Workers.AsNoTracking().SingleAsync()).AttendanceDepartmentId);
    }

    [Fact]
    public async Task Delete_is_blocked_and_existing_use_remains_a_conflict()
    {
        var worker = new Worker(Guid.NewGuid(), "001", "Local", attendanceDepartmentId: 1);
        await using var fixture = await Fixture.CreateAsync([new(1, "Operations"), new(2, "Quality")], [worker]);

        var used = await fixture.Service.CanDeleteDepartmentAsync(1);
        var unused = await fixture.Service.CanDeleteDepartmentAsync(2);
        var delete = await fixture.Service.DeleteDepartmentAsync(2, fixture.ActorUserId);

        Assert.Equal("Conflict", used.Error!.Code);
        Assert.Equal("ExternalSourceReadOnly", unused.Error!.Code);
        Assert.Equal("ExternalSourceReadOnly", delete.Error!.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppDbContext db, FakeAttendanceDepartmentReader reader)
        {
            Db = db;
            Reader = reader;
            ActorUserId = Guid.NewGuid();
            Service = new DepartmentAdministrationService(db, reader);
        }

        public AppDbContext Db { get; }
        public FakeAttendanceDepartmentReader Reader { get; }
        public Guid ActorUserId { get; }
        public IDepartmentAdministrationService Service { get; }

        public static async Task<Fixture> CreateAsync(
            IEnumerable<AttendanceDepartmentRecord> departments,
            IEnumerable<Worker>? workers = null)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            db.Workers.AddRange(workers ?? []);
            await db.SaveChangesAsync();
            var reader = new FakeAttendanceDepartmentReader(departments.ToDictionary(x => x.DepartmentId));
            return new Fixture(db, reader);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
