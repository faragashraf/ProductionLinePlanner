using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface IReadinessEngine
{
    Task<Result<StageReadinessDto>> GetFactoryReadinessAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<ProductionLinesReadinessDto>> GetProductionLinesReadinessAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<StageReadinessDto>> GetSubStageReadinessAsync(
        Guid subStageId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<SubStageAttendanceSummaryDto>>> GetActiveSubStageAttendanceSummariesAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);
}
