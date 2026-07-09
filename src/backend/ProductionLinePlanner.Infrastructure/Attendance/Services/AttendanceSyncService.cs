using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Attendance.Entities;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

public sealed class AttendanceSyncService : IAttendanceReadService, IAttendanceSyncService
{
    private const string TempStatusActive = "Active";
    private const string TempStatusScheduled = "Scheduled";
    private const string SyncAbsentStatus = "sync-no-source";

    private readonly AppDbContext _appDbContext;
    private readonly AttendanceDbContext _attendanceDbContext;
    private readonly AttendanceSourceOptions _sourceOptions;
    private readonly ILogger<AttendanceSyncService> _logger;

    public AttendanceSyncService(
        AppDbContext appDbContext,
        AttendanceDbContext attendanceDbContext,
        IOptions<AttendanceSourceOptions> sourceOptions,
        ILogger<AttendanceSyncService> logger)
    {
        _appDbContext = appDbContext;
        _attendanceDbContext = attendanceDbContext;
        _sourceOptions = sourceOptions.Value;
        _logger = logger;
    }

    public async Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(
        Guid? factoryId = null,
        Guid? lineId = null,
        DateTime? dateUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (dateUtc.HasValue && dateUtc.Value.Kind != DateTimeKind.Utc)
        {
            return Result<AttendanceWorkerStateDto[]>.Failure(new Error("ValidationError", "Date must be UTC."));
        }

        var forDate = GetDateOnly(dateUtc);
        var workerFilter = await GetVisibleWorkerIdsForScopeAsync(
            forDate,
            factoryId,
            lineId,
            cancellationToken);

        var workers = await _appDbContext.Workers
            .AsNoTracking()
            .Where(x => workerFilter.Contains(x.Id) && x.IsActive)
            .Select(x => new
            {
                x.Id,
                x.EmployeeCode,
                x.FullName,
                x.AttendanceUserId,
                x.BadgeNumber
            })
            .ToListAsync(cancellationToken);

        if (workers.Count == 0)
        {
            return Result<AttendanceWorkerStateDto[]>.Success(Array.Empty<AttendanceWorkerStateDto>());
        }

        var attendanceByWorker = await GetLatestAttendanceByWorkerForDateAsync(
            workers.Select(x => x.Id),
            forDate,
            cancellationToken);

        var items = workers
            .Select(worker =>
            {
                if (attendanceByWorker.TryGetValue(worker.Id, out var attendance))
                {
                    return new AttendanceWorkerStateDto
                    {
                        WorkerId = worker.Id,
                        EmployeeCode = worker.EmployeeCode,
                        FullName = worker.FullName,
                        AttendanceStatus = attendance.AttendanceStatus,
                        AttendanceTimeUtc = attendance.AttendanceTimeUtc,
                        Source = attendance.Source,
                        AttendanceUserId = worker.AttendanceUserId,
                        BadgeNumber = worker.BadgeNumber
                    };
                }

                return new AttendanceWorkerStateDto
                {
                    WorkerId = worker.Id,
                    EmployeeCode = worker.EmployeeCode,
                    FullName = worker.FullName,
                    AttendanceStatus = AttendanceStatus.Unassigned,
                    AttendanceTimeUtc = forDate,
                    Source = null,
                    AttendanceUserId = worker.AttendanceUserId,
                    BadgeNumber = worker.BadgeNumber
                };
            })
            .OrderBy(x => x.EmployeeCode)
            .ToArray();

        return Result<AttendanceWorkerStateDto[]>.Success(items);
    }

    public async Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(
        Guid workerId,
        DateTime? fromDateUtc = null,
        DateTime? toDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _appDbContext.Workers.AnyAsync(x => x.Id == workerId && x.IsActive, cancellationToken))
        {
            return Result<AttendanceRecordDto[]>.Failure(new Error("NotFound", "Worker not found."));
        }

        if (fromDateUtc.HasValue && fromDateUtc.Value.Kind != DateTimeKind.Utc)
        {
            return Result<AttendanceRecordDto[]>.Failure(new Error("ValidationError", "fromDate must be UTC."));
        }

        if (toDateUtc.HasValue && toDateUtc.Value.Kind != DateTimeKind.Utc)
        {
            return Result<AttendanceRecordDto[]>.Failure(new Error("ValidationError", "toDate must be UTC."));
        }

        if (fromDateUtc.HasValue && toDateUtc.HasValue && toDateUtc.Value < fromDateUtc.Value)
        {
            return Result<AttendanceRecordDto[]>.Failure(new Error("ValidationError", "toDate must be after fromDate."));
        }

        IQueryable<AttendanceRecord> query = _appDbContext.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.WorkerId == workerId)
            .OrderByDescending(x => x.AttendanceTimeUtc);

        if (fromDateUtc.HasValue)
        {
            query = query.Where(x => x.AttendanceTimeUtc >= fromDateUtc.Value);
        }

        if (toDateUtc.HasValue)
        {
            query = query.Where(x => x.AttendanceTimeUtc <= toDateUtc.Value);
        }

        var items = await query
            .Select(x => new AttendanceRecordDto
            {
                Id = x.Id,
                WorkerId = x.WorkerId,
                AttendanceTimeUtc = x.AttendanceTimeUtc,
                AttendanceStatus = x.AttendanceStatus,
                Source = x.Source,
                AttendanceUserId = x.AttendanceUserId,
                BadgeNumber = x.BadgeNumber,
                SourceRawId = x.SourceRawId
            })
            .ToArrayAsync(cancellationToken);

        return Result<AttendanceRecordDto[]>.Success(items);
    }

    public async Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(
        Guid subStageId,
        DateTime? dateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var subStage = await _appDbContext.SubStages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subStageId && x.IsActive, cancellationToken);

        if (subStage is null)
        {
            return Result<AttendanceSubStageAttendanceDto>.Failure(new Error("NotFound", "SubStage not found."));
        }

        if (dateUtc.HasValue && dateUtc.Value.Kind != DateTimeKind.Utc)
        {
            return Result<AttendanceSubStageAttendanceDto>.Failure(new Error("ValidationError", "Date must be UTC."));
        }

        var forDate = GetDateOnly(dateUtc);
        var activeWorkerIds = await _appDbContext.Workers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var assignments = await ResolveCurrentAssignmentsAsync(
            activeWorkerIds,
            forDate,
            cancellationToken);

        var stageWorkers = assignments
            .Where(x => x.Value.EffectiveSubStageId == subStageId)
            .Select(x => x.Key)
            .ToArray();

        var workers = await _appDbContext.Workers
            .AsNoTracking()
            .Where(x => stageWorkers.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.EmployeeCode,
                x.FullName,
                x.AttendanceUserId,
                x.BadgeNumber
            })
            .ToListAsync(cancellationToken);

        var attendanceByWorker = await GetLatestAttendanceByWorkerForDateAsync(
            workers.Select(x => x.Id),
            forDate,
            cancellationToken);

        var present = 0;
        var late = 0;
        var absent = 0;
        var unassigned = 0;

        var workerItems = workers.Select(worker =>
        {
            if (!attendanceByWorker.TryGetValue(worker.Id, out var attendance))
            {
                unassigned++;
                return new AttendanceWorkerStateDto
                {
                    WorkerId = worker.Id,
                    EmployeeCode = worker.EmployeeCode,
                    FullName = worker.FullName,
                    AttendanceStatus = AttendanceStatus.Unassigned,
                    AttendanceTimeUtc = forDate,
                    Source = null,
                    AttendanceUserId = worker.AttendanceUserId,
                    BadgeNumber = worker.BadgeNumber
                };
            }

            if (attendance.AttendanceStatus == AttendanceStatus.Present)
            {
                present++;
            }
            else if (attendance.AttendanceStatus == AttendanceStatus.Late)
            {
                late++;
            }
            else if (attendance.AttendanceStatus == AttendanceStatus.Absent)
            {
                absent++;
            }
            else
            {
                unassigned++;
            }

            return new AttendanceWorkerStateDto
            {
                WorkerId = worker.Id,
                EmployeeCode = worker.EmployeeCode,
                FullName = worker.FullName,
                AttendanceStatus = attendance.AttendanceStatus,
                AttendanceTimeUtc = attendance.AttendanceTimeUtc,
                Source = attendance.Source,
                AttendanceUserId = worker.AttendanceUserId,
                BadgeNumber = worker.BadgeNumber
            };
        }).ToArray();

        return Result<AttendanceSubStageAttendanceDto>.Success(new AttendanceSubStageAttendanceDto
        {
            SubStageId = subStageId,
            SubStageName = subStage.Name,
            DateUtc = forDate,
            Capacity = subStage.Capacity,
            AssignedWorkers = stageWorkers.Length,
            PresentWorkers = present,
            LateWorkers = late,
            AbsentWorkers = absent,
            UnassignedWorkers = Math.Max(0, Math.Max(0, subStage.Capacity - stageWorkers.Length) + unassigned),
            Workers = workerItems
        });
    }

    public async Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default)
    {
        var syncDate = GetDateOnly(DateTime.UtcNow);
        var startUtc = syncDate;
        var endUtc = syncDate.AddDays(1);

        List<AttendanceSourceUserInfo> sourceUserInfos;
        List<AttendanceSourceCheckInOut> sourceCheckIns;
        List<Worker> workers;

        try
        {
            sourceUserInfos = await _attendanceDbContext.UserInfos.AsNoTracking().ToListAsync(cancellationToken);
            sourceCheckIns = await _attendanceDbContext.CheckInOuts
                .AsNoTracking()
                .Where(x => x.CheckTime >= startUtc && x.CheckTime < endUtc && x.UserId != null)
                .ToListAsync(cancellationToken);
            workers = await _appDbContext.Workers
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to read attendance source data during sync.");
            return Result<AttendanceSyncResultDto>.Failure(new Error("AttendanceSourceError", "Unable to connect to attendance source or read required tables."));
        }

        var mappedWorkers = workers
            .Where(x => !string.IsNullOrWhiteSpace(x.AttendanceUserId) || !string.IsNullOrWhiteSpace(x.BadgeNumber))
            .ToArray();

        if (mappedWorkers.Length == 0)
        {
            return Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto
            {
                SyncDateUtc = syncDate,
                EvaluatedWorkers = 0,
                SyncedWorkers = 0,
                InsertedRecords = 0,
                UpdatedRecords = 0,
                UnchangedRecords = 0,
                UnmatchedSourceRows = 0,
                WorkersWithMissingMapping = 0
            });
        }

        var attendanceUserMap = BuildIdentityLookup(mappedWorkers, x => x.AttendanceUserId);
        var badgeMap = BuildIdentityLookup(mappedWorkers, x => x.BadgeNumber);
        var badgeBySourceUserId = sourceUserInfos
            .Where(x => x.UserId.HasValue)
            .GroupBy(x => NormalizeIdentity(x.UserId!.ToString()))
            .Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!, g => NormalizeIdentity(g.First().BadgeNumber), StringComparer.OrdinalIgnoreCase);

        var validCheckIns = sourceCheckIns
            .Where(x => x.UserId.HasValue && x.CheckTime != default)
            .Select(x => new
            {
                WorkerUserId = NormalizeIdentity(x.UserId!.ToString())!,
                x.CheckTime,
                RawSourceIdentifier = $"{NormalizeIdentity(x.UserId!.ToString())}:{x.CheckTime:O}:{NormalizeIdentity(x.CheckType)}"
            })
            .Where(x => x.WorkerUserId.Length > 0)
            .ToList();

        var matchedByWorker = new Dictionary<Guid, (DateTime CheckTime, string SourceRawId)>();
        var unmatchedSourceRows = 0;

        foreach (var item in validCheckIns.OrderBy(x => x.CheckTime))
        {
            var sourceUserId = item.WorkerUserId;
            if (!TryResolveWorkerId(sourceUserId, attendanceUserMap, badgeMap, badgeBySourceUserId, out var workerId))
            {
                unmatchedSourceRows++;
                continue;
            }

            if (!matchedByWorker.TryGetValue(workerId, out _))
            {
                matchedByWorker[workerId] = (item.CheckTime, item.RawSourceIdentifier);
            }
        }

        var workerIds = mappedWorkers.Select(x => x.Id).ToArray();
        var existingRecords = await _appDbContext.AttendanceRecords
            .Where(x => workerIds.Contains(x.WorkerId) && x.AttendanceTimeUtc >= startUtc && x.AttendanceTimeUtc < endUtc)
            .ToListAsync(cancellationToken);

        var existingByWorker = existingRecords
            .GroupBy(x => x.WorkerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.AttendanceTimeUtc).First());

        var insertCount = 0;
        var updateCount = 0;
        var unchangedCount = 0;
        var syncEntries = 0;
        var syncRunAt = DateTime.UtcNow;

        foreach (var worker in mappedWorkers)
        {
            var statusTime = syncRunAt;
            var status = AttendanceStatus.Absent;
            var sourceRawId = SyncAbsentStatus;

            if (matchedByWorker.TryGetValue(worker.Id, out var match))
            {
                statusTime = match.CheckTime;
                status = CalculateStatus(match.CheckTime, syncDate);
                sourceRawId = match.SourceRawId;
            }

            if (!existingByWorker.TryGetValue(worker.Id, out var existing))
            {
                var record = new AttendanceRecord(
                    id: Guid.NewGuid(),
                    workerId: worker.Id,
                    attendanceTimeUtc: statusTime,
                    attendanceStatus: status,
                    source: _sourceOptions.SourceName,
                    sourcePayload: null,
                    sourceRawId: sourceRawId,
                    attendanceUserId: worker.AttendanceUserId,
                    badgeNumber: worker.BadgeNumber,
                    createdAtUtc: syncRunAt);

                _appDbContext.AttendanceRecords.Add(record);
                insertCount++;
                syncEntries++;
                continue;
            }

            if (existing.AttendanceStatus == status && existing.AttendanceTimeUtc == statusTime && existing.SourceRawId == sourceRawId)
            {
                unchangedCount++;
                continue;
            }

            existing.UpdateAttendanceStatus(
                statusTime,
                status,
                _sourceOptions.SourceName,
                null,
                sourceRawId,
                worker.AttendanceUserId,
                worker.BadgeNumber,
                syncRunAt);
            updateCount++;
            syncEntries++;
        }

        await _appDbContext.SaveChangesAsync(cancellationToken);

        var missingWorkerMappings = sourceCheckIns.Select(x => NormalizeIdentity(x.UserId?.ToString())).Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(sourceUserId =>
            {
                if (string.IsNullOrWhiteSpace(sourceUserId))
                {
                    return false;
                }

                if (TryResolveWorkerId(sourceUserId, attendanceUserMap, badgeMap, badgeBySourceUserId, out _))
                {
                    return false;
                }

                return true;
            });

        return Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto
        {
            SyncDateUtc = syncDate,
            EvaluatedWorkers = mappedWorkers.Length,
            SyncedWorkers = syncEntries,
            InsertedRecords = insertCount,
            UpdatedRecords = updateCount,
            UnchangedRecords = unchangedCount,
            UnmatchedSourceRows = unmatchedSourceRows,
            WorkersWithMissingMapping = missingWorkerMappings
        });
    }

    private static string? NormalizeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static DateTime GetDateOnly(DateTime? dateUtc)
    {
        var now = dateUtc ?? DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static Dictionary<string, List<Guid>> BuildIdentityLookup(IEnumerable<Worker> workers, Func<Worker, string?> selector)
    {
        var result = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var worker in workers)
        {
            var key = NormalizeIdentity(selector(worker));
            if (key is null)
            {
                continue;
            }

            if (!result.TryGetValue(key, out var ids))
            {
                ids = [];
                result[key] = ids;
            }

            ids.Add(worker.Id);
        }

        return result;
    }

    private static bool TryResolveWorkerId(
        string sourceUserId,
        Dictionary<string, List<Guid>> attendanceUserLookup,
        Dictionary<string, List<Guid>> badgeLookup,
        Dictionary<string, string?> sourceBadgeByUserId,
        out Guid workerId)
    {
        workerId = Guid.Empty;

        if (attendanceUserLookup.TryGetValue(sourceUserId, out var byAttendanceUserId) && byAttendanceUserId.Count == 1)
        {
            workerId = byAttendanceUserId[0];
            return true;
        }

        if (sourceBadgeByUserId.TryGetValue(sourceUserId, out var sourceBadge) &&
            !string.IsNullOrWhiteSpace(sourceBadge) &&
            badgeLookup.TryGetValue(sourceBadge, out var byBadge) &&
            byBadge.Count == 1)
        {
            workerId = byBadge[0];
            return true;
        }

        return false;
    }

    private AttendanceStatus CalculateStatus(DateTime checkTimeUtc, DateTime syncDate)
    {
        var shiftStart = syncDate.Add(_sourceOptions.DayStartTime);
        var lateThreshold = shiftStart.AddMinutes(_sourceOptions.LateThresholdMinutes);

        return checkTimeUtc <= lateThreshold ? AttendanceStatus.Present : AttendanceStatus.Late;
    }

    private async Task<Dictionary<Guid, AttendanceRecord>> GetLatestAttendanceByWorkerForDateAsync(
        IEnumerable<Guid> workerIds,
        DateTime forDate,
        CancellationToken cancellationToken)
    {
        var workerIdArray = workerIds.Distinct().ToArray();
        if (workerIdArray.Length == 0)
        {
            return new Dictionary<Guid, AttendanceRecord>();
        }

        var endUtc = forDate.AddDays(1);
        var records = await _appDbContext.AttendanceRecords
            .AsNoTracking()
            .Where(x => workerIdArray.Contains(x.WorkerId) && x.AttendanceTimeUtc >= forDate && x.AttendanceTimeUtc < endUtc)
            .OrderBy(x => x.WorkerId)
            .ThenByDescending(x => x.AttendanceTimeUtc)
            .ToListAsync(cancellationToken);

        return records
            .GroupBy(x => x.WorkerId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    private async Task<HashSet<Guid>> GetVisibleWorkerIdsForScopeAsync(
        DateTime dateUtc,
        Guid? factoryId,
        Guid? lineId,
        CancellationToken cancellationToken)
    {
        var allActiveWorkerIds = await _appDbContext.Workers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (factoryId is null && lineId is null)
        {
            return allActiveWorkerIds.ToHashSet();
        }

        var scopeSubStageIds = await GetSubStageIdsInScopeAsync(factoryId, lineId, cancellationToken);
        if (scopeSubStageIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var assignments = await ResolveCurrentAssignmentsAsync(allActiveWorkerIds, dateUtc, cancellationToken);

        return assignments
            .Where(x => x.Value.EffectiveSubStageId is not null && scopeSubStageIds.Contains(x.Value.EffectiveSubStageId.Value))
            .Select(x => x.Key)
            .ToHashSet();
    }

    private async Task<HashSet<Guid>> GetSubStageIdsInScopeAsync(Guid? factoryId, Guid? lineId, CancellationToken cancellationToken)
    {
        var subStageIds = await (from subStage in _appDbContext.SubStages.AsNoTracking()
                                join main in _appDbContext.MainStages.AsNoTracking() on subStage.MainStageId equals main.Id
                                join line in _appDbContext.ProductionLines.AsNoTracking() on main.ProductionLineId equals line.Id
                                where subStage.IsActive && main.IsActive && line.IsActive
                                select new
                                {
                                    subStage.Id,
                                    lineId = line.Id,
                                    factoryId = (Guid?)line.FactoryId
                                })
            .Where(x => (!lineId.HasValue || x.lineId == lineId.Value) && (!factoryId.HasValue || x.factoryId == factoryId.Value))
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        return subStageIds.ToHashSet();
    }

    private async Task<Dictionary<Guid, AssignmentState>> ResolveCurrentAssignmentsAsync(
        IEnumerable<Guid> workerIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var uniqueWorkerIds = workerIds.Distinct().ToArray();
        if (uniqueWorkerIds.Length == 0)
        {
            return new Dictionary<Guid, AssignmentState>();
        }

        var defaultAssignments = await _appDbContext.WorkerDefaultAssignments
            .AsNoTracking()
            .Where(x => uniqueWorkerIds.Contains(x.WorkerId) && x.IsActive)
            .Select(x => new { x.WorkerId, x.AssignedAt, x.Id, x.SubStageId })
            .ToListAsync(cancellationToken);

        var currentDefaultsByWorker = defaultAssignments
            .GroupBy(x => x.WorkerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.AssignedAt)
                    .ThenByDescending(x => x.Id)
                    .First());

        var activeTemporaryAssignments = await _appDbContext.WorkerTemporaryAssignments
            .AsNoTracking()
            .Where(x => uniqueWorkerIds.Contains(x.WorkerId)
                        && x.StartAtUtc <= asOfUtc
                        && x.EndAtUtc > asOfUtc
                        && (x.Status == TempStatusActive || x.Status == TempStatusScheduled))
            .Select(x => new
            {
                x.WorkerId,
                x.StartAtUtc,
                x.EndAtUtc,
                x.FromSubStageId,
                x.ToSubStageId,
                x.ReplacementForWorkerId
            })
            .ToListAsync(cancellationToken);

        var temporaryByWorker = activeTemporaryAssignments
            .GroupBy(x => x.WorkerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.StartAtUtc)
                    .ThenByDescending(x => x.EndAtUtc)
                    .First());

        var results = new Dictionary<Guid, AssignmentState>();
        foreach (var workerId in uniqueWorkerIds)
        {
            if (temporaryByWorker.TryGetValue(workerId, out var tempAssignment))
            {
                results[workerId] = new AssignmentState(
                    workerId,
                    AssignmentType.Temporary,
                    tempAssignment.StartAtUtc,
                    tempAssignment.EndAtUtc,
                    tempAssignment.ToSubStageId,
                    tempAssignment.FromSubStageId,
                    tempAssignment.ToSubStageId,
                    tempAssignment.ReplacementForWorkerId);
                continue;
            }

            if (currentDefaultsByWorker.TryGetValue(workerId, out var defaultAssignment))
            {
                results[workerId] = new AssignmentState(
                    workerId,
                    AssignmentType.Default,
                    defaultAssignment.AssignedAt,
                    null,
                    defaultAssignment.SubStageId,
                    null,
                    null,
                    null);
                continue;
            }

            results[workerId] = new AssignmentState(
                workerId,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        return results;
    }

    private sealed record AssignmentState(
        Guid WorkerId,
        AssignmentType? AssignmentType,
        DateTime? StartsAtUtc,
        DateTime? EndsAtUtc,
        Guid? EffectiveSubStageId,
        Guid? FromSubStageId,
        Guid? ToSubStageId,
        Guid? ReplacementForWorkerId);
}
