using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Security;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProductionLinePlanner.Api.Endpoints;

public static class OperationalReadinessEndpoints
{
    public static void MapOperationalReadinessEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/operational-readiness")
            .RequireAuthorization()
            .RequirePermission(FactoryStructurePermissions.View)
            .RequirePermission("stages.view")
            .RequirePermission("assignments.view")
            .RequirePermission("attendance.view")
            .RequireRateLimiting(ApiRateLimitPolicies.NormalRead)
            .WithTags("Operational readiness");

        group.MapGet("", async (
            Guid? factoryId,
            [FromServices] IOperationalReadinessEngine engine,
            CancellationToken cancellationToken) =>
            ToResponse(await engine.GetSnapshotAsync(factoryId, null, cancellationToken)))
            .WithName("GetOperationalReadinessSnapshot");

        group.MapGet("/lines/{productionLineId:guid}/stages", async (
            Guid productionLineId,
            Guid? productModelId,
            [FromServices] IOperationalReadinessEngine engine,
            CancellationToken cancellationToken) =>
            ToResponse(await engine.GetLineStagesAsync(productionLineId, null, cancellationToken, productModelId)))
            .WithName("GetOperationalReadinessLineStages");

        group.MapGet("/lines/{productionLineId:guid}/stages/{stageId:guid}/workers", async (
            Guid productionLineId,
            Guid stageId,
            [FromServices] IOperationalReadinessEngine engine,
            CancellationToken cancellationToken) =>
            ToResponse(await engine.GetStageWorkersAsync(productionLineId, stageId, null, cancellationToken)))
            .WithName("GetOperationalReadinessStageWorkers");
    }

    private static IResult ToResponse<T>(Application.Common.Result<T> result)
    {
        if (result.IsSuccess) return Results.Ok(ApiResponse.Success(result.Value!));
        var status = result.Error?.Code switch
        {
            "ValidationError" => StatusCodes.Status400BadRequest,
            "NotFound" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };
        return ApiResponse.Failure(
            result.Error?.Code ?? "OperationalReadinessReadFailed",
            result.Error?.Message ?? "Unable to load operational readiness.",
            status);
    }
}
