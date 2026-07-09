using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Services;

public interface IAttendanceSyncService
{
    Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default);
}
