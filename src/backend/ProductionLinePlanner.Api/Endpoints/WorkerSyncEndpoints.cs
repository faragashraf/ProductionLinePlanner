using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Api.Endpoints;

public static class WorkerSyncEndpoints
{
    public static RouteGroupBuilder MapWorkerSyncEndpoints(this RouteGroupBuilder workersApi)
    {
        workersApi.MapGet("/sync/preview", PreviewAsync)
            .RequirePermission("workers.manage")
            .WithTags("Workers")
            .WithName("PreviewWorkerMasterSync");

        return workersApi;
    }

    private static async Task<IResult> PreviewAsync(
        IWorkerInitialSyncService syncService,
        CancellationToken cancellationToken)
    {
        var result = await syncService.PreviewActiveServiceSyncAsync(cancellationToken);
        return result.IsFailure
            ? ApiResponse.Failure(
                result.Error?.Code ?? "WorkerSyncPreviewFailed",
                result.Error?.Message ?? "Unable to preview worker master synchronization.",
                MapFailureStatusCode(result.Error?.Code))
            : Results.Ok(ApiResponse.Success(result.Value!));
    }

    private static int MapFailureStatusCode(string? code) => code switch
    {
        "ValidationError" => StatusCodes.Status400BadRequest,
        "Unauthorized" => StatusCodes.Status401Unauthorized,
        "Forbidden" => StatusCodes.Status403Forbidden,
        "AttendanceSourceError" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}
