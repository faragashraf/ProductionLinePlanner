using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class DepartmentAdministrationService(
    AppDbContext dbContext,
    IAttendanceDepartmentReader attendanceDepartmentReader,
    IAttendanceDepartmentWriter attendanceDepartmentWriter,
    IAttendanceEmployeeWriter attendanceEmployeeWriter,
    IAuditEngine auditEngine) : IDepartmentAdministrationService
{
    public async Task<Result<AttendanceDepartmentRecord[]>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        var departments = await attendanceDepartmentReader.GetAllDepartmentsAsync(cancellationToken);
        if (departments.IsFailure)
        {
            return Result<AttendanceDepartmentRecord[]>.Failure(departments.Error!);
        }

        var ordered = (departments.Value ?? [])
            .OrderBy(x => x.Name)
            .ThenBy(x => x.DepartmentId)
            .ToArray();

        return Result<AttendanceDepartmentRecord[]>.Success(ordered);
    }

    public async Task<Result<AttendanceDepartmentRecord>> CreateDepartmentAsync(
        string name,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<AttendanceDepartmentRecord>.Failure(new Error("Unauthorized", "User context is required."));
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result<AttendanceDepartmentRecord>.Failure(new Error("ValidationError", "Department name is required."));
        }

        var created = await attendanceDepartmentWriter.CreateDepartmentAsync(normalizedName, cancellationToken);
        if (created.IsFailure)
        {
            return Result<AttendanceDepartmentRecord>.Failure(created.Error!);
        }

        var record = created.Value!;
        await auditEngine.RecordAsync(
            actorUserId,
            Domain.Enums.AuditActionType.Create,
            "Department",
            record.DepartmentId.ToString(),
            before: null,
            after: record,
            requestMeta: requestMeta);

        return Result<AttendanceDepartmentRecord>.Success(record);
    }

    public async Task<Result> UpdateDepartmentNameAsync(
        int departmentId,
        string name,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (departmentId <= 0)
        {
            return Result.Failure(new Error("ValidationError", "DepartmentId must be greater than zero."));
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result.Failure(new Error("ValidationError", "Department name is required."));
        }

        var existing = await attendanceDepartmentReader.GetByIdAsync(departmentId, cancellationToken);
        if (existing.IsFailure)
        {
            return Result.Failure(existing.Error!);
        }

        if (existing.Value is null)
        {
            return Result.Failure(new Error("NotFound", "Department not found."));
        }

        if (string.Equals(existing.Value.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success();
        }

        var before = new { existing.Value.DepartmentId, existing.Value.Name };
        var updated = await attendanceDepartmentWriter.UpdateDepartmentNameAsync(departmentId, normalizedName, cancellationToken);
        if (updated.IsFailure)
        {
            return Result.Failure(updated.Error!);
        }

        await auditEngine.RecordAsync(
            actorUserId,
            Domain.Enums.AuditActionType.Update,
            "Department",
            departmentId.ToString(),
            before: before,
            after: new { DepartmentId = departmentId, Name = normalizedName },
            requestMeta: requestMeta);

        return Result.Success();
    }

    public async Task<Result> MoveWorkerToDepartmentAsync(
        Guid workerId,
        int departmentId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "User context is required."));
        }

        if (workerId == Guid.Empty)
        {
            return Result.Failure(new Error("ValidationError", "WorkerId is required."));
        }

        if (departmentId <= 0)
        {
            return Result.Failure(new Error("ValidationError", "DepartmentId must be greater than zero."));
        }

        var worker = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId, cancellationToken);
        if (worker is null)
        {
            return Result.Failure(new Error("NotFound", "Worker not found."));
        }

        if (string.IsNullOrWhiteSpace(worker.AttendanceUserId))
        {
            return Result.Failure(new Error("ValidationError", "Worker is not linked to attendance source."));
        }

        var department = await attendanceDepartmentReader.GetByIdAsync(departmentId, cancellationToken);
        if (department.IsFailure)
        {
            return Result.Failure(department.Error!);
        }

        if (department.Value is null)
        {
            return Result.Failure(new Error("NotFound", "Department not found."));
        }

        if (worker.AttendanceDepartmentId == departmentId)
        {
            return Result.Success();
        }

        var before = new { worker.Id, worker.AttendanceDepartmentId };
        var changed = await attendanceEmployeeWriter.UpdateWorkerDepartmentAsync(worker.AttendanceUserId!, departmentId, cancellationToken);
        if (changed.IsFailure)
        {
            return Result.Failure(changed.Error!);
        }

        worker.SetAttendanceDepartmentId(departmentId, DateTime.UtcNow);
        dbContext.Entry(worker).Property(nameof(Worker.LastExternalSyncAt)).CurrentValue = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEngine.RecordAsync(
            actorUserId,
            Domain.Enums.AuditActionType.Update,
            nameof(Worker),
            worker.Id.ToString(),
            before: before,
            after: new { worker.Id, worker.AttendanceDepartmentId },
            requestMeta: requestMeta);

        return Result.Success();
    }

    public async Task<Result> CanDeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
    {
        if (departmentId <= 0)
        {
            return Result.Failure(new Error("ValidationError", "DepartmentId must be greater than zero."));
        }

        var department = await attendanceDepartmentReader.GetByIdAsync(departmentId, cancellationToken);
        if (department.IsFailure)
        {
            return Result.Failure(department.Error!);
        }

        if (department.Value is null)
        {
            return Result.Failure(new Error("NotFound", "Department not found."));
        }

        var isUsed = await dbContext.Workers.AnyAsync(x => x.AttendanceDepartmentId == departmentId, cancellationToken);
        if (isUsed)
        {
            return Result.Failure(new Error("Conflict", "Department is in use by workers."));
        }

        return Result.Success();
    }

    public async Task<Result> DeleteDepartmentAsync(
        int departmentId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure(new Error("Unauthorized", "User context is required."));
        }

        var canDelete = await CanDeleteDepartmentAsync(departmentId, cancellationToken);
        if (canDelete.IsFailure)
        {
            return canDelete;
        }

        var existing = await attendanceDepartmentReader.GetByIdAsync(departmentId, cancellationToken);
        if (existing.IsFailure)
        {
            return Result.Failure(existing.Error!);
        }

        var before = existing.Value!;
        await auditEngine.RecordAsync(
            actorUserId,
            Domain.Enums.AuditActionType.Delete,
            "Department",
            departmentId.ToString(),
            before: before,
            after: null,
            requestMeta: requestMeta);

        return Result.Failure(new Error("ValidationError", "Delete is not supported for attendance departments in V1."));
    }
}
