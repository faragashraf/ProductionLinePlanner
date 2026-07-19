namespace ProductionLinePlanner.Application.Workers;

/// <summary>
/// Planner-owned binary storage. Keys are generated from a worker id and a
/// SHA-256 version; callers never supply a filesystem path.
/// </summary>
public interface IWorkerPhotoStorage
{
    Task<WorkerPhotoStorageWriteResult> StoreAsync(
        Guid workerId,
        string version,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    Task<WorkerPhotoStorageObject?> ReadAsync(
        Guid workerId,
        string version,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid workerId,
        string version,
        CancellationToken cancellationToken = default);
}

public sealed record WorkerPhotoStorageObject(
    byte[] Content,
    string ContentType,
    string Version);

public sealed record WorkerPhotoStorageWriteResult(bool Created);
