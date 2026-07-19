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

public sealed class InMemoryWorkerPhotoStorage : IWorkerPhotoStorage
{
    private readonly Dictionary<(Guid WorkerId, string Version), WorkerPhotoStorageObject> entries = [];

    public int ReadCalls { get; private set; }
    public int StoreCalls { get; private set; }
    public int DeleteCalls { get; private set; }

    public Task<WorkerPhotoStorageObject?> ReadAsync(
        Guid workerId,
        string version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCalls++;
        return Task.FromResult(entries.TryGetValue((workerId, version), out var entry) ? entry : null);
    }

    public Task<WorkerPhotoStorageWriteResult> StoreAsync(
        Guid workerId,
        string version,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoreCalls++;
        if (!WorkerPhotoFormat.TryDetect(content.Span, out var format))
        {
            throw new InvalidOperationException("Worker photo content is invalid or unsupported.");
        }

        var actualVersion = Convert.ToHexString(SHA256.HashData(content.Span)).ToLowerInvariant();
        if (!actualVersion.Equals(version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Worker photo version does not match its content.");
        }

        var created = !entries.ContainsKey((workerId, actualVersion));
        entries[(workerId, actualVersion)] = new WorkerPhotoStorageObject(content.ToArray(), format.ContentType, actualVersion);
        return Task.FromResult(new WorkerPhotoStorageWriteResult(created));
    }

    public Task DeleteAsync(Guid workerId, string version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteCalls++;
        entries.Remove((workerId, version));
        return Task.CompletedTask;
    }
}
