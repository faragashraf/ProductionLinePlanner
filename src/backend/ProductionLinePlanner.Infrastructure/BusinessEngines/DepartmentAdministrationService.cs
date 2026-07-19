using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Read-only view of source-observed attendance departments. Planner does not own ZKTime
/// departments and cannot create, rename, delete, or assign workers to them.
/// </summary>
public sealed class DepartmentAdministrationService(
    AppDbContext dbContext,
    IAttendanceDepartmentReader attendanceDepartmentReader) : IDepartmentAdministrationService
{
    private static readonly Error SourceReadOnly = new(
        "ExternalSourceReadOnly",
        "Attendance departments are source-observed and cannot be changed from Planner.");

    public async Task<Result<AttendanceDepartmentRecord[]>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        var departments = await attendanceDepartmentReader.GetAllDepartmentsAsync(cancellationToken);
        if (departments.IsFailure)
        {
            return Result<AttendanceDepartmentRecord[]>.Failure(departments.Error!);
        }

        return Result<AttendanceDepartmentRecord[]>.Success((departments.Value ?? [])
            .OrderBy(x => x.Name)
            .ThenBy(x => x.DepartmentId)
            .ToArray());
    }

    public Task<Result<AttendanceDepartmentRecord>> CreateDepartmentAsync(
        string name,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
            return Task.FromResult(Result<AttendanceDepartmentRecord>.Failure(new Error("Unauthorized", "User context is required.")));
        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(Result<AttendanceDepartmentRecord>.Failure(new Error("ValidationError", "Department name is required.")));

        return Task.FromResult(Result<AttendanceDepartmentRecord>.Failure(SourceReadOnly));
    }

    public Task<Result> UpdateDepartmentNameAsync(
        int departmentId,
        string name,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty) return Task.FromResult(Result.Failure(new Error("Unauthorized", "User context is required.")));
        if (departmentId <= 0) return Task.FromResult(Result.Failure(new Error("ValidationError", "DepartmentId must be greater than zero.")));
        if (string.IsNullOrWhiteSpace(name)) return Task.FromResult(Result.Failure(new Error("ValidationError", "Department name is required.")));
        return Task.FromResult(Result.Failure(SourceReadOnly));
    }

    public Task<Result> MoveWorkerToDepartmentAsync(
        Guid workerId,
        int departmentId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty) return Task.FromResult(Result.Failure(new Error("Unauthorized", "User context is required.")));
        if (workerId == Guid.Empty) return Task.FromResult(Result.Failure(new Error("ValidationError", "WorkerId is required.")));
        if (departmentId <= 0) return Task.FromResult(Result.Failure(new Error("ValidationError", "DepartmentId must be greater than zero.")));
        return Task.FromResult(Result.Failure(SourceReadOnly));
    }

    public async Task<Result> CanDeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
    {
        if (departmentId <= 0)
        {
            return Result.Failure(new Error("ValidationError", "DepartmentId must be greater than zero."));
        }

        var department = await attendanceDepartmentReader.GetByIdAsync(departmentId, cancellationToken);
        if (department.IsFailure) return Result.Failure(department.Error!);
        if (department.Value is null) return Result.Failure(new Error("NotFound", "Department not found."));

        var isUsed = await dbContext.Workers
            .AsNoTracking()
            .AnyAsync(x => x.AttendanceDepartmentId == departmentId, cancellationToken);
        return isUsed
            ? Result.Failure(new Error("Conflict", "Department is in use by workers."))
            : Result.Failure(SourceReadOnly);
    }

    public Task<Result> DeleteDepartmentAsync(
        int departmentId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty) return Task.FromResult(Result.Failure(new Error("Unauthorized", "User context is required.")));
        if (departmentId <= 0) return Task.FromResult(Result.Failure(new Error("ValidationError", "DepartmentId must be greater than zero.")));
        return Task.FromResult(Result.Failure(SourceReadOnly));
    }
}
