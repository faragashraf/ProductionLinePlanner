using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class WorkerInitialSyncServiceTests
{
    [Fact]
    public async Task New_worker_only_can_initialize_local_name_from_source()
    {
        await using var fixture = await Fixture.CreateAsync([Source(name: "الاسم الأول")]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var worker = await fixture.Db.Workers.AsNoTracking().SingleAsync();
        Assert.Equal("الاسم الأول", worker.FullName);
        Assert.Equal("001", worker.EmployeeCode);
        Assert.Equal("1001", worker.AttendanceUserId);
        Assert.Equal("001", worker.BadgeNumber);
        Assert.Null(worker.AttendanceDepartmentId);
    }

    [Fact]
    public async Task Subsequent_sync_protects_local_name_photo_salary_assignments_and_history()
    {
        var worker = LocalWorker(fullName: "الاسم العربي المحلي", photoReference: "local-photo.png");
        await using var fixture = await Fixture.CreateAsync(
            [Source(name: "External Replacement", departmentId: 99, employmentStatus: "LeftEmployment", department: "External", shift: "Night")],
            [worker]);
        var salary = new WorkerSalaryHistory(Guid.NewGuid(), worker.Id, 9000m, "EGP", DateTime.UtcNow.AddMonths(-1));
        var assignment = new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(-5), productionLineId: Guid.NewGuid());
        var history = new StageProductionWorkerAllocation(Guid.NewGuid(), worker.Id, worker.EmployeeCode, "Historical local name", 100m, null, "history");
        fixture.Db.WorkerSalaryHistories.Add(salary);
        fixture.Db.WorkerDefaultAssignments.Add(assignment);
        fixture.Db.Set<StageProductionWorkerAllocation>().Add(history);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var persisted = await fixture.Db.Workers.AsNoTracking().SingleAsync();
        Assert.Equal("الاسم العربي المحلي", persisted.FullName);
        Assert.Equal("local-photo.png", persisted.PhotoReference);
        Assert.Equal(1, persisted.AttendanceDepartmentId);
        Assert.True(persisted.IsActive);
        Assert.Equal(EmploymentStatus.Active, persisted.EmploymentStatus);
        Assert.Equal(9000m, (await fixture.Db.WorkerSalaryHistories.AsNoTracking().SingleAsync()).Amount);
        Assert.True((await fixture.Db.WorkerDefaultAssignments.AsNoTracking().SingleAsync()).IsActive);
        Assert.Equal("Historical local name", (await fixture.Db.Set<StageProductionWorkerAllocation>().AsNoTracking().SingleAsync()).SnapshotWorkerName);
    }

    [Fact]
    public async Task Existing_worker_reconciles_changed_badge_without_overwriting_planner_owned_fields()
    {
        var worker = LocalWorker(badgeNumber: "OLD", photoReference: "local-photo.png");
        await using var fixture = await Fixture.CreateAsync([Source(badge: "NEW", employeeCode: "001")], [worker]);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();
        var sync = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(preview.IsSuccess);
        var update = Assert.Single(preview.Value!.Rows, row => row.Action == WorkerSyncActions.ExistingWorkerUpdated);
        Assert.Empty(update.IdentityConflicts);
        Assert.True(sync.IsSuccess);
        Assert.Equal(1, sync.Value!.UpdatedCount);
        var persisted = await fixture.Db.Workers.AsNoTracking().SingleAsync();
        Assert.Equal("NEW", persisted.BadgeNumber);
        Assert.Equal("Local Name", persisted.FullName);
        Assert.Equal("local-photo.png", persisted.PhotoReference);
    }

    [Fact]
    public async Task Employee_code_remains_planner_owned_when_attendance_identity_is_reconciled()
    {
        var worker = LocalWorker(employeeCode: "LOCAL", badgeNumber: "001");
        await using var fixture = await Fixture.CreateAsync([Source(employeeCode: "SOURCE")], [worker]);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();
        var sync = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.Contains(preview.Value!.Rows, row => row.Action == WorkerSyncActions.ExistingWorkerUpdated);
        Assert.True(sync.IsSuccess);
        Assert.Equal("LOCAL", (await fixture.Db.Workers.AsNoTracking().SingleAsync()).EmployeeCode);
    }

    [Fact]
    public async Task Badge_number_is_used_when_attendance_user_id_has_not_been_linked_yet()
    {
        var worker = LocalWorker(employeeCode: "LOCAL", attendanceUserId: "OLD", badgeNumber: "2429");
        await using var fixture = await Fixture.CreateAsync(
            [Source(attendanceUserId: "17252", badge: "2429", employeeCode: "SOURCE")],
            [worker]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.UpdatedCount);
        var persisted = await fixture.Db.Workers.AsNoTracking().SingleAsync();
        Assert.Equal(worker.Id, persisted.Id);
        Assert.Equal("17252", persisted.AttendanceUserId);
        Assert.Equal("2429", persisted.BadgeNumber);
        Assert.Equal("LOCAL", persisted.EmployeeCode);
    }

    [Fact]
    public async Task Employee_code_is_used_only_after_attendance_user_id_and_badge_do_not_match()
    {
        var worker = LocalWorker(employeeCode: "2429", attendanceUserId: "OLD", badgeNumber: "OLD-BADGE");
        await using var fixture = await Fixture.CreateAsync(
            [Source(attendanceUserId: "17252", badge: "2429", employeeCode: "2429")],
            [worker]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.UpdatedCount);
        var persisted = await fixture.Db.Workers.AsNoTracking().SingleAsync();
        Assert.Equal(worker.Id, persisted.Id);
        Assert.Equal("17252", persisted.AttendanceUserId);
        Assert.Equal("2429", persisted.BadgeNumber);
        Assert.Equal("2429", persisted.EmployeeCode);
    }

    [Fact]
    public async Task Missing_from_current_employees_does_not_change_employment_status()
    {
        var worker = LocalWorker();
        await using var fixture = await Fixture.CreateAsync([], [worker]);

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.MissingFromSourceCount);
        Assert.Equal(0, result.Value.MarkedInactiveCount);
        var persisted = await fixture.Db.Workers.AsNoTracking().SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal(EmploymentStatus.Active, persisted.EmploymentStatus);
    }

    [Theory]
    [InlineData(1, true, EmploymentStatus.Active, true)]
    [InlineData(4, true, EmploymentStatus.Active, true)]
    [InlineData(2, false, EmploymentStatus.LeftEmployment, false)]
    [InlineData(null, false, EmploymentStatus.LeftEmployment, false)]
    public void Staged_default_department_classification_initializes_the_domain_status(
        int? sourceDefaultDepartmentId,
        bool isCurrentWorker,
        EmploymentStatus expectedStatus,
        bool expectedIsActive)
    {
        var policy = new WorkerSyncPolicy();
        var source = Source(
            departmentId: sourceDefaultDepartmentId,
            sourceDefaultDepartmentId: sourceDefaultDepartmentId,
            isCurrentWorker: isCurrentWorker);

        var result = policy.CreateNewWorker(source, new DateTime(2026, 7, 28, 8, 0, 0, DateTimeKind.Utc));

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedStatus, result.Value!.EmploymentStatus);
        Assert.Equal(expectedIsActive, result.Value.IsActive);
        Assert.Null(result.Value.EmploymentEndDate);
    }

    [Fact]
    public async Task Staged_worker_transition_is_idempotent_preserves_local_profile_and_reactivates_safely()
    {
        var worker = LocalWorker(fullName: "الاسم العربي المحلي", photoReference: "local-photo.png");
        var sourceRows = new[]
        {
            Source(departmentId: 2, sourceDefaultDepartmentId: 2, isCurrentWorker: false)
        };
        await using var fixture = await Fixture.CreateAsync(
            workers: [worker],
            getAllAsync: _ => Task.FromResult(Result<AttendanceEmployeeRecord[]>.Success(sourceRows)));

        var departure = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        var afterDeparture = await fixture.Db.Workers.AsNoTracking().SingleAsync();
        var firstEmploymentEndDate = afterDeparture.EmploymentEndDate;
        var firstExternalSyncAt = afterDeparture.LastExternalSyncAt;

        Assert.True(departure.IsSuccess);
        Assert.Equal(1, departure.Value!.MarkedInactiveCount);
        Assert.False(afterDeparture.IsActive);
        Assert.Equal(EmploymentStatus.LeftEmployment, afterDeparture.EmploymentStatus);
        Assert.NotNull(firstEmploymentEndDate);
        Assert.Equal("الاسم العربي المحلي", afterDeparture.FullName);
        Assert.Equal("local-photo.png", afterDeparture.PhotoReference);

        var replay = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        var afterReplay = await fixture.Db.Workers.AsNoTracking().SingleAsync();

        Assert.True(replay.IsSuccess);
        Assert.Equal(0, replay.Value!.MarkedInactiveCount);
        Assert.Equal(0, replay.Value.UpdatedCount);
        Assert.Equal(firstEmploymentEndDate, afterReplay.EmploymentEndDate);
        Assert.Equal(firstExternalSyncAt, afterReplay.LastExternalSyncAt);

        sourceRows[0] = Source(
            departmentId: 1,
            sourceDefaultDepartmentId: 1,
            isCurrentWorker: true);
        var reactivation = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);
        var afterReactivation = await fixture.Db.Workers.AsNoTracking().SingleAsync();

        Assert.True(reactivation.IsSuccess);
        Assert.Equal(1, reactivation.Value!.ReactivatedCount);
        Assert.True(afterReactivation.IsActive);
        Assert.Equal(EmploymentStatus.Active, afterReactivation.EmploymentStatus);
        Assert.Null(afterReactivation.EmploymentEndDate);
        Assert.Equal("الاسم العربي المحلي", afterReactivation.FullName);
        Assert.Equal("local-photo.png", afterReactivation.PhotoReference);
    }

    [Fact]
    public async Task Employment_department_and_shift_are_source_observed_only()
    {
        var worker = LocalWorker();
        await using var fixture = await Fixture.CreateAsync(
            [Source(departmentId: 88, employmentStatus: "Suspended", department: "External QA", shift: "B")],
            [worker]);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();
        var row = Assert.Single(preview.Value!.Rows);
        var sync = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.Equal("Suspended", row.SourceObservedEmploymentStatus);
        Assert.Equal(88, row.SourceObservedDepartmentId);
        Assert.Equal("External QA", row.SourceObservedDepartment);
        Assert.Equal("B", row.SourceObservedShift);
        Assert.True(sync.IsSuccess);
        var persisted = await fixture.Db.Workers.AsNoTracking().SingleAsync();
        Assert.Equal(EmploymentStatus.Active, persisted.EmploymentStatus);
        Assert.Equal(1, persisted.AttendanceDepartmentId);
    }

    [Fact]
    public async Task Preview_is_read_only_and_does_not_invoke_photo_or_attendance_capabilities()
    {
        await using var fixture = await Fixture.CreateAsync([Source()]);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();

        Assert.True(preview.IsSuccess);
        Assert.True(preview.Value!.IsReadOnly);
        Assert.False(preview.Value.CanApply);
        Assert.Equal(0, await fixture.Db.Workers.CountAsync());
        Assert.Equal(0, await fixture.Db.AttendanceRecords.CountAsync());
        Assert.Empty(fixture.Audit.Calls);
        Assert.Equal(1, fixture.Reader.GetAllCalls);
        Assert.Equal(0, fixture.Reader.GetByIdCalls);
        var constructorTypes = typeof(WorkerInitialSyncService).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Assert.DoesNotContain(typeof(IAttendanceWorkerPhotoReader), constructorTypes);
        Assert.DoesNotContain(typeof(IAttendanceSyncService), constructorTypes);
    }

    [Fact]
    public async Task Preview_sql_excludes_userinfo_photo_and_checkinout()
    {
        var sourceOptions = Options.Create(new AttendanceSourceOptions());
        await using var sourceConnection = new SqliteConnection("Data Source=:memory:");
        await sourceConnection.OpenAsync();
        var commands = new CommandCaptureInterceptor();
        await using var attendanceDb = new AttendanceDbContext(
            new DbContextOptionsBuilder<AttendanceDbContext>()
                .UseSqlite(sourceConnection)
                .AddInterceptors(commands)
                .Options,
            sourceOptions);
        await attendanceDb.Database.ExecuteSqlRawAsync(
            "CREATE TABLE USERINFO (USERID INTEGER NULL, BADGENUMBER TEXT NULL, Name TEXT NULL, DEFAULTDEPTID INTEGER NULL, PHOTO BLOB NULL);");
        await attendanceDb.Database.ExecuteSqlRawAsync(
            "CREATE TABLE CurrentEmployeesImport (EmployeeCode TEXT NULL);");
        await attendanceDb.Database.ExecuteSqlRawAsync(
            "INSERT INTO USERINFO (USERID, BADGENUMBER, Name, DEFAULTDEPTID, PHOTO) VALUES (1001, '001', 'Source', 7, X'010203');");
        await attendanceDb.Database.ExecuteSqlRawAsync(
            "INSERT INTO CurrentEmployeesImport (EmployeeCode) VALUES ('001');");

        await using var appDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var service = new WorkerInitialSyncService(
            appDb,
            new AttendanceDirectoryService(attendanceDb),
            new WorkerSyncPolicy(),
            new AuthoritativeWorkerSnapshotValidator(),
            new RecordingAuditEngine(),
            NullLogger<WorkerInitialSyncService>.Instance);

        var preview = await service.PreviewActiveServiceSyncAsync();

        Assert.True(preview.IsSuccess);
        var userInfoSelect = Assert.Single(
            commands.Commands,
            command => command.Contains("FROM \"USERINFO\"", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("PHOTO", userInfoSelect, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            commands.Commands,
            command => command.Contains("CHECKINOUT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, await appDb.Workers.CountAsync());
        Assert.Equal(0, await appDb.AttendanceRecords.CountAsync());
    }

    [Fact]
    public async Task Userinfo_worker_is_a_create_candidate_even_when_missing_from_current_employees_import()
    {
        var sourceOptions = Options.Create(new AttendanceSourceOptions());
        await using var sourceConnection = new SqliteConnection("Data Source=:memory:");
        await sourceConnection.OpenAsync();
        await using var attendanceDb = new AttendanceDbContext(
            new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlite(sourceConnection).Options,
            sourceOptions);
        await attendanceDb.Database.ExecuteSqlRawAsync(
            "CREATE TABLE USERINFO (USERID INTEGER NULL, BADGENUMBER TEXT NULL, Name TEXT NULL, DEFAULTDEPTID INTEGER NULL, PHOTO BLOB NULL);");
        await attendanceDb.Database.ExecuteSqlRawAsync(
            "CREATE TABLE CurrentEmployeesImport (EmployeeCode TEXT NULL);");
        await attendanceDb.Database.ExecuteSqlRawAsync(
            "INSERT INTO USERINFO (USERID, BADGENUMBER, Name, DEFAULTDEPTID) VALUES (17252, '2429', 'ZK worker', 1);");

        var reader = new AttendanceDirectoryService(attendanceDb);
        var source = await reader.GetAllAsync();

        Assert.True(source.IsSuccess);
        var worker = Assert.Single(source.Value!);
        Assert.Equal("17252", worker.AttendanceUserId);
        Assert.Equal("2429", worker.BadgeNumber);
        Assert.False(worker.IsActive);
    }

    [Fact]
    public async Task Empty_snapshot_is_uncertain_and_never_authoritative_for_absence()
    {
        await using var fixture = await Fixture.CreateAsync([], [LocalWorker()]);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();

        Assert.Contains("EmptySourceSnapshot", preview.Value!.SnapshotIssues);
        Assert.Contains("AbsenceNotAuthoritative", preview.Value.SnapshotIssues);
        Assert.Equal(0, preview.Value.WorkersToMarkInactiveOrExcluded);
    }

    [Fact]
    public async Task Duplicate_source_identities_are_explicit_conflicts()
    {
        await using var fixture = await Fixture.CreateAsync(
            [Source(name: "One"), Source(name: "Two")]);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();

        Assert.Equal(2, preview.Value!.IdentityConflictCount);
        Assert.All(preview.Value.Rows, row => Assert.Equal(WorkerSyncActions.IdentityConflict, row.Action));
        Assert.Equal(0, await fixture.Db.Workers.CountAsync());
    }

    [Fact]
    public async Task Duplicate_source_identity_preview_is_deterministic_regardless_of_reader_order()
    {
        var first = Source(name: "Zulu");
        var second = Source(name: "Alpha");
        await using var forward = await Fixture.CreateAsync([first, second]);
        await using var reverse = await Fixture.CreateAsync([second, first]);

        var forwardPreview = await forward.Service.PreviewActiveServiceSyncAsync();
        var reversePreview = await reverse.Service.PreviewActiveServiceSyncAsync();

        Assert.Equal(
            forwardPreview.Value!.Rows.Select(RowSignature),
            reversePreview.Value!.Rows.Select(RowSignature));
        Assert.All(forwardPreview.Value.Rows, row => Assert.Equal(WorkerSyncActions.IdentityConflict, row.Action));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Null_or_invalid_badge_is_unsupported(string? badge)
    {
        await using var fixture = await Fixture.CreateAsync([Source(badge: badge)]);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();

        Assert.Equal(1, preview.Value!.UnsupportedSourceStateCount);
        Assert.Equal(WorkerSyncActions.UnsupportedSourceState, Assert.Single(preview.Value.Rows).Action);
    }

    [Fact]
    public void Snapshot_validator_rejects_untrusted_snapshot()
    {
        var validator = new AuthoritativeWorkerSnapshotValidator();
        var snapshot = new WorkerSourceSnapshot([Source()]);

        var result = validator.ValidateAuthoritativeApplication(snapshot);

        Assert.True(result.IsFailure);
        Assert.Equal("UntrustedWorkerSnapshot", result.Error!.Code);
    }

    [Fact]
    public void Worker_sync_policy_declares_initialization_protection_and_source_observation_boundaries()
    {
        var policy = new WorkerSyncPolicy();

        Assert.Equal(
            [nameof(Worker.EmployeeCode), nameof(Worker.FullName), nameof(Worker.AttendanceUserId), nameof(Worker.BadgeNumber)],
            policy.InitializableFields);
        Assert.Contains("ArabicName", policy.ProtectedLocalFields);
        Assert.Contains("LocalDisplayName", policy.ProtectedLocalFields);
        Assert.Contains(nameof(Worker.PhotoReference), policy.ProtectedLocalFields);
        Assert.Contains("Salary", policy.ProtectedLocalFields);
        Assert.Contains("Assignments", policy.ProtectedLocalFields);
        Assert.Contains("Factory", policy.ProtectedLocalFields);
        Assert.Contains("ProductionLine", policy.ProtectedLocalFields);
        Assert.Contains("Stages", policy.ProtectedLocalFields);
        Assert.Contains("HistoricalLocalData", policy.ProtectedLocalFields);
        Assert.Equal([nameof(Worker.EmploymentStatus), "Department", "Shift"], policy.SourceObservedOnlyFields);
    }

    [Fact]
    public async Task Preview_propagates_caller_cancellation_without_writes()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var fixture = await Fixture.CreateAsync(
            getAllAsync: token => Task.FromCanceled<Result<AttendanceEmployeeRecord[]>>(token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.PreviewActiveServiceSyncAsync(cancellation.Token));
        Assert.Equal(0, await fixture.Db.Workers.CountAsync());
    }

    [Fact]
    public async Task Preview_classifies_source_exception_and_preserves_local_state()
    {
        await using var fixture = await Fixture.CreateAsync(
            workers: [LocalWorker()],
            getAllAsync: _ => throw new InvalidOperationException("source unavailable"));

        var result = await fixture.Service.PreviewActiveServiceSyncAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("AttendanceSourceError", result.Error!.Code);
        Assert.Equal(1, await fixture.Db.Workers.CountAsync());
    }

    [Fact]
    public async Task Worker_sync_policy_is_used_by_the_execution_path()
    {
        var trackingPolicy = new TrackingWorkerSyncPolicy();
        await using var fixture = await Fixture.CreateAsync([Source()], [LocalWorker()], policy: trackingPolicy);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();

        Assert.True(preview.IsSuccess);
        Assert.Equal(1, trackingPolicy.EvaluateExistingCalls);
        Assert.Equal(1, fixture.Reader.GetAllCalls);
    }

    [Fact]
    public async Task Snapshot_validator_is_used_by_the_execution_path()
    {
        var validator = new TrackingSnapshotValidator();
        await using var fixture = await Fixture.CreateAsync([Source()], snapshotValidator: validator);

        var preview = await fixture.Service.PreviewActiveServiceSyncAsync();

        Assert.True(preview.IsSuccess);
        Assert.Equal(1, validator.InspectCalls);
        Assert.Contains("SnapshotCompletenessUnproven", preview.Value!.SnapshotIssues);
    }

    [Fact]
    public async Task Sync_rolls_back_new_worker_when_local_persistence_fails()
    {
        var interceptor = new ThrowingSaveChangesInterceptor();
        await using var fixture = await Fixture.CreateAsync([Source()], saveChangesInterceptor: interceptor);
        interceptor.ThrowOnSave = true;

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("WorkerInitialSyncFailed", result.Error!.Code);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(0, await fixture.Db.Workers.CountAsync());
    }

    [Fact]
    public async Task Worker_master_sync_does_not_modify_attendance_records()
    {
        var worker = LocalWorker();
        await using var fixture = await Fixture.CreateAsync([Source(name: "Different")], [worker]);
        var attendance = new AttendanceRecord(Guid.NewGuid(), worker.Id, DateTime.UtcNow.AddHours(-1), AttendanceStatus.Present, "test", sourceRawId: "raw");
        fixture.Db.AttendanceRecords.Add(attendance);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.SyncWorkersAsync(fixture.ActorUserId);

        Assert.True(result.IsSuccess);
        var persisted = await fixture.Db.AttendanceRecords.AsNoTracking().SingleAsync();
        Assert.Equal(AttendanceStatus.Present, persisted.AttendanceStatus);
        Assert.Equal("raw", persisted.SourceRawId);
    }

    [Fact]
    public void Attendance_directory_and_registration_expose_no_external_writer_contract()
    {
        var applicationAssembly = typeof(AttendanceEmployeeRecord).Assembly;
        Assert.Null(applicationAssembly.GetType("ProductionLinePlanner.Application.Abstractions.IAttendanceEmployeeWriter"));
        Assert.Null(applicationAssembly.GetType("ProductionLinePlanner.Application.Abstractions.IAttendanceDepartmentWriter"));
        Assert.DoesNotContain(
            typeof(AttendanceDirectoryService).GetInterfaces(),
            contract => contract.Name.EndsWith("Writer", StringComparison.Ordinal));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AppDatabase"] = "Server=localhost;Database=Planner;User Id=test;Password=Test_password1;TrustServerCertificate=true",
                ["ConnectionStrings:AttendanceDatabase"] = "Server=localhost;Database=Attendance;User Id=test;Password=Test_password1;TrustServerCertificate=true"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Name.Contains("Attendance", StringComparison.Ordinal) &&
                          descriptor.ServiceType.Name.EndsWith("Writer", StringComparison.Ordinal));
    }

    private static AttendanceEmployeeRecord Source(
        string attendanceUserId = "1001",
        int? departmentId = 7,
        string? badge = "001",
        string name = "Source Name",
        bool isActive = true,
        string? employeeCode = "001",
        string? employmentStatus = null,
        string? department = null,
        string? shift = null,
        int? sourceDefaultDepartmentId = null,
        bool? isCurrentWorker = null) =>
        new(
            attendanceUserId,
            departmentId,
            badge,
            name,
            isActive,
            employeeCode,
            employmentStatus,
            department,
            shift,
            sourceDefaultDepartmentId,
            isCurrentWorker);

    private static Worker LocalWorker(
        string employeeCode = "001",
        string fullName = "Local Name",
        string attendanceUserId = "1001",
        string badgeNumber = "001",
        string? photoReference = null) =>
        new(Guid.NewGuid(), employeeCode, fullName, attendanceUserId, badgeNumber, attendanceDepartmentId: 1, photoReference: photoReference);

    private static string RowSignature(ProductionLinePlanner.Application.DTOs.WorkerMasterSyncPreviewRowDto row) =>
        $"{row.SourceAttendanceUserId}|{row.SourceBadgeNumber}|{row.SourceEmployeeCode}|{row.SourceName}|{row.Action}|{string.Join(',', row.IdentityConflicts)}";

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            AppDbContext db,
            TrackingAttendanceReader reader,
            RecordingAuditEngine audit,
            IWorkerSyncPolicy policy,
            IAuthoritativeWorkerSnapshotValidator snapshotValidator)
        {
            Db = db;
            Reader = reader;
            Audit = audit;
            ActorUserId = Guid.NewGuid();
            Service = new WorkerInitialSyncService(db, reader, policy, snapshotValidator, audit, NullLogger<WorkerInitialSyncService>.Instance);
        }

        public AppDbContext Db { get; }
        public TrackingAttendanceReader Reader { get; }
        public RecordingAuditEngine Audit { get; }
        public Guid ActorUserId { get; }
        public IWorkerInitialSyncService Service { get; }

        public static async Task<Fixture> CreateAsync(
            AttendanceEmployeeRecord[]? sourceRows = null,
            IEnumerable<Worker>? workers = null,
            Func<CancellationToken, Task<Result<AttendanceEmployeeRecord[]>>>? getAllAsync = null,
            IWorkerSyncPolicy? policy = null,
            IAuthoritativeWorkerSnapshotValidator? snapshotValidator = null,
            SaveChangesInterceptor? saveChangesInterceptor = null)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            if (saveChangesInterceptor is not null)
            {
                optionsBuilder.AddInterceptors(saveChangesInterceptor);
            }

            var db = new AppDbContext(optionsBuilder.Options);
            db.Workers.AddRange(workers ?? []);
            await db.SaveChangesAsync();
            var reader = new TrackingAttendanceReader(getAllAsync ?? (_ => Task.FromResult(Result<AttendanceEmployeeRecord[]>.Success(sourceRows ?? []))));
            return new Fixture(
                db,
                reader,
                new RecordingAuditEngine(),
                policy ?? new WorkerSyncPolicy(),
                snapshotValidator ?? new AuthoritativeWorkerSnapshotValidator());
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TrackingAttendanceReader(
        Func<CancellationToken, Task<Result<AttendanceEmployeeRecord[]>>> getAllAsync) : IAttendanceEmployeeReader
    {
        public int GetAllCalls { get; private set; }
        public int GetByIdCalls { get; private set; }

        public Task<Result<AttendanceEmployeeRecord[]>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCalls++;
            return getAllAsync(cancellationToken);
        }

        public Task<Result<AttendanceEmployeeRecord?>> GetByAttendanceUserIdAsync(string attendanceUserId, CancellationToken cancellationToken = default)
        {
            GetByIdCalls++;
            return Task.FromResult(Result<AttendanceEmployeeRecord?>.Success(null));
        }
    }

    private sealed class TrackingWorkerSyncPolicy : IWorkerSyncPolicy
    {
        private readonly WorkerSyncPolicy inner = new();
        public int EvaluateExistingCalls { get; private set; }
        public int CreateNewCalls { get; private set; }
        public IReadOnlyCollection<string> InitializableFields => inner.InitializableFields;
        public IReadOnlyCollection<string> ProtectedLocalFields => inner.ProtectedLocalFields;
        public IReadOnlyCollection<string> SourceObservedOnlyFields => inner.SourceObservedOnlyFields;

        public WorkerSyncPolicyDecision EvaluateExistingWorker(Worker worker, AttendanceEmployeeRecord source)
        {
            EvaluateExistingCalls++;
            return inner.EvaluateExistingWorker(worker, source);
        }

        public Result<Worker> CreateNewWorker(AttendanceEmployeeRecord source, DateTime createdAtUtc)
        {
            CreateNewCalls++;
            return inner.CreateNewWorker(source, createdAtUtc);
        }

        public bool SynchronizeExistingWorker(Worker worker, AttendanceEmployeeRecord source, DateTime synchronizedAtUtc) =>
            inner.SynchronizeExistingWorker(worker, source, synchronizedAtUtc);
    }

    private sealed class TrackingSnapshotValidator : IAuthoritativeWorkerSnapshotValidator
    {
        private readonly AuthoritativeWorkerSnapshotValidator inner = new();

        public int InspectCalls { get; private set; }

        public WorkerSnapshotValidation Inspect(WorkerSourceSnapshot snapshot)
        {
            InspectCalls++;
            return inner.Inspect(snapshot);
        }

        public Result ValidateAuthoritativeApplication(WorkerSourceSnapshot snapshot) =>
            inner.ValidateAuthoritativeApplication(snapshot);
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

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
