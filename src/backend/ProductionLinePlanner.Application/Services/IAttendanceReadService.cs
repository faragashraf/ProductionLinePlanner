using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Services;

public interface IAttendanceReadService
{
    Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(
        Guid workerId,
        DateTime? fromDateUtc = null,
        DateTime? toDateUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(
        Guid? factoryId = null,
        Guid? lineId = null,
        DateTime? dateUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(
        Guid subStageId,
        DateTime? dateUtc = null,
        CancellationToken cancellationToken = default);
}
