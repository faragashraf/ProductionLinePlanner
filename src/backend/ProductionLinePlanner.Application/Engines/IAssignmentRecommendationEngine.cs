using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface IAssignmentRecommendationEngine
{
    Task<Result<AssignmentRecommendationResultDto>> GetRecommendationsAsync(
        Guid productionLineId,
        Guid subStageId,
        Guid actorUserId,
        string? requestMeta = null,
        int topCandidates = 10,
        CancellationToken cancellationToken = default);
}
