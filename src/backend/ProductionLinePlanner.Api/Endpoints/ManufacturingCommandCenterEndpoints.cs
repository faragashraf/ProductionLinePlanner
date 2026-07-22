using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Security;

namespace ProductionLinePlanner.Api.Endpoints;

public static class ManufacturingCommandCenterEndpoints
{
    public static void MapManufacturingCommandCenterEndpoints(this WebApplication app)
    {
        app.MapGet("/api/manufacturing-command-center", async (
            DateOnly? productionDate,
            Guid? factoryId,
            Guid? departmentId,
            Guid? productionLineId,
            string? operationStatus,
            IManufacturingCommandCenterEngine engine,
            ICairoTimeZoneProvider cairoTimeZoneProvider,
            CancellationToken cancellationToken) =>
        {
            var cairoNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoTimeZoneProvider.TimeZone);
            var query = new ManufacturingCommandCenterQuery(
                productionDate ?? DateOnly.FromDateTime(cairoNow),
                factoryId,
                departmentId,
                productionLineId,
                operationStatus);
            var result = await engine.GetAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return ApiResponse.Failure(
                    result.Error?.Code ?? "CommandCenterReadFailed",
                    result.Error?.Message ?? "Unable to load manufacturing command center.",
                    result.Error?.Code == "ValidationError"
                        ? StatusCodes.Status400BadRequest
                        : StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(ApiResponse.Success(result.Value!));
        })
            .RequireAuthorization()
            .RequirePermission("production.view")
            .RequireRateLimiting(ApiRateLimitPolicies.NormalRead)
            .WithTags("Manufacturing command center")
            .WithName("GetManufacturingCommandCenter");
    }
}
