using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Security;
using ProductionLinePlanner.Application.Reports.Quantities;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Api.Endpoints;

public static class ProductionQuantitiesReportEndpoints
{
    public static void MapProductionQuantitiesReportEndpoints(WebApplication app)
    {
        app.MapGet("/api/reports/production/quantities", async (
                DateOnly from,
                DateOnly to,
                IProductionQuantitiesReportService service,
                CancellationToken cancellationToken,
                Guid? factoryId = null,
                Guid? productionLineId = null,
                Guid? productModelId = null,
                Guid? productionOrderId = null,
                Guid? productModelStageId = null,
                Guid? workerId = null,
                StageProductionRecordStatus? status = null,
                QuantitiesReportView view = QuantitiesReportView.Details,
                int page = 1,
                int pageSize = 50,
                QuantitiesReportSortBy? sortBy = null,
                QuantitiesReportSortDirection sortDirection = QuantitiesReportSortDirection.Ascending) =>
            {
                var request = new QuantitiesReportFilterRequest
                {
                    From = from,
                    To = to,
                    FactoryId = factoryId,
                    ProductionLineId = productionLineId,
                    ProductModelId = productModelId,
                    ProductionOrderId = productionOrderId,
                    ProductModelStageId = productModelStageId,
                    WorkerId = workerId,
                    Status = status,
                    View = view,
                    Page = page,
                    PageSize = pageSize,
                    SortBy = sortBy,
                    SortDirection = sortDirection
                };
                var result = await service.QueryAsync(request, cancellationToken);
                return result.IsSuccess
                    ? Results.Ok(ApiResponse.Success(result.Value!))
                    : ApiResponse.Failure(result.Error?.Code ?? "ValidationError", result.Error?.Message ?? "Validation failed.");
            })
            .RequireAuthorization()
            .RequirePermission("reports.production.view")
            .RequireRateLimiting(ApiRateLimitPolicies.NormalRead)
            .WithTags("Production quantity reports")
            .WithName("GetProductionQuantitiesReport");
    }
}
