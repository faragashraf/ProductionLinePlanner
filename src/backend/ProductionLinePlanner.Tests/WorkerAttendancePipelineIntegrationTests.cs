using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class WorkerAttendancePipelineIntegrationTests
{
    [Fact]
    public async Task Attendance_pipeline_upserts_userinfo_workers_before_matching_daily_punches()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var appConnectionString = $"Data Source=file:{databaseName}-app?mode=memory&cache=shared";
        var sourceConnectionString = $"Data Source=file:{databaseName}-source?mode=memory&cache=shared";
        await using var appAnchor = new SqliteConnection(appConnectionString);
        await using var sourceAnchor = new SqliteConnection(sourceConnectionString);
        await appAnchor.OpenAsync();
        await sourceAnchor.OpenAsync();
        appAnchor.CreateCollation("SQL_Latin1_General_CP1_CI_AS", StringComparer.OrdinalIgnoreCase.Compare);

        var sourceOptions = Options.Create(new AttendanceSourceOptions());
        var appOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(appAnchor).Options;
        var attendanceOptions = new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlite(sourceConnectionString).Options;

        await using (var appDb = new AppDbContext(appOptions))
        {
            await appDb.Database.EnsureCreatedAsync();
            appDb.Workers.Add(new Worker(
                Guid.NewGuid(),
                employeeCode: "1024",
                fullName: "Planner-owned name",
                attendanceUserId: "3887",
                badgeNumber: "1024",
                photoReference: "planner-photo.png"));
            await appDb.SaveChangesAsync();
        }

        await using (var attendanceDb = new AttendanceDbContext(attendanceOptions, sourceOptions))
        {
            await attendanceDb.Database.ExecuteSqlRawAsync(
                "CREATE TABLE USERINFO (USERID INTEGER NULL, BADGENUMBER TEXT NULL, Name TEXT NULL, DEFAULTDEPTID INTEGER NULL, PHOTO BLOB NULL);");
            await attendanceDb.Database.ExecuteSqlRawAsync(
                "CREATE TABLE CurrentEmployeesImport (EmployeeCode TEXT NULL);");
            await attendanceDb.Database.ExecuteSqlRawAsync(
                "CREATE TABLE CHECKINOUT (USERID INTEGER NULL, CHECKTIME TEXT NULL, CHECKTYPE TEXT NULL);");
            await attendanceDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO USERINFO (USERID, BADGENUMBER, Name, DEFAULTDEPTID) VALUES
                (17252, '2429', 'Worker 2429', 1),
                (17253, '2430', 'Worker 2430', 1),
                (17254, '2431', 'Worker 2431', 1),
                (17255, '2434', 'Worker 2434', 1),
                (17256, '2437', 'Worker 2437', 1),
                (17257, '2436', 'Worker 2436', 1),
                (3887, '1024', 'External replacement name', 1);
                """);
            await attendanceDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO CHECKINOUT (USERID, CHECKTIME, CHECKTYPE) VALUES
                (17252, '2026-07-16 08:05:00', 'I'),
                (17252, '2026-07-16 17:10:00', 'O'),
                (17253, '2026-07-16 08:10:00', 'I'),
                (3887, '2026-07-16 08:15:00', 'I'),
                (3887, '2026-07-16 17:20:00', 'O');
                """);
        }

        await using (var appDb = new AppDbContext(appOptions))
        await using (var attendanceDb = new AttendanceDbContext(attendanceOptions, sourceOptions))
        {
            var workerSync = new WorkerInitialSyncService(
                appDb,
                new AttendanceDirectoryService(attendanceDb),
                new WorkerSyncPolicy(),
                new AuthoritativeWorkerSnapshotValidator(),
                new RecordingAuditEngine(),
                NullLogger<WorkerInitialSyncService>.Instance);
            var attendanceSync = new AttendanceSyncService(
                appDb,
                new ZkTimeDirectAttendanceSource(attendanceDb, sourceOptions, NullLogger<ZkTimeDirectAttendanceSource>.Instance),
                sourceOptions,
                NullLogger<AttendanceSyncService>.Instance,
                TestCairoTimeZoneProvider.Instance,
                workerSync);

            var first = await attendanceSync.SyncForProductionDateAsync(new DateOnly(2026, 7, 16));
            var second = await attendanceSync.SyncForProductionDateAsync(new DateOnly(2026, 7, 16));

            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(3, first.Value!.MatchedWorkersCount);
            Assert.Equal(7, await appDb.Workers.CountAsync());
            Assert.Equal(7, await appDb.AttendanceRecords.CountAsync());
            Assert.Equal(5, await appDb.AttendanceNotificationEvents.CountAsync());
            Assert.Equal(3, await appDb.AttendanceNotificationEvents.CountAsync(item => item.AttendanceType == WorkerAttendanceNotificationType.CheckIn));
            Assert.Equal(2, await appDb.AttendanceNotificationEvents.CountAsync(item => item.AttendanceType == WorkerAttendanceNotificationType.CheckOut));
        }

        await using var reloadedAppDb = new AppDbContext(appOptions);
        var importedWorkers = await reloadedAppDb.Workers
            .AsNoTracking()
            .Where(worker => new[] { "17252", "17253", "17254", "17255", "17256", "17257" }.Contains(worker.AttendanceUserId!))
            .OrderBy(worker => worker.AttendanceUserId)
            .ToArrayAsync();
        var attendance = await reloadedAppDb.AttendanceRecords.AsNoTracking().ToArrayAsync();
        var preservedWorker = await reloadedAppDb.Workers.AsNoTracking().SingleAsync(worker => worker.AttendanceUserId == "3887");

        Assert.Equal(6, importedWorkers.Length);
        Assert.Equal(["17252", "17253", "17254", "17255", "17256", "17257"], importedWorkers.Select(worker => worker.AttendanceUserId));
        Assert.Equal(7, attendance.Length);
        Assert.Equal(3, attendance.Count(record => record.AttendanceStatus == AttendanceStatus.Present));
        Assert.Equal(4, attendance.Count(record => record.AttendanceStatus == AttendanceStatus.Absent));

        var worker2429 = Assert.Single(importedWorkers, worker => worker.BadgeNumber == "2429");
        var worker2429Attendance = Assert.Single(attendance, record => record.WorkerId == worker2429.Id);
        Assert.Equal(AttendanceStatus.Present, worker2429Attendance.AttendanceStatus);
        Assert.Contains("FirstInUtc", worker2429Attendance.SourcePayload);
        Assert.Contains("LastOutUtc", worker2429Attendance.SourcePayload);

        Assert.Equal("Planner-owned name", preservedWorker.FullName);
        Assert.Equal("planner-photo.png", preservedWorker.PhotoReference);
        Assert.Single(attendance, record => record.WorkerId == preservedWorker.Id);
    }
}
