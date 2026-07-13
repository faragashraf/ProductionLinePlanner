using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IAttendanceEmployeeReader
{
    Task<Result<AttendanceEmployeeRecord?>> GetByAttendanceUserIdAsync(string attendanceUserId, CancellationToken cancellationToken = default);
}
