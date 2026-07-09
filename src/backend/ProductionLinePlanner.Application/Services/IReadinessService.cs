using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Services;

public interface IReadinessService
{
    Task<Result<StageReadinessDto>> GetFactoryReadinessAsync(
        Guid factoryId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<StageReadinessDto>> GetProductionLineReadinessAsync(
        Guid productionLineId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<StageReadinessDto>> GetSubStageReadinessAsync(
        Guid subStageId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);
}
