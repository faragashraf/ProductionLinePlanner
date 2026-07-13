using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class EmployeeMasterDataServiceTests
{
    [Fact]
    public async Task GetWorkersAsync_returns_filtered_paged_result()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync(
            [
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "W-001",
                    fullName: "Ali Hassan",
                    attendanceUserId: "111",
                    attendanceDepartmentId: 10,
                    isActive: true),
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "W-002",
                    fullName: "Mona",
                    attendanceUserId: "222",
                    attendanceDepartmentId: 11,
                    isActive: false)
            ]);

        var result = await fixture.Service.GetWorkersAsync("Ali", true, page: 1, pageSize: 10);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value);
        Assert.Equal("Ali Hassan", result.Value.Single().FullName);
    }

    [Fact]
    public async Task GetWorkersAsync_rejects_invalid_paging()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var result = await fixture.Service.GetWorkersAsync(null, true, page: 0, pageSize: 10);

        Assert.True(result.IsFailure);
        Assert.Equal("ValidationError", result.Error!.Code);
    }

    [Fact]
    public async Task GetWorkerAsync_returns_worker_by_id()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var result = await fixture.Service.GetWorkerAsync(fixture.Workers[0].Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Workers[0].EmployeeCode, result.Value!.EmployeeCode);
    }

    [Fact]
    public async Task GetWorkerAsync_returns_not_found()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var result = await fixture.Service.GetWorkerAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateMasterIdentityAsync_updates_name_and_department_when_attendance_sync_succeeds()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var request = new UpdateWorkerRequest
        {
            FullName = "Ahmed Nasser",
            AttendanceDepartmentId = 20,
            Phone = "0110001111"
        };

        var result = await fixture.Service.UpdateMasterIdentityAsync(fixture.Workers[0].Id, request, fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var updated = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.Id == fixture.Workers[0].Id);
        Assert.Equal("Ahmed Nasser", updated.FullName);
        Assert.Equal(20, updated.AttendanceDepartmentId);
        Assert.Equal("0110001111", updated.Phone);
        Assert.Equal(fixture.Workers[0].AttendanceUserId, fixture.AttendanceEmployeeWriter.FullNameUpdates.Single().AttendanceUserId);
        Assert.Equal("Ahmed Nasser", fixture.AttendanceEmployeeWriter.FullNameUpdates.Single().FullName);
        Assert.Equal(20, fixture.AttendanceEmployeeWriter.DepartmentUpdates.Single().DepartmentId);
        Assert.Single(fixture.AuditEngine.Calls);
    }

    [Fact]
    public async Task UpdateMasterIdentityAsync_rejects_when_actor_missing()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var result = await fixture.Service.UpdateMasterIdentityAsync(fixture.Workers[0].Id, new UpdateWorkerRequest { FullName = "Updated" }, Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("Unauthorized", result.Error!.Code);
        Assert.Empty(fixture.AuditEngine.Calls);
    }

    [Fact]
    public async Task UpdateMasterIdentityAsync_rejects_request_without_mutable_fields()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var result = await fixture.Service.UpdateMasterIdentityAsync(fixture.Workers[0].Id, new UpdateWorkerRequest(), fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("ValidationError", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateMasterIdentityAsync_rejects_invalid_name_input()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var result = await fixture.Service.UpdateMasterIdentityAsync(fixture.Workers[0].Id, new UpdateWorkerRequest
        {
            FullName = "  "
        }, fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("ValidationError", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateMasterIdentityAsync_returns_failure_when_attendance_sync_fails_and_projection_is_not_saved()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync(
            updateWorkerFullNameAsync: (_, _, _) => Task.FromResult(Result.Failure(new Error("ValidationError", "ATT sync failed."))));

        var before = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.Id == fixture.Workers[0].Id);

        var result = await fixture.Service.UpdateMasterIdentityAsync(fixture.Workers[0].Id, new UpdateWorkerRequest
        {
            FullName = "Should Not Persist",
            AttendanceDepartmentId = 20
        }, fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("ValidationError", result.Error!.Code);

        var after = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.Id == fixture.Workers[0].Id);
        Assert.Equal(before.FullName, after.FullName);
        Assert.Equal(before.AttendanceDepartmentId, after.AttendanceDepartmentId);
        Assert.Empty(fixture.AuditEngine.Calls);
    }

    [Fact]
    public async Task SetEmploymentStatusAsync_sets_left_status_and_end_date()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var request = new SetWorkerEmploymentStatusRequest
        {
            EmploymentStatus = EmploymentStatus.LeftEmployment.ToString(),
            EmploymentEndDate = new DateTime(2025, 01, 01)
        };

        var result = await fixture.Service.SetEmploymentStatusAsync(fixture.Workers[0].Id, request, fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var updated = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.Id == fixture.Workers[0].Id);
        Assert.Equal(EmploymentStatus.LeftEmployment, updated.EmploymentStatus);
        Assert.False(updated.IsActive);
        Assert.Equal(request.EmploymentEndDate, updated.EmploymentEndDate);
        Assert.Single(fixture.AuditEngine.Calls);
    }

    [Fact]
    public async Task SetEmploymentStatusAsync_rejects_invalid_status()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var result = await fixture.Service.SetEmploymentStatusAsync(fixture.Workers[0].Id, new SetWorkerEmploymentStatusRequest { EmploymentStatus = "Unknown" }, fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("ValidationError", result.Error!.Code);
    }

    [Fact]
    public void UpdateWorkerRequest_does_not_expose_badge_number_in_v1()
    {
        var hasBadgeProperty = typeof(UpdateWorkerRequest).GetProperty("BadgeNumber") is not null;
        Assert.False(hasBadgeProperty);
    }

    [Fact]
    public void UpdateWorkerRequest_is_read_only_in_projection_terms()
    {
        var properties = typeof(UpdateWorkerRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains("FullName", properties);
        Assert.Contains("AttendanceDepartmentId", properties);
        Assert.DoesNotContain("BadgeNumber", properties);
    }

    private sealed class EmployeeMasterDataFixture : IAsyncDisposable
    {
        private EmployeeMasterDataFixture(
            AppDbContext dbContext,
            FakeAttendanceEmployeeWriter attendanceEmployeeWriter,
            FakeAttendanceDepartmentReader attendanceDepartmentReader,
            FakeAttendanceEmployeeReader attendanceEmployeeReader,
            RecordingAuditEngine auditEngine,
            IReadOnlyList<Worker> workers,
            Guid actorUserId)
        {
            DbContext = dbContext;
            AttendanceEmployeeWriter = attendanceEmployeeWriter;
            AttendanceDepartmentReader = attendanceDepartmentReader;
            AttendanceEmployeeReader = attendanceEmployeeReader;
            AuditEngine = auditEngine;
            Workers = workers;
            ActorUserId = actorUserId;
            Service = new EmployeeMasterDataService(dbContext, attendanceEmployeeWriter, attendanceEmployeeReader, attendanceDepartmentReader, auditEngine);
        }

        public AppDbContext DbContext { get; }
        public FakeAttendanceEmployeeWriter AttendanceEmployeeWriter { get; }
        public FakeAttendanceEmployeeReader AttendanceEmployeeReader { get; }
        public FakeAttendanceDepartmentReader AttendanceDepartmentReader { get; }
        public RecordingAuditEngine AuditEngine { get; }
        public IReadOnlyList<Worker> Workers { get; }
        public Guid ActorUserId { get; }
        public IEmployeeMasterDataService Service { get; }

        public static async Task<EmployeeMasterDataFixture> CreateAsync(
            IEnumerable<Worker>? workers = null,
            Func<string, string, CancellationToken, Task<Result>>? updateWorkerFullNameAsync = null,
            Func<string, int, CancellationToken, Task<Result>>? updateWorkerDepartmentAsync = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            var dbContext = new AppDbContext(options);

            var attendanceEmployeeData = new Dictionary<string, AttendanceEmployeeRecord?>
            {
                ["111"] = new AttendanceEmployeeRecord("111", 1, "1001", "Omar", true)
            };

            var attendanceDepartments = new Dictionary<int, AttendanceDepartmentRecord>
            {
                [1] = new AttendanceDepartmentRecord(1, "Operations"),
                [2] = new AttendanceDepartmentRecord(2, "Quality"),
                [10] = new AttendanceDepartmentRecord(10, "Packing"),
                [11] = new AttendanceDepartmentRecord(11, "QA"),
                [20] = new AttendanceDepartmentRecord(20, "Finished Goods")
            };

            var attendanceEmployeeWriter = new FakeAttendanceEmployeeWriter(
                attendanceEmployeeData,
                updateWorkerFullNameAsync,
                updateWorkerDepartmentAsync);
            var attendanceEmployeeReader = new FakeAttendanceEmployeeReader(attendanceEmployeeData);
            var attendanceDepartmentReader = new FakeAttendanceDepartmentReader(attendanceDepartments);

            var workerList = workers?.ToList() ?? [
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "EMP-001",
                    fullName: "Omar Hassan",
                    attendanceUserId: "111",
                    attendanceDepartmentId: 1,
                    isActive: true,
                    createdAtUtc: new DateTime(2024, 12, 01))
            ];

            dbContext.Workers.AddRange(workerList);
            await dbContext.SaveChangesAsync();

            var auditEngine = new RecordingAuditEngine();
            var actorUserId = Guid.NewGuid();

            return new EmployeeMasterDataFixture(
                dbContext,
                attendanceEmployeeWriter,
                attendanceDepartmentReader,
                attendanceEmployeeReader,
                auditEngine,
                workerList,
                actorUserId);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
