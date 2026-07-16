using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class WorkerInitialSyncServiceTests
{
    [Fact]
    public async Task Sync_creates_workers_when_local_table_is_empty()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [
                new AttendanceEmployeeRecord("1001", 1, "001", "Alice", true),
                new AttendanceEmployeeRecord("1002", 2, "002", "Bob", true),
                new AttendanceEmployeeRecord("1003", 3, "003", "Sara", true)
            ]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.SourceCount);
        Assert.Equal(3, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value!.UpdatedCount);
        Assert.Equal(0, result.Value!.UnchangedCount);
        Assert.Equal(0, result.Value!.WarningCount);
        Assert.Equal(3, await fixture.DbContext.Workers.CountAsync());
        Assert.Single(fixture.AuditEngine.Calls);
        Assert.Equal("WorkerInitialSync", fixture.AuditEngine.Calls.Single().ActionType.ToString());
    }

    [Fact]
    public async Task Sync_twice_does_not_create_duplicates_and_reports_unchanged()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 1, "001", "Alice", true)]);

        var first = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.CreatedCount);

        var second = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, second.Value!.UnchangedCount);
        Assert.Equal(0, second.Value!.UpdatedCount);
        Assert.Equal(1, await fixture.DbContext.Workers.CountAsync());
    }

    [Fact]
    public async Task Sync_updates_worker_when_name_changes_in_source()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 5, "001", "New Name", true)],
            [
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "001",
                    fullName: "Old Name",
                    attendanceUserId: "1001",
                    attendanceDepartmentId: 5)
            ]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();
        Assert.Equal("New Name", worker.FullName);
        Assert.Equal(1, result.Value!.UpdatedCount);
    }

    [Fact]
    public async Task Sync_updates_department_when_source_department_changes()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 7, "001", "Alice", true)],
            [
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "001",
                    fullName: "Alice",
                    attendanceUserId: "1001",
                    attendanceDepartmentId: 1)
            ]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();
        Assert.Equal(7, worker.AttendanceDepartmentId);
        Assert.Equal(1, result.Value!.UpdatedCount);
    }

    [Fact]
    public async Task Sync_reactivates_a_worker_when_the_authoritative_current_service_source_contains_them()
    {
        var leftDate = DateTime.UtcNow.AddDays(-2);
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 3, "001", "Alice", true)],
            [
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "001",
                    fullName: "Alice",
                    attendanceUserId: "1001",
                    attendanceDepartmentId: 3,
                    isActive: false,
                    employmentStatus: EmploymentStatus.LeftEmployment,
                    employmentEndDate: leftDate)
            ]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();
        Assert.Equal(EmploymentStatus.Active, worker.EmploymentStatus);
        Assert.Null(worker.EmploymentEndDate);
        Assert.True(worker.IsActive);
        Assert.Equal(1, result.Value!.ReactivatedCount);
    }

    [Fact]
    public async Task Sync_preserves_photo_reference()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 3, "001", "Alice", true)],
            [
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "001",
                    fullName: "Alice",
                    attendanceUserId: "1001",
                    attendanceDepartmentId: 3,
                    photoReference: "photo.png")
            ]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();
        Assert.Equal("photo.png", worker.PhotoReference);
    }

    [Fact]
    public async Task Sync_records_a_managed_photo_reference_for_a_valid_zktime_bmp_and_is_idempotent()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 3, "001", "Alice", true)],
            sourcePhotos: [new AttendanceWorkerPhotoRecord("1001", CreateBmpPhoto())]);

        var first = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();
        var second = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.PhotosFoundCount);
        Assert.Equal(1, first.Value.PhotosSynchronizedCount);
        Assert.Equal(1, first.Value.PhotosCreatedCount);
        Assert.StartsWith($"/api/workers/{worker.Id:D}/photo?v=", worker.PhotoReference);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, second.Value!.PhotosUnchangedCount);
        Assert.Equal(0, second.Value.PhotosSynchronizedCount);
        Assert.Equal(2, fixture.PhotoCache.StoreCalls);
    }

    [Fact]
    public async Task Sync_populates_worker_119_cached_photo_by_attendance_user_identity_when_the_existing_reference_is_null()
    {
        var worker119 = new Worker(
            id: Guid.NewGuid(),
            employeeCode: "119",
            fullName: "Worker 119",
            attendanceUserId: "1",
            badgeNumber: "119",
            photoReference: null);
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1", 3, "119", "Worker 119", true)],
            [worker119],
            sourcePhotos: [new AttendanceWorkerPhotoRecord("1", CreateBmpPhoto())]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        var synchronized = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(worker => worker.Id == worker119.Id);
        var cached = await fixture.PhotoCache.GetAsync(worker119.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PhotosFoundCount);
        Assert.Equal(1, result.Value.PhotosCreatedCount);
        Assert.StartsWith($"/api/workers/{worker119.Id:D}/photo?v=", synchronized.PhotoReference);
        Assert.NotNull(cached);
        Assert.Equal("image/bmp", cached!.ContentType);
        Assert.Equal(CreateBmpPhoto(), cached.Content);
    }

    [Fact]
    public async Task Sync_rejects_invalid_photo_without_breaking_worker_synchronization()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 3, "001", "Alice", true)],
            sourcePhotos: [new AttendanceWorkerPhotoRecord("1001", [0x00, 0x01])]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.InvalidOrUnsupportedPhotosCount);
        Assert.Equal(1, result.Value.WorkersWithoutPhotosCount);
        Assert.Null(worker.PhotoReference);
    }

    [Fact]
    public async Task Sync_updates_changed_cached_photo_and_clears_managed_reference_when_source_photo_disappears()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1", 3, "119", "Worker 119", true)],
            sourcePhotos: [new AttendanceWorkerPhotoRecord("1", CreateBmpPhoto())]);

        var first = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();
        var firstReference = worker.PhotoReference;

        var changed = CreateBmpPhoto();
        changed[^1] = 0x7F;
        fixture.AttendanceReader.Photos[0] = new AttendanceWorkerPhotoRecord("1", changed);
        var changedResult = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();
        var changedReference = worker.PhotoReference;

        fixture.AttendanceReader.Photos.Clear();
        var missingResult = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();

        Assert.True(first.IsSuccess);
        Assert.True(changedResult.IsSuccess);
        Assert.Equal(1, changedResult.Value!.PhotosUpdatedCount);
        Assert.NotEqual(firstReference, changedReference);
        Assert.True(missingResult.IsSuccess);
        Assert.Equal(1, missingResult.Value!.WorkersWithoutPhotosCount);
        Assert.Null(worker.PhotoReference);
        Assert.Equal(1, fixture.PhotoCache.RemoveCalls);
    }

    [Fact]
    public async Task Sync_flags_local_workers_missing_from_source()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 3, "001", "Alice", true)],
            [
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "001",
                    fullName: "Alice",
                    attendanceUserId: "1001"),
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "002",
                    fullName: "Missing",
                    attendanceUserId: "2002")
            ]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.MissingFromSourceCount);
        Assert.Equal(1, result.Value.MarkedInactiveCount);
        var missingWorker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.EmployeeCode == "002");
        Assert.False(missingWorker.IsActive);
        Assert.Equal(EmploymentStatus.LeftEmployment, missingWorker.EmploymentStatus);
    }

    [Fact]
    public async Task Preview_reports_only_current_service_workers_and_never_plans_physical_deletion()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 1, "001", "Alice", true)],
            [
                new Worker(Guid.NewGuid(), "001", "Alice", attendanceUserId: "1001"),
                new Worker(Guid.NewGuid(), "002", "Former", attendanceUserId: "1002")
            ]);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();

        Assert.True(preview.IsSuccess);
        Assert.Equal(2, preview.Value!.CurrentLocalWorkers);
        Assert.Equal(1, preview.Value.ActiveOnServiceWorkersInZkTime);
        Assert.Equal(1, preview.Value.WorkersToRemainActive);
        Assert.Equal(1, preview.Value.WorkersToMarkInactiveOrExcluded);
        Assert.Equal(0, preview.Value.WorkersSafelyRemovable);
    }

    [Fact]
    public async Task Sync_excludes_former_worker_without_deleting_historical_participant_snapshot()
    {
        var formerWorker = new Worker(Guid.NewGuid(), "002", "Former", attendanceUserId: "1002");
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync([], [formerWorker]);
        var allocation = new StageProductionWorkerAllocation(
            Guid.NewGuid(), formerWorker.Id, "002", "Historical snapshot", 100m, null, null);
        fixture.DbContext.Set<StageProductionWorkerAllocation>().Add(allocation);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync(x => x.Id == formerWorker.Id);
        Assert.False(worker.IsActive);
        Assert.Equal(EmploymentStatus.LeftEmployment, worker.EmploymentStatus);
        var historicalAllocation = await fixture.DbContext.Set<StageProductionWorkerAllocation>().AsNoTracking().SingleAsync();
        Assert.Equal("Historical snapshot", historicalAllocation.SnapshotWorkerName);
        Assert.Equal(formerWorker.Id, historicalAllocation.WorkerId);
    }

    [Fact]
    public async Task Sync_treats_an_inactive_source_row_as_excluded_from_active_service()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1002", 1, "002", "Former", false)],
            [new Worker(Guid.NewGuid(), "002", "Former", attendanceUserId: "1002")]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.SourceCount);
        Assert.Equal(1, result.Value.MarkedInactiveCount);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();
        Assert.False(worker.IsActive);
        Assert.Equal(EmploymentStatus.LeftEmployment, worker.EmploymentStatus);
    }

    [Fact]
    public async Task Sync_skips_numeric_only_source_name_and_logs_warning()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 1, "001", "12345", true)],
            [
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "001",
                    fullName: "Original Name",
                    attendanceUserId: "1001",
                    attendanceDepartmentId: 1)
            ]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var worker = await fixture.DbContext.Workers.AsNoTracking().SingleAsync();
        Assert.Equal("Original Name", worker.FullName);
        Assert.Equal(1, result.Value!.WarningCount);
        Assert.Equal(1, result.Value!.UpdatedCount);
    }

    [Fact]
    public async Task Sync_skips_duplicate_attendance_users_in_source()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            sourceRows: [],
            localWorkers: null,
            getAllAsync: _ => Task.FromResult(Result<AttendanceEmployeeRecord[]>.Success(
                [
                    new AttendanceEmployeeRecord("1001", 3, "001", "Alice", true),
                    new AttendanceEmployeeRecord("1001", 3, "001", "Another", true)
                ]))
        );

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value!.WarningCount);
        Assert.Equal(1, await fixture.DbContext.Workers.CountAsync());
    }

    [Fact]
    public async Task Sync_fails_without_changes_when_source_read_fails()
    {
        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            sourceRows: [],
            localWorkers:
            [
                new Worker(
                    id: Guid.NewGuid(),
                    employeeCode: "001",
                    fullName: "Existing",
                    attendanceUserId: "1001")
            ],
            getAllAsync: _ => Task.FromResult(Result<AttendanceEmployeeRecord[]>.Failure(new Error("AttendanceSourceError", "Source unavailable")))
        );

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("AttendanceSourceError", result.Error!.Code);
        Assert.Equal(1, await fixture.DbContext.Workers.CountAsync());
        Assert.Empty(fixture.AuditEngine.Calls);
    }

    [Fact]
    public async Task Sync_fails_and_rolls_back_when_save_changes_fails()
    {
        var interceptor = new ThrowingSaveChangesInterceptor
        {
            ThrowOnSave = true
        };

        await using var fixture = await WorkerInitialSyncFixture.CreateAsync(
            [new AttendanceEmployeeRecord("1001", 3, "001", "Alice", true)],
            localWorkers: null,
            interceptors: [interceptor]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("WorkerInitialSyncFailed", result.Error!.Code);
        Assert.Equal(0, await fixture.DbContext.Workers.CountAsync());
        Assert.Empty(fixture.AuditEngine.Calls);
    }

    private sealed class WorkerInitialSyncFixture : IAsyncDisposable
    {
        private WorkerInitialSyncFixture(
            AppDbContext dbContext,
            FakeAttendanceEmployeeReader attendanceReader,
            InMemoryWorkerPhotoCache photoCache,
            RecordingAuditEngine auditEngine,
            Guid actorUserId)
        {
            DbContext = dbContext;
            AttendanceReader = attendanceReader;
            PhotoCache = photoCache;
            AuditEngine = auditEngine;
            ActorUserId = actorUserId;
            Service = new WorkerInitialSyncService(dbContext, attendanceReader, attendanceReader, photoCache, auditEngine);
        }

        public AppDbContext DbContext { get; }
        public FakeAttendanceEmployeeReader AttendanceReader { get; }
        public InMemoryWorkerPhotoCache PhotoCache { get; }
        public RecordingAuditEngine AuditEngine { get; }
        public Guid ActorUserId { get; }
        public IWorkerInitialSyncService Service { get; }

        public static async Task<WorkerInitialSyncFixture> CreateAsync(
            AttendanceEmployeeRecord[]? sourceRows = null,
            IEnumerable<Worker>? localWorkers = null,
            Func<CancellationToken, Task<Result<AttendanceEmployeeRecord[]>>>? getAllAsync = null,
            AttendanceWorkerPhotoRecord[]? sourcePhotos = null,
            params IInterceptor[] interceptors)
        {
            var context = CreateContext(interceptors);

            var workersToSeed = localWorkers?.ToList() ?? [];
            foreach (var worker in workersToSeed)
            {
                context.Workers.Add(worker);
            }

            if (workersToSeed.Count > 0)
            {
                await context.SaveChangesAsync();
            }

            Dictionary<string, AttendanceEmployeeRecord?> employeeRecords =
                sourceRows?.ToDictionary(
                    x => x.AttendanceUserId!,
                    x => (AttendanceEmployeeRecord?)x,
                    StringComparer.OrdinalIgnoreCase) ?? [];

            var attendanceReader = new FakeAttendanceEmployeeReader(
                employeeRecords,
                getAllAsync: getAllAsync ?? (_ => Task.FromResult(Result<AttendanceEmployeeRecord[]>.Success(sourceRows ?? []))),
                photos: sourcePhotos);

            return new WorkerInitialSyncFixture(
                context,
                attendanceReader,
                new InMemoryWorkerPhotoCache(),
                new RecordingAuditEngine(),
                Guid.NewGuid());
        }

        public ValueTask DisposeAsync()
        {
            return DbContext.DisposeAsync();
        }

        private static AppDbContext CreateContext(params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));

            foreach (var interceptor in interceptors)
            {
                builder.AddInterceptors(interceptor);
            }

            return new AppDbContext(builder.Options);
        }
    }

    private static byte[] CreateBmpPhoto()
    {
        var photo = new byte[54];
        photo[0] = 0x42;
        photo[1] = 0x4D;
        return photo;
    }

    private sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public bool ThrowOnSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("Persistence failed.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
