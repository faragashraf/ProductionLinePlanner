using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Api.Endpoints;

public static class WorkerPhotoEndpoint
{
    public static async Task<IResult> GetAsync(
        Guid workerId,
        AppDbContext dbContext,
        IWorkerPhotoCache workerPhotoCache,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var worker = await dbContext.Workers
            .AsNoTracking()
            .Where(x => x.Id == workerId)
            .Select(x => new { x.PhotoReference })
            .SingleOrDefaultAsync(cancellationToken);

        if (worker is null || string.IsNullOrWhiteSpace(worker.PhotoReference))
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            return Results.NotFound();
        }

        var cachedPhoto = await workerPhotoCache.GetAsync(workerId, cancellationToken);
        if (cachedPhoto is null)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            return Results.NotFound();
        }

        var etag = $"\"{cachedPhoto.Version}\"";
        if (string.Equals(httpContext.Request.Headers.IfNoneMatch.ToString(), etag, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        httpContext.Response.Headers.ETag = etag;
        httpContext.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.File(cachedPhoto.Content, cachedPhoto.ContentType);
    }
}
