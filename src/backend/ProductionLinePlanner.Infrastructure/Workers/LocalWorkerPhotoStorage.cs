using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using ProductionLinePlanner.Application.Workers;

namespace ProductionLinePlanner.Infrastructure.Workers;

/// <summary>
/// Versioned Planner-owned storage outside the web root. The provider accepts
/// only generated worker/version keys and never a caller-controlled path.
/// </summary>
public sealed class LocalWorkerPhotoStorage : IWorkerPhotoStorage
{
    private const string StorageDirectoryName = "worker-photos";
    private readonly string storageRoot;

    public LocalWorkerPhotoStorage(IConfiguration configuration)
    {
        var configuredRoot = configuration["WorkerPhotos:RootPath"]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            // Backward-compatible with deployments that configured the old cache root.
            configuredRoot = configuration["WorkerPhotoCache:RootPath"]?.Trim();
        }

        var applicationDataRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "App_Data")
            : Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(AppContext.BaseDirectory, configuredRoot);

        storageRoot = Path.GetFullPath(Path.Combine(applicationDataRoot, StorageDirectoryName));

        // A deployment may override the root, but it must never turn protected
        // worker photos into static content by locating the data directory under
        // the application's web root.
        if (storageRoot.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("wwwroot", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Worker photo storage must be outside wwwroot.");
        }
    }

    public async Task<WorkerPhotoStorageWriteResult> StoreAsync(
        Guid workerId,
        string version,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(workerId, version);
        if (!WorkerPhotoFormat.TryDetect(content.Span, out _))
        {
            throw new InvalidOperationException("Worker photo content is invalid or unsupported.");
        }

        var actualVersion = Convert.ToHexString(SHA256.HashData(content.Span)).ToLowerInvariant();
        if (!WorkerPhotoReference.IsFullVersion(version)
            || !actualVersion.Equals(version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Worker photo version does not match its content.");
        }

        if (await ReadAsync(workerId, version, cancellationToken) is not null)
        {
            return new WorkerPhotoStorageWriteResult(Created: false);
        }

        var workerDirectory = GetWorkerDirectory(workerId);
        Directory.CreateDirectory(workerDirectory);
        var destination = GetVersionedPath(workerId, version);
        var destinationExisted = File.Exists(destination);
        var temporary = Path.Combine(workerDirectory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, destination, overwrite: true);
            return new WorkerPhotoStorageWriteResult(Created: !destinationExisted);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<WorkerPhotoStorageObject?> ReadAsync(
        Guid workerId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(workerId, version);

        var versioned = await ReadAndValidateAsync(GetVersionedPath(workerId, version), version, cancellationToken);
        if (versioned is not null)
        {
            return versioned;
        }

        // Read-only compatibility with the former single-file cache. A legacy
        // object is accepted only when its SHA-256 matches the DB reference.
        return await ReadAndValidateAsync(GetLegacyPath(workerId), version, cancellationToken);
    }

    public async Task DeleteAsync(
        Guid workerId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(workerId, version);
        cancellationToken.ThrowIfCancellationRequested();

        var versionedPath = GetVersionedPath(workerId, version);
        if (File.Exists(versionedPath))
        {
            File.Delete(versionedPath);
        }

        var legacyPath = GetLegacyPath(workerId);
        if (await ReadAndValidateAsync(legacyPath, version, cancellationToken) is not null && File.Exists(legacyPath))
        {
            File.Delete(legacyPath);
        }
    }

    private async Task<WorkerPhotoStorageObject?> ReadAndValidateAsync(
        string path,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length <= 0 || fileInfo.Length > WorkerPhotoFormat.MaximumBytes)
        {
            return null;
        }

        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!WorkerPhotoFormat.TryDetect(content, out var format))
        {
            return null;
        }

        var actualVersion = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return WorkerPhotoReference.MatchesContentHash(actualVersion, expectedVersion)
            ? new WorkerPhotoStorageObject(content, format.ContentType, actualVersion)
            : null;
    }

    private string GetWorkerDirectory(Guid workerId) => Path.Combine(storageRoot, workerId.ToString("N"));

    private string GetVersionedPath(Guid workerId, string version) =>
        Path.Combine(GetWorkerDirectory(workerId), $"{version.ToLowerInvariant()}.photo");

    private string GetLegacyPath(Guid workerId) => Path.Combine(storageRoot, $"{workerId:N}.photo");

    private static void ValidateKey(Guid workerId, string version)
    {
        if (workerId == Guid.Empty) throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (!WorkerPhotoReference.IsValidVersion(version))
            throw new ArgumentException("Version must be a SHA-256 hex value.", nameof(version));
    }

}
