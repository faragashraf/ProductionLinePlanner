using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

/// <summary>
/// Read-only directory over the external attendance source. This capability intentionally has no
/// writer interface and contains no SQL mutation path for USERINFO, CHECKINOUT, or ZKTime tables.
/// </summary>
public sealed class AttendanceDirectoryService(AttendanceDbContext attendanceDbContext) :
    IAttendanceEmployeeReader,
    IAttendanceDepartmentReader,
    IAttendanceWorkerPhotoReader
{
    public async Task<Result<AttendanceWorkerPhotoRecord[]>> GetAllCurrentPhotosAsync(CancellationToken cancellationToken = default)
    {
        var currentEmployeeCodes = await GetCurrentEmployeeCodesAsync(cancellationToken);
        var sourcePhotos = await attendanceDbContext.UserInfos
            .AsNoTracking()
            .Where(x => x.Photo != null && x.UserId != null)
            .Select(x => new { x.UserId, x.BadgeNumber, x.Photo })
            .ToArrayAsync(cancellationToken);

        return Result<AttendanceWorkerPhotoRecord[]>.Success(sourcePhotos
            .Where(x => currentEmployeeCodes.Contains(NormalizeCode(x.BadgeNumber)))
            .Where(x => x.Photo is { Length: > 0 })
            .Select(x => new AttendanceWorkerPhotoRecord(x.UserId!.Value.ToString(), x.Photo!))
            .ToArray());
    }

    public async Task<Result<AttendanceWorkerPhotoRecord?>> GetPhotoByAttendanceUserIdAsync(
        string attendanceUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseAttendanceUserId(attendanceUserId, out var userId))
        {
            return Result<AttendanceWorkerPhotoRecord?>.Failure(new Error("ValidationError", "AttendanceUserId must be a valid integer."));
        }

        var photo = await attendanceDbContext.UserInfos
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Photo != null)
            .Select(x => x.Photo)
            .FirstOrDefaultAsync(cancellationToken);

        return Result<AttendanceWorkerPhotoRecord?>.Success(photo is { Length: > 0 }
            ? new AttendanceWorkerPhotoRecord(userId.ToString(), photo)
            : null);
    }

    public async Task<Result<AttendanceEmployeeRecord?>> GetByAttendanceUserIdAsync(
        string attendanceUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseAttendanceUserId(attendanceUserId, out var userId))
        {
            return Result<AttendanceEmployeeRecord?>.Failure(new Error("ValidationError", "AttendanceUserId must be a valid integer."));
        }

        var entity = await attendanceDbContext.UserInfos
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.UserId,
                x.DefaultDeptId,
                x.BadgeNumber,
                x.Name
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return Result<AttendanceEmployeeRecord?>.Success(null);
        }

        var currentEmployeeCodes = await GetCurrentEmployeeCodesAsync(cancellationToken);
        var observedEmployeeCode = NormalizeCode(entity.BadgeNumber);

        return Result<AttendanceEmployeeRecord?>.Success(new AttendanceEmployeeRecord(
            AttendanceUserId: userId.ToString(),
            DepartmentId: entity.DefaultDeptId,
            BadgeNumber: entity.BadgeNumber,
            Name: entity.Name,
            IsActive: currentEmployeeCodes.Contains(observedEmployeeCode),
            EmployeeCode: observedEmployeeCode));
    }

    public async Task<Result<AttendanceEmployeeRecord[]>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var currentEmployeeCodes = await GetCurrentEmployeeCodesAsync(cancellationToken);
        var sourceUsers = await attendanceDbContext.UserInfos
            .AsNoTracking()
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.BadgeNumber)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                x.UserId,
                x.DefaultDeptId,
                x.BadgeNumber,
                x.Name
            })
            .ToArrayAsync(cancellationToken);

        var records = sourceUsers
            .Where(x => currentEmployeeCodes.Contains(NormalizeCode(x.BadgeNumber)))
            .Select(x => new AttendanceEmployeeRecord(
                AttendanceUserId: x.UserId?.ToString(),
                DepartmentId: x.DefaultDeptId,
                BadgeNumber: x.BadgeNumber,
                Name: x.Name,
                IsActive: true,
                EmployeeCode: NormalizeCode(x.BadgeNumber)))
            .ToArray();

        return Result<AttendanceEmployeeRecord[]>.Success(records);
    }

    public async Task<Result<AttendanceDepartmentRecord[]>> GetAllDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        var departments = await attendanceDbContext.Departments
            .AsNoTracking()
            .Where(x => x.DepartmentId != null)
            .OrderBy(x => x.Name)
            .Select(x => new AttendanceDepartmentRecord(x.DepartmentId!.Value, x.Name ?? string.Empty))
            .ToArrayAsync(cancellationToken);

        return Result<AttendanceDepartmentRecord[]>.Success(departments);
    }

    public async Task<Result<AttendanceDepartmentRecord?>> GetByIdAsync(
        int departmentId,
        CancellationToken cancellationToken = default)
    {
        if (departmentId <= 0)
        {
            return Result<AttendanceDepartmentRecord?>.Failure(new Error("ValidationError", "DepartmentId must be greater than zero."));
        }

        var department = await attendanceDbContext.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DepartmentId == departmentId, cancellationToken);

        return Result<AttendanceDepartmentRecord?>.Success(department is null
            ? null
            : new AttendanceDepartmentRecord(department.DepartmentId ?? 0, department.Name?.Trim() ?? string.Empty));
    }

    private static bool TryParseAttendanceUserId(string? attendanceUserId, out int userId) =>
        int.TryParse(attendanceUserId, out userId) && userId > 0;

    private async Task<HashSet<string>> GetCurrentEmployeeCodesAsync(CancellationToken cancellationToken) =>
        (await attendanceDbContext.CurrentEmployees.AsNoTracking()
            .Select(x => x.EmployeeCode)
            .ToArrayAsync(cancellationToken))
            .Select(NormalizeCode)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeCode(string? value) => value?.Trim() ?? string.Empty;
}
