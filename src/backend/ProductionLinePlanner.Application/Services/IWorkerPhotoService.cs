using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Services;

public interface IWorkerPhotoService
{
    Task<Result<WorkerPhotoChangeResult>> UploadAsync(
        Guid workerId,
        Stream content,
        long declaredLength,
        string? declaredContentType,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<WorkerPhotoDownload>> DownloadAsync(
        Guid workerId,
        string? requestedVersion = null,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        Guid workerId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);
}

public sealed record WorkerPhotoMetadata(
    Guid WorkerId,
    string PhotoReference,
    string Version,
    string ContentType,
    long Length);

public sealed record WorkerPhotoChangeResult(
    WorkerPhotoMetadata Photo,
    bool Created,
    bool Replaced,
    bool Unchanged);

public sealed record WorkerPhotoDownload(
    byte[] Content,
    string ContentType,
    string Version);
