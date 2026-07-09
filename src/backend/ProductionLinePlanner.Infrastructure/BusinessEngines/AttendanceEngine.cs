using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class AttendanceEngine : IAttendanceEngine
{
    private readonly IAttendanceReadService _attendanceReadService;
    private readonly IAttendanceSyncService _attendanceSyncService;
    private readonly AppDbContext _dbContext;

    public AttendanceEngine(
        IAttendanceReadService attendanceReadService,
        IAttendanceSyncService attendanceSyncService,
        AppDbContext dbContext)
    {
        _attendanceReadService = attendanceReadService;
        _attendanceSyncService = attendanceSyncService;
        _dbContext = dbContext;
    }

    public Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(
        Guid? factoryId,
        Guid? lineId,
        DateTime? dateUtc,
        CancellationToken cancellationToken = default)
        => _attendanceReadService.GetTodayAttendanceAsync(factoryId, lineId, dateUtc, cancellationToken);

    public Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(
        Guid workerId,
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        CancellationToken cancellationToken = default)
        => _attendanceReadService.GetWorkerAttendanceAsync(workerId, fromDateUtc, toDateUtc, cancellationToken);

    public Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(
        Guid subStageId,
        DateTime? dateUtc,
        CancellationToken cancellationToken = default)
        => _attendanceReadService.GetSubStageAttendanceAsync(subStageId, dateUtc, cancellationToken);

    public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default)
        => _attendanceSyncService.SyncTodayAsync(cancellationToken);

    public async Task<Result<Dictionary<Guid, AttendanceStatusRecord>>> GetLatestAttendanceStatusByWorkerAsync(
        IEnumerable<Guid> workerIds,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var workerIdArray = workerIds.Distinct().ToArray();
        if (workerIdArray.Length == 0)
        {
            return Result<Dictionary<Guid, AttendanceStatusRecord>>.Success(new Dictionary<Guid, AttendanceStatusRecord>());
        }

        var asOf = asOfUtc ?? DateTime.UtcNow;
        var dateOnly = new DateTime(asOf.Year, asOf.Month, asOf.Day, 0, 0, 0, DateTimeKind.Utc);

        var query = await _dbContext.AttendanceRecords
            .AsNoTracking()
            .Where(x => workerIdArray.Contains(x.WorkerId)
                        && x.AttendanceTimeUtc >= dateOnly
                        && x.AttendanceTimeUtc <= asOf)
            .GroupBy(x => x.WorkerId)
            .Select(g => new
            {
                WorkerId = g.Key,
                Entry = g.OrderByDescending(x => x.AttendanceTimeUtc).First()
            })
            .ToListAsync(cancellationToken);

        var result = query.ToDictionary(
            x => x.WorkerId,
            x => new AttendanceStatusRecord(x.WorkerId, x.Entry.AttendanceStatus, x.Entry.AttendanceTimeUtc, x.Entry.Source));

        return Result<Dictionary<Guid, AttendanceStatusRecord>>.Success(result);
    }
}

