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
    public async Task Sync_does_not_overwrite_left_employment_status()
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
        Assert.Equal(EmploymentStatus.LeftEmployment, worker.EmploymentStatus);
        Assert.Equal(leftDate, worker.EmploymentEndDate);
        Assert.False(worker.IsActive);
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
            RecordingAuditEngine auditEngine,
            Guid actorUserId)
        {
            DbContext = dbContext;
            AttendanceReader = attendanceReader;
            AuditEngine = auditEngine;
            ActorUserId = actorUserId;
            Service = new WorkerInitialSyncService(dbContext, attendanceReader, auditEngine);
        }

        public AppDbContext DbContext { get; }
        public FakeAttendanceEmployeeReader AttendanceReader { get; }
        public RecordingAuditEngine AuditEngine { get; }
        public Guid ActorUserId { get; }
        public IWorkerInitialSyncService Service { get; }

        public static async Task<WorkerInitialSyncFixture> CreateAsync(
            AttendanceEmployeeRecord[]? sourceRows = null,
            IEnumerable<Worker>? localWorkers = null,
            Func<CancellationToken, Task<Result<AttendanceEmployeeRecord[]>>>? getAllAsync = null,
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
                getAllAsync: getAllAsync ?? (_ => Task.FromResult(Result<AttendanceEmployeeRecord[]>.Success(sourceRows ?? []))));

            return new WorkerInitialSyncFixture(
                context,
                attendanceReader,
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
