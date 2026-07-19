using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Workers;

public sealed class WorkerPhotoService(
    AppDbContext dbContext,
    IWorkerPhotoStorage storage,
    IAuditEngine auditEngine,
    ILogger<WorkerPhotoService> logger) : IWorkerPhotoService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> WorkerOperationLocks = new();

    public Task<Result<WorkerPhotoChangeResult>> UploadAsync(
        Guid workerId,
        Stream content,
        long declaredLength,
        string? declaredContentType,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default) =>
        WithWorkerLockAsync(
            workerId,
            () => UploadCoreAsync(
                workerId,
                content,
                declaredLength,
                declaredContentType,
                actorUserId,
                requestMeta,
                cancellationToken),
            cancellationToken);

    private async Task<Result<WorkerPhotoChangeResult>> UploadCoreAsync(
        Guid workerId,
        Stream content,
        long declaredLength,
        string? declaredContentType,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        var identityError = ValidateWriteIdentity(workerId, actorUserId);
        if (identityError is not null) return Result<WorkerPhotoChangeResult>.Failure(identityError);
        if (content is null || !content.CanRead)
            return Result<WorkerPhotoChangeResult>.Failure(new Error("ValidationError", "A readable photo stream is required."));
        if (declaredLength <= 0)
            return Result<WorkerPhotoChangeResult>.Failure(new Error("ValidationError", "Worker photo cannot be empty."));
        if (declaredLength > WorkerPhotoFormat.MaximumBytes)
            return Result<WorkerPhotoChangeResult>.Failure(PhotoTooLargeError());

        var worker = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId, cancellationToken);
        if (worker is null)
            return Result<WorkerPhotoChangeResult>.Failure(new Error("NotFound", "Worker photo not found."));

        byte[] bytes;
        try
        {
            bytes = await ReadBoundedAsync(content, cancellationToken);
        }
        catch (WorkerPhotoSizeException)
        {
            return Result<WorkerPhotoChangeResult>.Failure(PhotoTooLargeError());
        }

        if (bytes.LongLength != declaredLength)
        {
            return Result<WorkerPhotoChangeResult>.Failure(new Error(
                "ValidationError",
                "Worker photo length does not match the uploaded content."));
        }

        if (!WorkerPhotoFormat.TryDetect(bytes, out var format))
        {
            return Result<WorkerPhotoChangeResult>.Failure(new Error(
                "UnsupportedPhotoType",
                "Only structurally valid JPEG, PNG, and BMP worker photos are allowed."));
        }

        if (!WorkerPhotoFormat.IsDeclaredContentTypeCompatible(declaredContentType, format.ContentType))
        {
            return Result<WorkerPhotoChangeResult>.Failure(new Error(
                "UnsupportedPhotoType",
                "Declared worker photo type does not match its content."));
        }

        var version = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var photoReference = WorkerPhotoReference.Build(workerId, version);
        var metadata = new WorkerPhotoMetadata(workerId, photoReference, version, format.ContentType, bytes.LongLength);
        var previousReference = worker.PhotoReference;
        var previousUpdatedAt = worker.UpdatedAtUtc;
        var hadManagedPhoto = WorkerPhotoReference.TryParse(previousReference, workerId, out var previousVersion);

        try
        {
            await storage.StoreAsync(workerId, version, bytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to store worker photo for {WorkerId}.", workerId);
            return Result<WorkerPhotoChangeResult>.Failure(new Error(
                "WorkerPhotoStorageError",
                "Unable to store the worker photo."));
        }

        if (hadManagedPhoto && previousVersion.Equals(version, StringComparison.OrdinalIgnoreCase))
        {
            return Result<WorkerPhotoChangeResult>.Success(new WorkerPhotoChangeResult(
                metadata,
                Created: false,
                Replaced: false,
                Unchanged: true));
        }

        var now = DateTime.UtcNow;
        worker.SetPhotoReference(photoReference, now);
        var before = hadManagedPhoto
            ? new WorkerPhotoAuditSnapshot(workerId, previousReference, previousVersion, null, null, "Local")
            : null;
        var after = new WorkerPhotoAuditSnapshot(workerId, photoReference, version, format.ContentType, bytes.LongLength, "LocalUpload");

        try
        {
            var audit = await auditEngine.RecordAsync(
                actorUserId,
                hadManagedPhoto ? AuditActionType.Update : AuditActionType.Create,
                "WorkerPhoto",
                workerId.ToString(),
                before,
                after,
                requestMeta,
                cancellationToken);
            if (audit.IsFailure)
            {
                worker.SetPhotoReference(previousReference, previousUpdatedAt);
                return Result<WorkerPhotoChangeResult>.Failure(audit.Error!);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            worker.SetPhotoReference(previousReference, previousUpdatedAt);
            throw;
        }
        catch (Exception exception)
        {
            worker.SetPhotoReference(previousReference, previousUpdatedAt);
            logger.LogError(exception, "Unable to persist worker photo reference for {WorkerId}.", workerId);
            return Result<WorkerPhotoChangeResult>.Failure(new Error(
                "WorkerPhotoPersistenceError",
                "Unable to persist the worker photo."));
        }

        if (hadManagedPhoto && !previousVersion.Equals(version, StringComparison.OrdinalIgnoreCase))
        {
            await DeleteObsoletePhotoAsync(workerId, previousVersion, cancellationToken);
        }

        return Result<WorkerPhotoChangeResult>.Success(new WorkerPhotoChangeResult(
            metadata,
            Created: !hadManagedPhoto,
            Replaced: hadManagedPhoto,
            Unchanged: false));
    }

    public async Task<Result<WorkerPhotoDownload>> DownloadAsync(
        Guid workerId,
        string? requestedVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (workerId == Guid.Empty)
            return Result<WorkerPhotoDownload>.Failure(new Error("NotFound", "Worker photo not found."));

        var photoReference = await dbContext.Workers
            .AsNoTracking()
            .Where(x => x.Id == workerId)
            .Select(x => x.PhotoReference)
            .SingleOrDefaultAsync(cancellationToken);
        if (!WorkerPhotoReference.TryParse(photoReference, workerId, out var currentVersion))
            return Result<WorkerPhotoDownload>.Failure(new Error("NotFound", "Worker photo not found."));

        if (!string.IsNullOrWhiteSpace(requestedVersion)
            && (!WorkerPhotoReference.IsValidVersion(requestedVersion)
                || !currentVersion.Equals(requestedVersion, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<WorkerPhotoDownload>.Failure(new Error("NotFound", "Worker photo not found."));
        }

        try
        {
            var stored = await storage.ReadAsync(workerId, currentVersion, cancellationToken);
            return stored is null
                ? Result<WorkerPhotoDownload>.Failure(new Error("NotFound", "Worker photo not found."))
                : Result<WorkerPhotoDownload>.Success(new WorkerPhotoDownload(
                    stored.Content,
                    stored.ContentType,
                    stored.Version));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to read worker photo for {WorkerId}.", workerId);
            return Result<WorkerPhotoDownload>.Failure(new Error(
                "WorkerPhotoStorageError",
                "Unable to read the worker photo."));
        }
    }

    public Task<Result> DeleteAsync(
        Guid workerId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default) =>
        WithWorkerLockAsync(
            workerId,
            () => DeleteCoreAsync(workerId, actorUserId, requestMeta, cancellationToken),
            cancellationToken);

    private async Task<Result> DeleteCoreAsync(
        Guid workerId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        var identityError = ValidateWriteIdentity(workerId, actorUserId);
        if (identityError is not null) return Result.Failure(identityError);

        var worker = await dbContext.Workers.FirstOrDefaultAsync(x => x.Id == workerId, cancellationToken);
        if (worker is null || !WorkerPhotoReference.TryParse(worker.PhotoReference, workerId, out var version))
            return Result.Failure(new Error("NotFound", "Worker photo not found."));

        var previousReference = worker.PhotoReference;
        var previousUpdatedAt = worker.UpdatedAtUtc;
        worker.SetPhotoReference(null, DateTime.UtcNow);

        try
        {
            var audit = await auditEngine.RecordAsync(
                actorUserId,
                AuditActionType.Delete,
                "WorkerPhoto",
                workerId.ToString(),
                new WorkerPhotoAuditSnapshot(workerId, previousReference, version, null, null, "Local"),
                after: null,
                requestMeta,
                cancellationToken);
            if (audit.IsFailure)
            {
                worker.SetPhotoReference(previousReference, previousUpdatedAt);
                return audit;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            worker.SetPhotoReference(previousReference, previousUpdatedAt);
            throw;
        }
        catch (Exception exception)
        {
            worker.SetPhotoReference(previousReference, previousUpdatedAt);
            logger.LogError(exception, "Unable to delete worker photo reference for {WorkerId}.", workerId);
            return Result.Failure(new Error("WorkerPhotoPersistenceError", "Unable to delete the worker photo."));
        }

        await DeleteObsoletePhotoAsync(workerId, version, cancellationToken);
        return Result.Success();
    }

    private static Error? ValidateWriteIdentity(Guid workerId, Guid actorUserId)
    {
        if (actorUserId == Guid.Empty) return new Error("Unauthorized", "User context is required.");
        return workerId == Guid.Empty ? new Error("ValidationError", "WorkerId is required.") : null;
    }

    private static Error PhotoTooLargeError() => new(
        "PhotoTooLarge",
        $"Worker photo must not exceed {WorkerPhotoFormat.MaximumBytes} bytes.");

    private static async Task<byte[]> ReadBoundedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > WorkerPhotoFormat.MaximumBytes) throw new WorkerPhotoSizeException();
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private async Task DeleteObsoletePhotoAsync(Guid workerId, string version, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(workerId, version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Cleanup of obsolete worker photo {PhotoVersion} for {WorkerId} was cancelled.", version, workerId);
        }
        catch (Exception exception)
        {
            // The DB pointer is authoritative. A failed best-effort cleanup can
            // leave an unreachable local object but cannot expose the old photo.
            logger.LogWarning(exception, "Unable to clean obsolete worker photo {PhotoVersion} for {WorkerId}.", version, workerId);
        }
    }

    private sealed record WorkerPhotoAuditSnapshot(
        Guid WorkerId,
        string? PhotoReference,
        string Version,
        string? ContentType,
        long? Length,
        string Source);

    private sealed class WorkerPhotoSizeException : Exception;

    private static async Task<T> WithWorkerLockAsync<T>(
        Guid workerId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var gate = WorkerOperationLocks.GetOrAdd(workerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            gate.Release();
        }
    }
}
