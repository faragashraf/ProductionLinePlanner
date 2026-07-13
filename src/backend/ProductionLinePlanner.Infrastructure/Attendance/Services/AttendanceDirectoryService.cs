using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Attendance.Entities;
using Microsoft.Extensions.Options;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

public sealed class AttendanceDirectoryService(
    AttendanceDbContext attendanceDbContext,
    IOptions<AttendanceSourceOptions> sourceOptions) : IAttendanceEmployeeReader, IAttendanceEmployeeWriter, IAttendanceDepartmentReader, IAttendanceDepartmentWriter
{
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
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (entity is null)
        {
            return Result<AttendanceEmployeeRecord?>.Success(null);
        }

        return Result<AttendanceEmployeeRecord?>.Success(new AttendanceEmployeeRecord(
            AttendanceUserId: userId.ToString(),
            DepartmentId: entity.DepartmentId,
            BadgeNumber: entity.BadgeNumber,
            Name: entity.Name,
            IsActive: true));
    }

    public async Task<Result<AttendanceEmployeeRecord[]>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var sourceUsers = await attendanceDbContext.UserInfos
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var records = sourceUsers
            .Select(x => new AttendanceEmployeeRecord(
                AttendanceUserId: x.UserId?.ToString(),
                DepartmentId: x.DepartmentId,
                BadgeNumber: x.BadgeNumber,
                Name: x.Name,
                IsActive: true))
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

        if (department is null)
        {
            return Result<AttendanceDepartmentRecord?>.Success(null);
        }

        return Result<AttendanceDepartmentRecord?>.Success(new AttendanceDepartmentRecord(
            department.DepartmentId ?? 0,
            department.Name?.Trim() ?? string.Empty));
    }

    public async Task<Result> UpdateWorkerFullNameAsync(string attendanceUserId, string fullName, CancellationToken cancellationToken = default)
    {
        if (!TryParseAttendanceUserId(attendanceUserId, out var userId))
        {
            return Result.Failure(new Error("ValidationError", "AttendanceUserId must be a valid integer."));
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return Result.Failure(new Error("ValidationError", "FullName is required."));
        }

        var table = GetUserInfoTableName();
        var rows = await attendanceDbContext.Database.ExecuteSqlRawAsync(
            $"UPDATE [{table}] SET [Name] = @Name WHERE [USERID] = @UserId",
            new SqlParameter("@Name", fullName.Trim()),
            new SqlParameter("@UserId", userId),
            cancellationToken);

        if (rows == 0)
        {
            return Result.Failure(new Error("NotFound", "Worker was not found in attendance source."));
        }

        return Result.Success();
    }

    public async Task<Result> UpdateWorkerDepartmentAsync(string attendanceUserId, int departmentId, CancellationToken cancellationToken = default)
    {
        if (!TryParseAttendanceUserId(attendanceUserId, out var userId))
        {
            return Result.Failure(new Error("ValidationError", "AttendanceUserId must be a valid integer."));
        }

        if (departmentId <= 0)
        {
            return Result.Failure(new Error("ValidationError", "DepartmentId must be greater than zero."));
        }

        var existsDepartment = await attendanceDbContext.Departments
            .AsNoTracking()
            .AnyAsync(x => x.DepartmentId == departmentId, cancellationToken);
        if (!existsDepartment)
        {
            return Result.Failure(new Error("NotFound", "Department was not found in attendance source."));
        }

        var table = GetUserInfoTableName();
        var rows = await attendanceDbContext.Database.ExecuteSqlRawAsync(
            $"UPDATE [{table}] SET [DEFAULTDEPTID] = @DepartmentId WHERE [USERID] = @UserId",
            new SqlParameter("@DepartmentId", departmentId),
            new SqlParameter("@UserId", userId),
            cancellationToken);

        if (rows == 0)
        {
            return Result.Failure(new Error("NotFound", "Worker was not found in attendance source."));
        }

        return Result.Success();
    }

    public async Task<Result<AttendanceDepartmentRecord>> CreateDepartmentAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result<AttendanceDepartmentRecord>.Failure(new Error("ValidationError", "Department name is required."));
        }

        var exists = await attendanceDbContext.Departments
            .AsNoTracking()
            .AnyAsync(x => x.Name == normalizedName, cancellationToken);
        if (exists)
        {
            return Result<AttendanceDepartmentRecord>.Failure(new Error("Conflict", "Department name must be unique."));
        }

        var departmentsTable = GetDepartmentsTableName();
        var nextDepartmentId = await attendanceDbContext.Departments
            .AsNoTracking()
            .Select(x => x.DepartmentId ?? 0)
            .DefaultIfEmpty(0)
            .MaxAsync(cancellationToken) + 1;

        var command = $"INSERT INTO [{departmentsTable}] ([DEPTID], [DEPTNAME]) VALUES (@DepartmentId, @DepartmentName)";
        var inserted = await attendanceDbContext.Database.ExecuteSqlRawAsync(
            command,
            new SqlParameter("@DepartmentId", nextDepartmentId),
            new SqlParameter("@DepartmentName", normalizedName),
            cancellationToken);

        if (inserted == 0)
        {
            return Result<AttendanceDepartmentRecord>.Failure(new Error("Conflict", "Unable to create department in attendance source."));
        }

        return Result<AttendanceDepartmentRecord>.Success(new AttendanceDepartmentRecord(nextDepartmentId, normalizedName));
    }

    public async Task<Result> UpdateDepartmentNameAsync(int departmentId, string name, CancellationToken cancellationToken = default)
    {
        if (departmentId <= 0)
        {
            return Result.Failure(new Error("ValidationError", "DepartmentId must be greater than zero."));
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result.Failure(new Error("ValidationError", "Department name is required."));
        }

        var exists = await attendanceDbContext.Departments
            .AsNoTracking()
            .AnyAsync(x => x.Name == normalizedName && x.DepartmentId != departmentId, cancellationToken);
        if (exists)
        {
            return Result.Failure(new Error("Conflict", "Department name must be unique."));
        }

        var departmentsTable = GetDepartmentsTableName();
        var updated = await attendanceDbContext.Database.ExecuteSqlRawAsync(
            $"UPDATE [{departmentsTable}] SET [DEPTNAME] = @DepartmentName WHERE [DEPTID] = @DepartmentId",
            new SqlParameter("@DepartmentName", normalizedName),
            new SqlParameter("@DepartmentId", departmentId),
            cancellationToken);

        if (updated == 0)
        {
            return Result.Failure(new Error("NotFound", "Department was not found in attendance source."));
        }

        return Result.Success();
    }

    private static bool TryParseAttendanceUserId(string? attendanceUserId, out int userId)
    {
        userId = 0;
        return int.TryParse(attendanceUserId, out userId);
    }

    private string GetUserInfoTableName() => sourceOptions.Value.UserInfoTable ?? "USERINFO";

    private string GetDepartmentsTableName() => sourceOptions.Value.DepartmentsTable ?? "DEPARTMENTS";
}
