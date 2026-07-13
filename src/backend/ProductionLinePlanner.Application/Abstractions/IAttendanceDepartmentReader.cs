using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IAttendanceDepartmentReader
{
    Task<Result<AttendanceDepartmentRecord?>> GetByIdAsync(int departmentId, CancellationToken cancellationToken = default);

    Task<Result<AttendanceDepartmentRecord[]>> GetAllDepartmentsAsync(CancellationToken cancellationToken = default);
}
