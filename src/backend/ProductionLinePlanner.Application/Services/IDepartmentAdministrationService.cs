using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Services;

public interface IDepartmentAdministrationService
{
    Task<Result<AttendanceDepartmentRecord[]>> GetDepartmentsAsync(CancellationToken cancellationToken = default);

    Task<Result<AttendanceDepartmentRecord>> CreateDepartmentAsync(
        string name,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateDepartmentNameAsync(
        int departmentId,
        string name,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result> MoveWorkerToDepartmentAsync(
        Guid workerId,
        int departmentId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result> CanDeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

    Task<Result> DeleteDepartmentAsync(int departmentId, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default);
}
