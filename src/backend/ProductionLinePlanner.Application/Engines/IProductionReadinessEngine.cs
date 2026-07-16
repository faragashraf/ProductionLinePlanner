using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface IProductionReadinessEngine
{
    Task<Result<ProductProductionReadinessDto>> GetProductReadinessAsync(
        Guid productModelId,
        Guid productionLineId,
        DateOnly productionDate,
        CancellationToken cancellationToken = default);
}
