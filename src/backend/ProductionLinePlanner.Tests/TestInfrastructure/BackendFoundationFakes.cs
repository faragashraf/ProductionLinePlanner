using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Tests;

public sealed class FakeAttendanceEmployeeWriter : IAttendanceEmployeeWriter
{
    private readonly Dictionary<string, AttendanceEmployeeRecord?> _employees;
    private readonly Func<string, string, CancellationToken, Task<Result>> _updateWorkerFullNameAsync;
    private readonly Func<string, int, CancellationToken, Task<Result>> _updateWorkerDepartmentAsync;

    public FakeAttendanceEmployeeWriter(
        Dictionary<string, AttendanceEmployeeRecord?>? employees = null,
        Func<string, string, CancellationToken, Task<Result>>? updateWorkerFullNameAsync = null,
        Func<string, int, CancellationToken, Task<Result>>? updateWorkerDepartmentAsync = null)
    {
        _employees = employees ?? [];
        _updateWorkerFullNameAsync = updateWorkerFullNameAsync ?? DefaultUpdateWorkerFullNameAsync;
        _updateWorkerDepartmentAsync = updateWorkerDepartmentAsync ?? DefaultUpdateWorkerDepartmentAsync;
    }

    public List<(string AttendanceUserId, string FullName)> FullNameUpdates { get; } = [];
    public List<(string AttendanceUserId, int DepartmentId)> DepartmentUpdates { get; } = [];

    public Task<Result> UpdateWorkerFullNameAsync(
        string attendanceUserId,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        return _updateWorkerFullNameAsync(attendanceUserId, fullName, cancellationToken);
    }

    public Task<Result> UpdateWorkerDepartmentAsync(
        string attendanceUserId,
        int departmentId,
        CancellationToken cancellationToken = default)
    {
        return _updateWorkerDepartmentAsync(attendanceUserId, departmentId, cancellationToken);
    }

    private Task<Result> DefaultUpdateWorkerFullNameAsync(string attendanceUserId, string fullName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        FullNameUpdates.Add((attendanceUserId, fullName));

        if (!_employees.TryGetValue(attendanceUserId, out var record))
        {
            return Task.FromResult(Result.Failure(new Error("NotFound", "Worker was not found in attendance source.")));
        }

        if (record is null)
        {
            return Task.FromResult(Result.Failure(new Error("NotFound", "Worker was not found in attendance source.")));
        }

        _employees[attendanceUserId] = record with
        {
            Name = fullName
        };

        return Task.FromResult(Result.Success());
    }

    private Task<Result> DefaultUpdateWorkerDepartmentAsync(string attendanceUserId, int departmentId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        DepartmentUpdates.Add((attendanceUserId, departmentId));

        if (!_employees.TryGetValue(attendanceUserId, out var record))
        {
            return Task.FromResult(Result.Failure(new Error("NotFound", "Worker was not found in attendance source.")));
        }

        if (record is null)
        {
            return Task.FromResult(Result.Failure(new Error("NotFound", "Worker was not found in attendance source.")));
        }

        _employees[attendanceUserId] = record with
        {
            DepartmentId = departmentId
        };

        return Task.FromResult(Result.Success());
    }
}

public sealed class FakeAttendanceEmployeeReader : IAttendanceEmployeeReader
{
    private readonly Dictionary<string, AttendanceEmployeeRecord?> _employees;
    private readonly Func<string, CancellationToken, Task<Result<AttendanceEmployeeRecord?>>> _getByAttendanceUserIdAsync;
    private readonly Func<CancellationToken, Task<Result<AttendanceEmployeeRecord[]>>> _getAllAsync;

    public FakeAttendanceEmployeeReader(
        Dictionary<string, AttendanceEmployeeRecord?>? employees = null,
        Func<string, CancellationToken, Task<Result<AttendanceEmployeeRecord?>>>? getByAttendanceUserIdAsync = null,
        Func<CancellationToken, Task<Result<AttendanceEmployeeRecord[]>>>? getAllAsync = null)
    {
        _employees = employees ?? [];
        _getByAttendanceUserIdAsync = getByAttendanceUserIdAsync ?? DefaultGetByAttendanceUserIdAsync;
        _getAllAsync = getAllAsync ?? DefaultGetAllAsync;
    }

    public Task<Result<AttendanceEmployeeRecord?>> GetByAttendanceUserIdAsync(
        string attendanceUserId,
        CancellationToken cancellationToken = default)
    {
        return _getByAttendanceUserIdAsync(attendanceUserId, cancellationToken);
    }

    public Task<Result<AttendanceEmployeeRecord[]>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _getAllAsync(cancellationToken);
    }

    private Task<Result<AttendanceEmployeeRecord?>> DefaultGetByAttendanceUserIdAsync(
        string attendanceUserId,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _ = attendanceUserId;

        return Task.FromResult(_employees.TryGetValue(attendanceUserId, out var record)
            ? Result<AttendanceEmployeeRecord?>.Success(record)
            : Result<AttendanceEmployeeRecord?>.Success(null));
    }

    private Task<Result<AttendanceEmployeeRecord[]>> DefaultGetAllAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(
            Result<AttendanceEmployeeRecord[]>.Success(_employees.Values
                .OfType<AttendanceEmployeeRecord>()
                .ToArray()));
    }
}

public sealed class FakeAttendanceDepartmentReader : IAttendanceDepartmentReader
{
    private readonly Func<int, CancellationToken, Task<Result<AttendanceDepartmentRecord?>>> _getByIdAsync;

    public FakeAttendanceDepartmentReader(
        Dictionary<int, AttendanceDepartmentRecord>? departments = null,
        Func<int, CancellationToken, Task<Result<AttendanceDepartmentRecord?>>>? getByIdAsync = null)
    {
        Departments = departments ?? [];
        _getByIdAsync = getByIdAsync ?? DefaultGetByIdAsync;
    }

    public Dictionary<int, AttendanceDepartmentRecord> Departments { get; }

    public Task<Result<AttendanceDepartmentRecord[]>> GetAllDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<AttendanceDepartmentRecord[]>.Success(Departments.Values.ToArray()));
    }

    public Task<Result<AttendanceDepartmentRecord?>> GetByIdAsync(int departmentId, CancellationToken cancellationToken = default)
    {
        return _getByIdAsync(departmentId, cancellationToken);
    }

    private Task<Result<AttendanceDepartmentRecord?>> DefaultGetByIdAsync(int departmentId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(Departments.TryGetValue(departmentId, out var department)
            ? Result<AttendanceDepartmentRecord?>.Success(department)
            : Result<AttendanceDepartmentRecord?>.Success(null));
    }
}

public sealed class FakeAttendanceDepartmentWriter : IAttendanceDepartmentWriter
{
    private readonly Dictionary<int, AttendanceDepartmentRecord> _departments;
    private readonly Func<string, CancellationToken, Task<Result<AttendanceDepartmentRecord>>> _createDepartmentAsync;
    private readonly Func<int, string, CancellationToken, Task<Result>> _updateDepartmentNameAsync;

    public FakeAttendanceDepartmentWriter(
        Dictionary<int, AttendanceDepartmentRecord>? departments = null,
        Func<string, CancellationToken, Task<Result<AttendanceDepartmentRecord>>>? createDepartmentAsync = null,
        Func<int, string, CancellationToken, Task<Result>>? updateDepartmentNameAsync = null)
    {
        _departments = departments ?? [];
        _createDepartmentAsync = createDepartmentAsync ?? DefaultCreateDepartmentAsync;
        _updateDepartmentNameAsync = updateDepartmentNameAsync ?? DefaultUpdateDepartmentNameAsync;
    }

    public int NextDepartmentId { get; set; } = 1;
    public List<string> CreatedDepartments { get; } = [];
    public List<(int DepartmentId, string Name)> UpdatedDepartments { get; } = [];

    public Task<Result<AttendanceDepartmentRecord>> CreateDepartmentAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return _createDepartmentAsync(name, cancellationToken);
    }

    public Task<Result> UpdateDepartmentNameAsync(
        int departmentId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return _updateDepartmentNameAsync(departmentId, name, cancellationToken);
    }

    private Task<Result<AttendanceDepartmentRecord>> DefaultCreateDepartmentAsync(string name, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var trimmed = name.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Task.FromResult(Result<AttendanceDepartmentRecord>.Failure(new Error("ValidationError", "Department name is required.")));
        }

        if (_departments.Values.Any(x => string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Result<AttendanceDepartmentRecord>.Failure(new Error("Conflict", "Department name must be unique.")));
        }

        var id = NextDepartmentId++;
        var created = new AttendanceDepartmentRecord(id, trimmed);
        _departments[id] = created;
        CreatedDepartments.Add(trimmed);
        return Task.FromResult(Result<AttendanceDepartmentRecord>.Success(created));
    }

    private Task<Result> DefaultUpdateDepartmentNameAsync(int departmentId, string name, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var trimmed = name.Trim();

        if (_departments.TryGetValue(departmentId, out var existing) is false)
        {
            return Task.FromResult(Result.Failure(new Error("NotFound", "Department not found.")));
        }

        if (_departments.Values.Any(x => x.DepartmentId != departmentId && string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Result.Failure(new Error("Conflict", "Department name must be unique.")));
        }

        _departments[departmentId] = existing with { Name = trimmed };
        UpdatedDepartments.Add((departmentId, trimmed));
        return Task.FromResult(Result.Success());
    }
}

public sealed class RecordingAuditEngine : IAuditEngine
{
    public sealed record AuditCall(
        Guid ActorUserId,
        AuditActionType ActionType,
        string EntityType,
        string EntityId,
        object? Before,
        object? After,
        string? RequestMeta);

    public List<AuditCall> Calls { get; } = [];

    public Task<Result> RecordAsync(
        Guid actorUserId,
        AuditActionType actionType,
        string entityType,
        string entityId,
        object? before = null,
        object? after = null,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        Calls.Add(new AuditCall(actorUserId, actionType, entityType, entityId, before, after, requestMeta));
        return Task.FromResult(Result.Success());
    }
}
