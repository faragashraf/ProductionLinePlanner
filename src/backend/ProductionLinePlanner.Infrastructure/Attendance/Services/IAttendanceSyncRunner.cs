using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

public sealed record AttendanceSyncExecutionContext(
    DateOnly ProductionDate,
    string CorrelationId,
    string TriggerType);

/// <summary>
/// Scoped sync executor used by the singleton coordinator. Keeping database
/// contexts inside this boundary lets a sync finish safely after its HTTP
/// client disconnects.
/// </summary>
public interface IAttendanceSyncRunner
{
    Task<Result<AttendanceSyncResultDto>> RunAsync(
        AttendanceSyncExecutionContext context,
        CancellationToken cancellationToken = default);
}
