using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IAttendanceEmployeeWriter
{
    Task<Result> UpdateWorkerFullNameAsync(string attendanceUserId, string fullName, CancellationToken cancellationToken = default);

    Task<Result> UpdateWorkerDepartmentAsync(string attendanceUserId, int departmentId, CancellationToken cancellationToken = default);
}
