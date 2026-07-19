using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Domain.Enums;
using System.Security.Cryptography;

namespace ProductionLinePlanner.Tests;

public sealed class FakeAttendanceEmployeeReader : IAttendanceEmployeeReader, IAttendanceWorkerPhotoReader
{
    private readonly Dictionary<string, AttendanceEmployeeRecord?> _employees;
    private readonly Func<string, CancellationToken, Task<Result<AttendanceEmployeeRecord?>>> _getByAttendanceUserIdAsync;
    private readonly Func<CancellationToken, Task<Result<AttendanceEmployeeRecord[]>>> _getAllAsync;
    private readonly Func<CancellationToken, Task<Result<AttendanceWorkerPhotoRecord[]>>> _getAllPhotosAsync;
    private readonly Func<string, CancellationToken, Task<Result<AttendanceWorkerPhotoRecord?>>> _getPhotoByAttendanceUserIdAsync;

    public FakeAttendanceEmployeeReader(
        Dictionary<string, AttendanceEmployeeRecord?>? employees = null,
        Func<string, CancellationToken, Task<Result<AttendanceEmployeeRecord?>>>? getByAttendanceUserIdAsync = null,
        Func<CancellationToken, Task<Result<AttendanceEmployeeRecord[]>>>? getAllAsync = null,
        AttendanceWorkerPhotoRecord[]? photos = null)
    {
        _employees = employees ?? [];
        _getByAttendanceUserIdAsync = getByAttendanceUserIdAsync ?? DefaultGetByAttendanceUserIdAsync;
        _getAllAsync = getAllAsync ?? DefaultGetAllAsync;
        Photos = (photos ?? []).ToList();
        _getAllPhotosAsync = _ => Task.FromResult(Result<AttendanceWorkerPhotoRecord[]>.Success(Photos.ToArray()));
        _getPhotoByAttendanceUserIdAsync = (attendanceUserId, _) => Task.FromResult(
            Photos.FirstOrDefault(photo => string.Equals(photo.AttendanceUserId, attendanceUserId, StringComparison.OrdinalIgnoreCase)) is { } photo
                ? Result<AttendanceWorkerPhotoRecord?>.Success(photo)
                : Result<AttendanceWorkerPhotoRecord?>.Success(null));
    }

    public List<AttendanceWorkerPhotoRecord> Photos { get; }

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

    public Task<Result<AttendanceWorkerPhotoRecord[]>> GetAllCurrentPhotosAsync(CancellationToken cancellationToken = default) =>
        _getAllPhotosAsync(cancellationToken);

    public Task<Result<AttendanceWorkerPhotoRecord?>> GetPhotoByAttendanceUserIdAsync(
        string attendanceUserId,
        CancellationToken cancellationToken = default) =>
        _getPhotoByAttendanceUserIdAsync(attendanceUserId, cancellationToken);

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

public sealed class InMemoryWorkerPhotoCache : IWorkerPhotoCache
{
    private readonly Dictionary<Guid, WorkerPhotoCacheEntry> _entries = [];

    public int GetCalls { get; private set; }
    public int StoreCalls { get; private set; }
    public int RemoveCalls { get; private set; }

    public Task<WorkerPhotoCacheEntry?> GetAsync(Guid workerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetCalls++;
        return Task.FromResult(_entries.TryGetValue(workerId, out var entry) ? entry : null);
    }

    public Task<WorkerPhotoCacheStoreResult> StoreAsync(Guid workerId, byte[] photo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoreCalls++;
        if (!WorkerPhotoFormat.TryGetContentType(photo, out var contentType))
        {
            throw new InvalidOperationException("Worker photo format is invalid or unsupported.");
        }

        var version = Convert.ToHexString(SHA256.HashData(photo)).ToLowerInvariant()[..16];
        if (_entries.TryGetValue(workerId, out var current) && current.Version == version)
        {
            return Task.FromResult(new WorkerPhotoCacheStoreResult(contentType, version, false, false, true));
        }

        _entries[workerId] = new WorkerPhotoCacheEntry(photo.ToArray(), contentType, version);
        return Task.FromResult(new WorkerPhotoCacheStoreResult(contentType, version, current is null, current is not null, false));
    }

    public Task RemoveAsync(Guid workerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveCalls++;
        _entries.Remove(workerId);
        return Task.CompletedTask;
    }
}
