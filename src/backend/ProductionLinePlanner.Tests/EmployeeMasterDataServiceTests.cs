using Microsoft.EntityFrameworkCore;
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
    public async Task GetWorkersAsync_all_directory_filter_keeps_former_workers_readable()
    {
        var activeWorker = new Worker(Guid.NewGuid(), "W-001", "Active Worker");
        var formerWorker = new Worker(Guid.NewGuid(), "W-002", "Former Worker", isActive: false, employmentStatus: EmploymentStatus.LeftEmployment);
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync([activeWorker, formerWorker]);

        var allWorkers = await fixture.Service.GetWorkersAsync(null, null, page: 1, pageSize: 10);
        var activeWorkers = await fixture.Service.GetWorkersAsync(null, true, page: 1, pageSize: 10);
        var formerWorkers = await fixture.Service.GetWorkersAsync(null, false, page: 1, pageSize: 10);

        Assert.Equal(2, allWorkers.Value!.Length);
        Assert.Single(activeWorkers.Value!);
        Assert.Single(formerWorkers.Value!);
        Assert.Equal("Former Worker", formerWorkers.Value!.Single().FullName);
        Assert.Equal(EmploymentStatus.LeftEmployment.ToString(), formerWorkers.Value!.Single().EmploymentStatus);
    }

    [Fact]
    public async Task Worker_directory_exposes_photo_metadata_only_for_a_managed_cached_photo_reference()
    {
        var managedWorker = new Worker(Guid.NewGuid(), "119", "Worker 119");
        var photoVersion = new string('a', 64);
        managedWorker.SetPhotoReference($"/api/workers/{managedWorker.Id:D}/photo?v={photoVersion}", DateTime.UtcNow);
        var legacyManagedWorker = new Worker(Guid.NewGuid(), "121", "Legacy managed photo");
        var legacyPhotoVersion = new string('b', 16);
        legacyManagedWorker.SetPhotoReference($"/api/workers/{legacyManagedWorker.Id:D}/photo?v={legacyPhotoVersion}", DateTime.UtcNow);
        var legacyWorker = new Worker(Guid.NewGuid(), "120", "Legacy photo", photoReference: "legacy-photo.png");
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync([managedWorker, legacyManagedWorker, legacyWorker]);

        var result = await fixture.Service.GetWorkersAsync(null, null, page: 1, pageSize: 10);

        Assert.True(result.IsSuccess);
        var workers = result.Value!;
        var managed = workers.Single(worker => worker.Id == managedWorker.Id);
        var legacy = workers.Single(worker => worker.Id == legacyWorker.Id);
        Assert.True(managed.HasPhoto);
        Assert.Equal(photoVersion, managed.PhotoVersion);
        Assert.Equal($"/api/workers/{managedWorker.Id:D}/photo?v={photoVersion}", managed.PhotoReference);
        var legacyManaged = workers.Single(worker => worker.Id == legacyManagedWorker.Id);
        Assert.True(legacyManaged.HasPhoto);
        Assert.Equal(legacyPhotoVersion, legacyManaged.PhotoVersion);
        Assert.False(legacy.HasPhoto);
        Assert.Null(legacy.PhotoReference);
        Assert.Null(legacy.PhotoVersion);
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
    public async Task UpdateMasterIdentityAsync_updates_planner_owned_name_and_phone_locally()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var request = new UpdateWorkerRequest
        {
            FullName = "Ahmed Nasser",
            Phone = "0110001111"
        };

        var result = await fixture.Service.UpdateMasterIdentityAsync(fixture.Workers[0].Id, request, fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var updated = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.Id == fixture.Workers[0].Id);
        Assert.Equal("Ahmed Nasser", updated.FullName);
        Assert.Equal(1, updated.AttendanceDepartmentId);
        Assert.Equal("0110001111", updated.Phone);
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
    public async Task UpdateMasterIdentityAsync_rejects_source_observed_department_without_local_write()
    {
        await using var fixture = await EmployeeMasterDataFixture.CreateAsync();

        var before = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.Id == fixture.Workers[0].Id);

        var result = await fixture.Service.UpdateMasterIdentityAsync(fixture.Workers[0].Id, new UpdateWorkerRequest
        {
            AttendanceDepartmentId = 20
        }, fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("SourceObservedOnly", result.Error!.Code);

        var after = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.Id == fixture.Workers[0].Id);
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
            RecordingAuditEngine auditEngine,
            IReadOnlyList<Worker> workers,
            Guid actorUserId)
        {
            DbContext = dbContext;
            AuditEngine = auditEngine;
            Workers = workers;
            ActorUserId = actorUserId;
            Service = new EmployeeMasterDataService(dbContext, auditEngine);
        }

        public AppDbContext DbContext { get; }
        public RecordingAuditEngine AuditEngine { get; }
        public IReadOnlyList<Worker> Workers { get; }
        public Guid ActorUserId { get; }
        public IEmployeeMasterDataService Service { get; }

        public static async Task<EmployeeMasterDataFixture> CreateAsync(IEnumerable<Worker>? workers = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            var dbContext = new AppDbContext(options);

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
                auditEngine,
                workerList,
                actorUserId);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
