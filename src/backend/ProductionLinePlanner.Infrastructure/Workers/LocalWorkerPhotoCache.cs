using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using ProductionLinePlanner.Application.Workers;

namespace ProductionLinePlanner.Infrastructure.Workers;

/// <summary>
/// Stores trusted, synchronization-produced worker images outside wwwroot. The
/// only caller that reads ZKTime is the worker synchronization service; normal
/// browser delivery reads this cache exclusively.
/// </summary>
public sealed class LocalWorkerPhotoCache(IConfiguration configuration) : IWorkerPhotoCache
{
    private const string CacheDirectoryName = "worker-photos";

    public async Task<WorkerPhotoCacheEntry?> GetAsync(Guid workerId, CancellationToken cancellationToken = default)
    {
        var path = GetPhotoPath(workerId);
        if (!File.Exists(path)) return null;

        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!WorkerPhotoFormat.TryGetContentType(content, out var contentType))
        {
            return null;
        }

        return new WorkerPhotoCacheEntry(content, contentType, GetVersion(content));
    }

    public async Task<WorkerPhotoCacheStoreResult> StoreAsync(Guid workerId, byte[] photo, CancellationToken cancellationToken = default)
    {
        if (!WorkerPhotoFormat.TryGetContentType(photo, out var contentType))
        {
            throw new InvalidOperationException("Worker photo format is invalid or unsupported.");
        }

        var version = GetVersion(photo);
        var existing = await GetAsync(workerId, cancellationToken);
        if (existing is not null && string.Equals(existing.Version, version, StringComparison.Ordinal))
        {
            return new WorkerPhotoCacheStoreResult(contentType, version, Created: false, Updated: false, Unchanged: true);
        }

        var directory = GetCacheDirectory();
        Directory.CreateDirectory(directory);
        var destination = GetPhotoPath(workerId);
        var temporary = Path.Combine(directory, $"{workerId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, photo, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return new WorkerPhotoCacheStoreResult(contentType, version, Created: existing is null, Updated: existing is not null, Unchanged: false);
    }

    public Task RemoveAsync(Guid workerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPhotoPath(workerId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetCacheDirectory()
    {
        var configuredRoot = configuration["WorkerPhotoCache:RootPath"]?.Trim();
        var applicationRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "App_Data")
            : configuredRoot;
        return Path.Combine(applicationRoot, CacheDirectoryName);
    }

    private string GetPhotoPath(Guid workerId) => Path.Combine(GetCacheDirectory(), $"{workerId:N}.photo");

    private static string GetVersion(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()[..16];
}
