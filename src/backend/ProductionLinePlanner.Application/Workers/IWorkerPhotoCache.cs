namespace ProductionLinePlanner.Application.Workers;

/// <summary>
/// Application-managed worker-photo cache. It deliberately has no dependency on
/// ZKTime so normal image delivery cannot read USERINFO.PHOTO.
/// </summary>
public interface IWorkerPhotoCache
{
    Task<WorkerPhotoCacheEntry?> GetAsync(Guid workerId, CancellationToken cancellationToken = default);
    Task<WorkerPhotoCacheStoreResult> StoreAsync(Guid workerId, byte[] photo, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid workerId, CancellationToken cancellationToken = default);
}

public sealed record WorkerPhotoCacheEntry(byte[] Content, string ContentType, string Version);

public sealed record WorkerPhotoCacheStoreResult(string ContentType, string Version, bool Created, bool Updated, bool Unchanged);
