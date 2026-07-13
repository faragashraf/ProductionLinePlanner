using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class DepartmentAdministrationServiceTests
{
    [Fact]
    public async Task GetDepartmentsAsync_returns_ordered_departments()
    {
        await using var fixture = await DepartmentFixture.CreateAsync(
            [
                new AttendanceDepartmentRecord(2, "Quality"),
                new AttendanceDepartmentRecord(1, "Operations")
            ]);

        var result = await fixture.Service.GetDepartmentsAsync();

        Assert.True(result.IsSuccess);
        var departments = result.Value!.ToArray();
        Assert.Equal(["Operations", "Quality"], departments.Select(x => x.Name).ToArray());
        Assert.Equal(1, departments[0].DepartmentId);
        Assert.Equal(2, departments[1].DepartmentId);
    }

    [Fact]
    public async Task CreateDepartmentAsync_adds_department_and_returns_record()
    {
        await using var fixture = await DepartmentFixture.CreateAsync([]);

        var result = await fixture.Service.CreateDepartmentAsync("Packaging", fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("Packaging", result.Value!.Name);
        Assert.Single(fixture.AuditEngine.Calls);
    }

    [Fact]
    public async Task CreateDepartmentAsync_rejects_duplicate_names()
    {
        await using var fixture = await DepartmentFixture.CreateAsync([new AttendanceDepartmentRecord(1, "Operations")]);

        var result = await fixture.Service.CreateDepartmentAsync("Operations", fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateDepartmentNameAsync_rejects_unknown_department()
    {
        await using var fixture = await DepartmentFixture.CreateAsync([new AttendanceDepartmentRecord(1, "Operations")]);

        var result = await fixture.Service.UpdateDepartmentNameAsync(3, "New Name", fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateDepartmentNameAsync_rejects_duplicate_name()
    {
        await using var fixture = await DepartmentFixture.CreateAsync(
            [
                new AttendanceDepartmentRecord(1, "Operations"),
                new AttendanceDepartmentRecord(2, "Packaging")
            ]);

        var result = await fixture.Service.UpdateDepartmentNameAsync(2, "operations", fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task MoveWorkerToDepartmentAsync_moves_worker_and_logs_audit()
    {
        await using var fixture = await DepartmentFixture.CreateAsync(
            [
                new AttendanceDepartmentRecord(1, "Operations"),
                new AttendanceDepartmentRecord(2, "Quality")
            ],
            workers: [new Worker(
                id: Guid.NewGuid(),
                employeeCode: "W-01",
                fullName: "Worker One",
                attendanceUserId: "111",
                attendanceDepartmentId: 1)]);

        var result = await fixture.Service.MoveWorkerToDepartmentAsync(
            fixture.Workers[0].Id,
            2,
            fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var updated = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.Id == fixture.Workers[0].Id);
        Assert.Equal(2, updated.AttendanceDepartmentId);
        Assert.Contains(fixture.AttendanceWriter.DepartmentUpdates, item => item.DepartmentId == 2);
        Assert.Single(fixture.AuditEngine.Calls);
    }

    [Fact]
    public async Task MoveWorkerToDepartmentAsync_rejects_missing_worker()
    {
        await using var fixture = await DepartmentFixture.CreateAsync([new AttendanceDepartmentRecord(1, "Operations")]);

        var result = await fixture.Service.MoveWorkerToDepartmentAsync(Guid.NewGuid(), 1, fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteDepartmentAsync_returns_validation_for_v1_when_not_used()
    {
        await using var fixture = await DepartmentFixture.CreateAsync([new AttendanceDepartmentRecord(1, "Operations")]);

        var result = await fixture.Service.DeleteDepartmentAsync(1, fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("ValidationError", result.Error!.Code);
        Assert.Empty(fixture.AuditEngine.Calls);
    }

    [Fact]
    public async Task CanDeleteDepartmentAsync_returns_conflict_when_department_is_in_use()
    {
        await using var fixture = await DepartmentFixture.CreateAsync(
            [new AttendanceDepartmentRecord(1, "Operations")],
            workers: [new Worker(
                id: Guid.NewGuid(),
                employeeCode: "W-01",
                fullName: "Worker One",
                attendanceUserId: "111",
                attendanceDepartmentId: 1)]);

        var result = await fixture.Service.CanDeleteDepartmentAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    private sealed class DepartmentFixture : IAsyncDisposable
    {
        private DepartmentFixture(
            AppDbContext dbContext,
            IDepartmentAdministrationService service,
            FakeAttendanceDepartmentReader reader,
            FakeAttendanceDepartmentWriter writer,
            FakeAttendanceEmployeeWriter attendanceWriter,
            FakeAttendanceEmployeeReader attendanceEmployeeReader,
            RecordingAuditEngine auditEngine,
            IReadOnlyList<Worker> workers,
            Guid actorUserId)
        {
            DbContext = dbContext;
            Service = service;
            DepartmentReader = reader;
            DepartmentWriter = writer;
            AttendanceWriter = attendanceWriter;
            AttendanceEmployeeReader = attendanceEmployeeReader;
            AuditEngine = auditEngine;
            Workers = workers;
            ActorUserId = actorUserId;
        }

        public AppDbContext DbContext { get; }
        public IDepartmentAdministrationService Service { get; }
        public FakeAttendanceDepartmentReader DepartmentReader { get; }
        public FakeAttendanceDepartmentWriter DepartmentWriter { get; }
        public FakeAttendanceEmployeeWriter AttendanceWriter { get; }
        public FakeAttendanceEmployeeReader AttendanceEmployeeReader { get; }
        public RecordingAuditEngine AuditEngine { get; }
        public IReadOnlyList<Worker> Workers { get; }
        public Guid ActorUserId { get; }

        public static async Task<DepartmentFixture> CreateAsync(
            IEnumerable<AttendanceDepartmentRecord> departments,
            IEnumerable<Worker>? workers = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            var dbContext = new AppDbContext(options);
            var departmentMap = departments.ToDictionary(
                x => x.DepartmentId,
                x => x);

            var attendanceDepartments = departmentMap.Any()
                ? new Dictionary<int, AttendanceDepartmentRecord>(departmentMap)
                : new Dictionary<int, AttendanceDepartmentRecord>
                {
                    [1] = new AttendanceDepartmentRecord(1, "Operations")
                };

            var attendanceEmployees = new Dictionary<string, AttendanceEmployeeRecord?>
            {
                ["111"] = new AttendanceEmployeeRecord("111", 1, "1001", "Worker One", true)
            };

            var serviceAttendanceWriter = new FakeAttendanceEmployeeWriter(attendanceEmployees, null, null);
            var departmentReader = new FakeAttendanceDepartmentReader(attendanceDepartments);
            var attendanceEmployeeReader = new FakeAttendanceEmployeeReader(attendanceEmployees);
            var auditEngine = new RecordingAuditEngine();

            var departmentWriterForService = new FakeAttendanceDepartmentWriter(
                departments: new Dictionary<int, AttendanceDepartmentRecord>(attendanceDepartments),
                createDepartmentAsync: (name, token) =>
                {
                    var trimmed = name.Trim();
                    if (attendanceDepartments.Values.Any(x => string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Task.FromResult(Result<AttendanceDepartmentRecord>.Failure(new Error("Conflict", "Department name must be unique.")));
                    }

                    var nextId = attendanceDepartments.Keys.DefaultIfEmpty(0).Max() + 1;
                    var created = new AttendanceDepartmentRecord(nextId, trimmed);
                    attendanceDepartments[nextId] = created;
                    return Task.FromResult(Result<AttendanceDepartmentRecord>.Success(created));
                },
                updateDepartmentNameAsync: (departmentId, name, token) =>
                {
                    if (attendanceDepartments.TryGetValue(departmentId, out var existing) is false)
                    {
                        return Task.FromResult(Result.Failure(new Error("NotFound", "Department was not found in attendance source.")));
                    }

                    var normalized = name.Trim();
                    if (attendanceDepartments.Values.Any(x => string.Equals(x.Name, normalized, StringComparison.OrdinalIgnoreCase) && x.DepartmentId != departmentId))
                    {
                        return Task.FromResult(Result.Failure(new Error("Conflict", "Department name must be unique.")));
                    }

                    attendanceDepartments[departmentId] = new AttendanceDepartmentRecord(departmentId, normalized);
                    return Task.FromResult(Result.Success());
                });

            foreach (var worker in workers ?? Array.Empty<Worker>())
            {
                dbContext.Workers.Add(worker);
            }

            await dbContext.SaveChangesAsync();

            var service = new DepartmentAdministrationService(
                dbContext,
                departmentReader,
                departmentWriterForService,
                serviceAttendanceWriter,
                attendanceEmployeeReader,
                auditEngine);

            return new DepartmentFixture(
                dbContext,
                service,
                departmentReader,
                departmentWriterForService,
                serviceAttendanceWriter,
                attendanceEmployeeReader,
                auditEngine,
                workers?.ToList() ?? new List<Worker>(),
                Guid.NewGuid());
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
