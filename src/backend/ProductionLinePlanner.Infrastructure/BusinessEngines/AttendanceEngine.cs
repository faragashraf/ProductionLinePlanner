using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
    private readonly ICairoTimeZoneProvider _cairoTimeZoneProvider;

    public AttendanceEngine(
        IAttendanceReadService attendanceReadService,
        IAttendanceSyncService attendanceSyncService,
        AppDbContext dbContext,
        ICairoTimeZoneProvider cairoTimeZoneProvider)
    {
        _attendanceReadService = attendanceReadService;
        _attendanceSyncService = attendanceSyncService;
        _dbContext = dbContext;
        _cairoTimeZoneProvider = cairoTimeZoneProvider;
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

    public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default)
        => _attendanceSyncService.SyncForProductionDateAsync(productionDate, cancellationToken);

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
        var cairo = TimeZoneInfo.ConvertTimeFromUtc(asOf.Kind == DateTimeKind.Utc ? asOf : asOf.ToUniversalTime(), _cairoTimeZoneProvider.TimeZone);
        var localStart = DateOnly.FromDateTime(cairo).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dateStartUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, _cairoTimeZoneProvider.TimeZone);
        var dateEndUtc = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), _cairoTimeZoneProvider.TimeZone);

        var query = await _dbContext.AttendanceRecords
            .AsNoTracking()
            .Where(x => workerIdArray.Contains(x.WorkerId)
                        && x.AttendanceTimeUtc >= dateStartUtc
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
            x => new AttendanceStatusRecord(x.WorkerId, x.Entry.AttendanceStatus, x.Entry.AttendanceTimeUtc, x.Entry.Source, x.Entry.SourceRawId));

        return Result<Dictionary<Guid, AttendanceStatusRecord>>.Success(result);
    }

    public async Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(
        IEnumerable<Guid> workerIds,
        DateOnly productionDate,
        CancellationToken cancellationToken = default)
    {
        var ids = workerIds.Distinct().ToArray();
        if (ids.Length == 0)
            return Result<Dictionary<Guid, AttendancePresenceWindowDto>>.Success([]);

        var localStart = productionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, _cairoTimeZoneProvider.TimeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), _cairoTimeZoneProvider.TimeZone);
        var records = await _dbContext.AttendanceRecords.AsNoTracking()
            .Where(record => ids.Contains(record.WorkerId) && record.AttendanceTimeUtc >= startUtc && record.AttendanceTimeUtc < endUtc)
            .OrderBy(record => record.WorkerId)
            .ThenByDescending(record => record.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);

        var result = new Dictionary<Guid, AttendancePresenceWindowDto>();
        foreach (var group in records.GroupBy(record => record.WorkerId))
        {
            var record = group.First();
            var hasCheckIn = record.AttendanceStatus is AttendanceStatus.Present or AttendanceStatus.Late;
            DateTime? firstIn = hasCheckIn ? record.AttendanceTimeUtc : null;
            DateTime? lastOut = null;
            if (hasCheckIn && !string.IsNullOrWhiteSpace(record.SourcePayload))
            {
                try
                {
                    using var json = JsonDocument.Parse(record.SourcePayload);
                    if (json.RootElement.TryGetProperty("FirstInUtc", out var first) && first.TryGetDateTime(out var parsedFirst)) firstIn = parsedFirst;
                    if (json.RootElement.TryGetProperty("LastOutUtc", out var last) && last.ValueKind != JsonValueKind.Null && last.TryGetDateTime(out var parsedLast)) lastOut = parsedLast;
                }
                catch (JsonException)
                {
                    // Legacy/non-window payloads keep the first-in evidence and
                    // remain not ready until a source sync supplies LastOut.
                }
            }
            result[group.Key] = new AttendancePresenceWindowDto(group.Key, record.AttendanceStatus, firstIn, lastOut, hasCheckIn);
        }
        return Result<Dictionary<Guid, AttendancePresenceWindowDto>>.Success(result);
    }
}
