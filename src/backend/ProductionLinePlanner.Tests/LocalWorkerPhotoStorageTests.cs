using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using ProductionLinePlanner.Infrastructure.Workers;

namespace ProductionLinePlanner.Tests;

public sealed class LocalWorkerPhotoStorageTests
{
    [Fact]
    public async Task Storage_uses_generated_versioned_paths_and_rejects_path_like_versions()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"plp-worker-photo-{Guid.NewGuid():N}");
        try
        {
            var storage = CreateStorage(testRoot);
            var workerId = Guid.NewGuid();
            var content = WorkerPhotoTestData.CreateBitmap();
            var version = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

            var write = await storage.StoreAsync(workerId, version, content);
            var read = await storage.ReadAsync(workerId, version);

            Assert.True(write.Created);
            Assert.NotNull(read);
            Assert.Equal(content, read!.Content);
            Assert.Equal("image/bmp", read.ContentType);
            var storedFile = Assert.Single(Directory.GetFiles(testRoot, "*.photo", SearchOption.AllDirectories));
            Assert.Equal($"{version}.photo", Path.GetFileName(storedFile));
            await Assert.ThrowsAsync<ArgumentException>(() => storage.ReadAsync(workerId, "../outside"));
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Storage_reads_a_legacy_cache_file_only_when_its_hash_matches_the_reference()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"plp-worker-photo-{Guid.NewGuid():N}");
        try
        {
            var storage = CreateStorage(testRoot);
            var workerId = Guid.NewGuid();
            var content = WorkerPhotoTestData.CreateBitmap();
            var version = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var legacyDirectory = Path.Combine(testRoot, "worker-photos");
            Directory.CreateDirectory(legacyDirectory);
            await File.WriteAllBytesAsync(Path.Combine(legacyDirectory, $"{workerId:N}.photo"), content);

            Assert.NotNull(await storage.ReadAsync(workerId, version));
            Assert.NotNull(await storage.ReadAsync(workerId, version[..16]));
            Assert.Null(await storage.ReadAsync(workerId, new string('a', 64)));
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void Storage_rejects_a_configured_root_under_wwwroot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"plp-worker-photo-web-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WorkerPhotos:RootPath"] = Path.Combine(contentRoot, "wwwroot", "protected-data")
                })
                .Build();

            var exception = Assert.Throws<InvalidOperationException>(() => new LocalWorkerPhotoStorage(configuration));

            Assert.Equal("Worker photo storage must be outside wwwroot.", exception.Message);
        }
        finally
        {
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static LocalWorkerPhotoStorage CreateStorage(string root) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WorkerPhotos:RootPath"] = root })
            .Build());

}
