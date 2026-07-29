using Microsoft.AspNetCore.Mvc;
using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Diagnostics;
using ProductionLinePlanner.Api.Security;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Application.Workers;

namespace ProductionLinePlanner.Api.Endpoints;

public static class WorkerPhotoEndpoint
{
    private const long MultipartRequestLimit = WorkerPhotoFormat.MaximumBytes + (64 * 1024);

    public static RouteGroupBuilder MapWorkerPhotoEndpoints(this RouteGroupBuilder workersApi)
    {
        workersApi.MapGet("/{workerId:guid}/photo", DownloadAsync)
            .RequirePermission("workers.view")
            .RequireRateLimiting(ApiRateLimitPolicies.WorkerPhotoRead)
            .WithTags("Workers")
            .WithName("GetWorkerPhoto");

        workersApi.MapPut("/{workerId:guid}/photo", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(MultipartRequestLimit),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MultipartRequestLimit })
            .RequirePermission("workers.manage")
            .RequireRateLimiting(ApiRateLimitPolicies.WorkerPhotoWrite)
            .Accepts<IFormFile>("multipart/form-data")
            .WithTags("Workers")
            .WithName("UploadOrReplaceWorkerPhoto");

        workersApi.MapDelete("/{workerId:guid}/photo", DeleteAsync)
            .RequirePermission("workers.manage")
            .RequireRateLimiting(ApiRateLimitPolicies.WorkerPhotoWrite)
            .WithTags("Workers")
            .WithName("DeleteWorkerPhoto");

        return workersApi;
    }

    private static async Task<IResult> DownloadAsync(
        Guid workerId,
        string? v,
        IWorkerPhotoService workerPhotoService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await workerPhotoService.DownloadAsync(workerId, v, cancellationToken);
        if (result.IsFailure)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            return ApiResponse.Failure(
                result.Error?.Code ?? "NotFound",
                result.Error?.Message ?? "Worker photo not found.",
                MapFailureStatusCode(result.Error?.Code));
        }

        var photo = result.Value!;
        var etag = $"\"{photo.Version}\"";
        httpContext.Response.Headers.ETag = etag;
        httpContext.Response.Headers.Vary = "Authorization";
        // Protected photos may be stored privately, but every browser-level
        // reuse must revalidate authorization. The versioned URL still busts
        // both browser and client caches on replace.
        httpContext.Response.Headers.CacheControl = "private, no-cache, must-revalidate";

        if (RequestHasMatchingEtag(httpContext, etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.File(photo.Content, photo.ContentType, enableRangeProcessing: false);
    }

    private static async Task<IResult> UploadAsync(
        Guid workerId,
        IFormFile photo,
        IWorkerPhotoService workerPhotoService,
        ICurrentUserService currentUserService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (currentUserService.UserId is not { } actorUserId)
        {
            return ApiResponse.Failure("Unauthorized", "User context is required.", StatusCodes.Status401Unauthorized);
        }

        await using var content = photo.OpenReadStream();
        var result = await workerPhotoService.UploadAsync(
            workerId,
            content,
            photo.Length,
            photo.ContentType,
            actorUserId,
            AuditRequestMetadata.From(httpContext),
            cancellationToken);
        if (result.IsFailure)
        {
            return ApiResponse.Failure(
                result.Error?.Code ?? "WorkerPhotoUploadFailed",
                result.Error?.Message ?? "Unable to upload the worker photo.",
                MapFailureStatusCode(result.Error?.Code));
        }

        var change = result.Value!;
        return change.Created
            ? Results.Created(change.Photo.PhotoReference, ApiResponse.Success(change))
            : Results.Ok(ApiResponse.Success(change));
    }

    private static async Task<IResult> DeleteAsync(
        Guid workerId,
        IWorkerPhotoService workerPhotoService,
        ICurrentUserService currentUserService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (currentUserService.UserId is not { } actorUserId)
        {
            return ApiResponse.Failure("Unauthorized", "User context is required.", StatusCodes.Status401Unauthorized);
        }

        var result = await workerPhotoService.DeleteAsync(
            workerId,
            actorUserId,
            AuditRequestMetadata.From(httpContext),
            cancellationToken);
        return result.IsFailure
            ? ApiResponse.Failure(
                result.Error?.Code ?? "WorkerPhotoDeleteFailed",
                result.Error?.Message ?? "Unable to delete the worker photo.",
                MapFailureStatusCode(result.Error?.Code))
            : Results.NoContent();
    }

    private static bool RequestHasMatchingEtag(HttpContext httpContext, string etag) =>
        httpContext.Request.Headers.IfNoneMatch
            .SelectMany(value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            .Any(value => value == "*" || value.Equals(etag, StringComparison.Ordinal));

    private static int MapFailureStatusCode(string? code) => code switch
    {
        "ValidationError" => StatusCodes.Status400BadRequest,
        "Unauthorized" => StatusCodes.Status401Unauthorized,
        "Forbidden" => StatusCodes.Status403Forbidden,
        "NotFound" => StatusCodes.Status404NotFound,
        "PhotoTooLarge" => StatusCodes.Status413PayloadTooLarge,
        "UnsupportedPhotoType" => StatusCodes.Status415UnsupportedMediaType,
        _ => StatusCodes.Status500InternalServerError
    };
}
