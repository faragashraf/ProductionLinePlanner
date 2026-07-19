using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface IAttendanceWorkforceEngine
{
    Task<Result<AttendanceWorkforcePageDto>> GetPageAsync(AttendanceWorkforceQuery query, CancellationToken cancellationToken = default);
    Task<Result<AttendanceWorkforceDetailDto>> GetWorkerDetailAsync(Guid workerId, DateOnly productionDate, CancellationToken cancellationToken = default);
}
