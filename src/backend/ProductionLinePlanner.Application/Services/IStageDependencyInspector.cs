using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Services;

public interface IStageDependencyInspector
{
    Task<Result<StageDependencySummaryDto>> InspectAsync(Guid subStageId, CancellationToken cancellationToken = default);
}
