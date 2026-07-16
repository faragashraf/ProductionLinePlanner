using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductionLinePlanner.Api.Endpoints;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class WorkerPhotoEndpointTests
{
    [Fact]
    public async Task Worker_119_with_a_managed_reference_serves_cached_zktime_bmp_without_live_source_access()
    {
        await using var db = CreateContext();
        var worker = new Worker(Guid.NewGuid(), "119", "Worker 119", attendanceUserId: "1", badgeNumber: "119");
        var cache = new InMemoryWorkerPhotoCache();
        var bmp = CreateBmp();
        var stored = await cache.StoreAsync(worker.Id, bmp);
        worker.SetPhotoReference($"/api/workers/{worker.Id:D}/photo?v={stored.Version}", DateTime.UtcNow);
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var context = CreateHttpContext();
        var result = await WorkerPhotoEndpoint.GetAsync(worker.Id, db, cache, context, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("image/bmp", context.Response.ContentType);
        Assert.Equal("private, max-age=3600", context.Response.Headers.CacheControl);
        Assert.Equal($"\"{stored.Version}\"", context.Response.Headers.ETag);
        Assert.Equal(bmp, ((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal(1, cache.GetCalls);
    }

    [Fact]
    public async Task Missing_managed_reference_or_cache_entry_returns_404_and_no_store()
    {
        await using var db = CreateContext();
        var worker = new Worker(Guid.NewGuid(), "119", "Worker 119", attendanceUserId: "1", badgeNumber: "119");
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var context = CreateHttpContext();
        var result = await WorkerPhotoEndpoint.GetAsync(worker.Id, db, new InMemoryWorkerPhotoCache(), context, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task Matching_etag_returns_304_from_the_cached_photo()
    {
        await using var db = CreateContext();
        var worker = new Worker(Guid.NewGuid(), "119", "Worker 119");
        var cache = new InMemoryWorkerPhotoCache();
        var stored = await cache.StoreAsync(worker.Id, CreateBmp());
        worker.SetPhotoReference($"/api/workers/{worker.Id:D}/photo?v={stored.Version}", DateTime.UtcNow);
        db.Workers.Add(worker);
        await db.SaveChangesAsync();

        var context = CreateHttpContext();
        context.Request.Headers.IfNoneMatch = $"\"{stored.Version}\"";
        var result = await WorkerPhotoEndpoint.GetAsync(worker.Id, db, cache, context, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status304NotModified, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        return context;
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static byte[] CreateBmp()
    {
        var bytes = new byte[54];
        bytes[0] = 0x42;
        bytes[1] = 0x4D;
        return bytes;
    }
}
