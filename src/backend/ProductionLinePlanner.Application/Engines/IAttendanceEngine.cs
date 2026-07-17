using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface IAttendanceEngine
{
    Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(
        Guid? factoryId,
        Guid? lineId,
        DateTime? dateUtc,
        CancellationToken cancellationToken = default);

    Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(
        Guid workerId,
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        CancellationToken cancellationToken = default);

    Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(
        Guid subStageId,
        DateTime? dateUtc,
        CancellationToken cancellationToken = default);

    Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default);
    Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default);

    Task<Result<Dictionary<Guid, AttendanceStatusRecord>>>
        GetLatestAttendanceStatusByWorkerAsync(
            IEnumerable<Guid> workerIds,
            DateTime? asOfUtc = null,
            CancellationToken cancellationToken = default);

    Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(
        IEnumerable<Guid> workerIds,
        DateOnly productionDate,
        CancellationToken cancellationToken = default);
}
