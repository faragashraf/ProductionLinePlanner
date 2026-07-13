using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IAttendanceDepartmentWriter
{
    Task<Result<AttendanceDepartmentRecord>> CreateDepartmentAsync(string name, CancellationToken cancellationToken = default);

    Task<Result> UpdateDepartmentNameAsync(int departmentId, string name, CancellationToken cancellationToken = default);
}
