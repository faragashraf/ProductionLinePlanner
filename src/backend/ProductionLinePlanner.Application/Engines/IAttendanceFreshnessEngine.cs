using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface IAttendanceFreshnessEngine
{
    Task<AttendanceSyncFreshnessDto> GetAsync(
        DateOnly operationalDate,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);
}
