using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface ILineStaffingEngine
{
    Task<Result<LineStaffingPlanDto>> GetLineStaffingPlanAsync(
        Guid factoryId,
        Guid productionLineId,
        Guid productModelId,
        DateOnly staffingReferenceDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns only one stage, its participating workers, and plan summary
    /// counters after a staffing mutation.
    /// </summary>
    Task<Result<LineStaffingStageRefreshDto>> GetLineStaffingStageRefreshAsync(
        Guid factoryId,
        Guid productionLineId,
        Guid productModelId,
        Guid subStageId,
        DateOnly staffingReferenceDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the organizational worker source for staffing dialogs. This is
    /// deliberately limited to active employment and never joins attendance.
    /// </summary>
    Task<Result<IReadOnlyCollection<LineStaffingWorkerDto>>> GetActiveStaffingWorkersAsync(
        DateOnly staffingReferenceDate,
        CancellationToken cancellationToken = default);
}
