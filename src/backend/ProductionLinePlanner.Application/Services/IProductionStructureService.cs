using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Services;

public interface IProductionStructureService
{
    Task<Result<ProductionLineDto>> CreateProductionLineAsync(
        CreateProductionLineRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<ProductionLineDto>>> GetProductionLinesAsync(
        Guid factoryId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<MainStageDto>> CreateMainStageAsync(
        CreateMainStageRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<MainStageDto>>> GetMainStagesAsync(
        Guid productionLineId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<SubStageDto>> CreateSubStageAsync(
        CreateSubStageRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SubStageDto>>> GetSubStagesAsync(
        Guid mainStageId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}
