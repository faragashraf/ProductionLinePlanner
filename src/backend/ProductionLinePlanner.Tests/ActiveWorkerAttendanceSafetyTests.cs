using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Services;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class ActiveWorkerAttendanceSafetyTests
{
    [Fact]
    public async Task Attendance_refresh_creates_current_availability_only_for_active_service_workers()
    {
        var appDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var sourceOptions = Options.Create(new AttendanceSourceOptions());
        var attendanceDb = new AttendanceDbContext(
            new DbContextOptionsBuilder<AttendanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options,
            sourceOptions);
        var activeWorker = new Worker(Guid.NewGuid(), "001", "Active", attendanceUserId: "1001");
        var formerWorker = new Worker(Guid.NewGuid(), "002", "Former", attendanceUserId: "1002", isActive: false, employmentStatus: EmploymentStatus.LeftEmployment);
        appDb.Workers.AddRange(activeWorker, formerWorker);
        await appDb.SaveChangesAsync();
        var service = new AttendanceSyncService(appDb, attendanceDb, sourceOptions, NullLogger<AttendanceSyncService>.Instance, TestCairoTimeZoneProvider.Instance);

        var result = await service.SyncForProductionDateAsync(new DateOnly(2026, 7, 15));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.WorkersWithoutAttendanceCount);
        var records = await appDb.AttendanceRecords.AsNoTracking().ToArrayAsync();
        Assert.Single(records);
        Assert.Equal(activeWorker.Id, records[0].WorkerId);
        await appDb.DisposeAsync();
        await attendanceDb.DisposeAsync();
    }

    [Fact]
    public async Task Zero_source_check_ins_never_marks_an_active_worker_as_present()
    {
        var appDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var sourceOptions = Options.Create(new AttendanceSourceOptions());
        var attendanceDb = new AttendanceDbContext(
            new DbContextOptionsBuilder<AttendanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options,
            sourceOptions);
        var activeWorker = new Worker(Guid.NewGuid(), "001", "Active", attendanceUserId: "1001");
        appDb.Workers.Add(activeWorker);
        await appDb.SaveChangesAsync();
        var service = new AttendanceSyncService(appDb, attendanceDb, sourceOptions, NullLogger<AttendanceSyncService>.Instance, TestCairoTimeZoneProvider.Instance);

        var sync = await service.SyncForProductionDateAsync(new DateOnly(2026, 7, 15));
        var today = await service.GetTodayAttendanceAsync(dateUtc: new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.True(sync.IsSuccess);
        Assert.Equal(0, sync.Value!.SourceCheckInsCount);
        var state = Assert.Single(today.Value!);
        Assert.Equal(AttendanceStatus.Absent, state.AttendanceStatus);
        var stored = await appDb.AttendanceRecords.AsNoTracking().SingleAsync();
        Assert.Equal("sync-no-source", stored.SourceRawId);
        await appDb.DisposeAsync();
        await attendanceDb.DisposeAsync();
    }

    [Fact]
    public async Task Latest_daily_attendance_includes_the_synced_end_of_day_absence_before_that_timestamp()
    {
        var appDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var worker = new Worker(Guid.NewGuid(), "003", "Absent worker");
        var asOfUtc = new DateTime(2026, 7, 19, 3, 0, 0, DateTimeKind.Utc);
        var dayEndUtc = new DateTime(2026, 7, 19, 21, 0, 0, DateTimeKind.Utc);
        appDb.Workers.Add(worker);
        appDb.AttendanceRecords.Add(new AttendanceRecord(
            Guid.NewGuid(),
            worker.Id,
            dayEndUtc.AddTicks(-1),
            AttendanceStatus.Absent,
            source: "AttendanceSync",
            sourceRawId: "sync-no-source"));
        await appDb.SaveChangesAsync();

        var engine = new AttendanceEngine(null!, null!, appDb, TestCairoTimeZoneProvider.Instance);

        var result = await engine.GetLatestAttendanceStatusByWorkerAsync([worker.Id], asOfUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendanceStatus.Absent, result.Value![worker.Id].Status);
        await appDb.DisposeAsync();
    }
}
