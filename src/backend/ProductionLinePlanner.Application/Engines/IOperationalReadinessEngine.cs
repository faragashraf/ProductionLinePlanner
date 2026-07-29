using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Application.Engines;

public interface IOperationalReadinessEngine
{
    Task<Result<OperationalReadinessSnapshotDto>> GetSnapshotAsync(
        Guid? factoryId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<OperationalReadinessStagesDto>> GetLineStagesAsync(
        Guid productionLineId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<OperationalReadinessWorkersDto>> GetStageWorkersAsync(
        Guid productionLineId,
        Guid stageId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<OperationalReadinessDeltaDto>> GetDeltaAsync(
        ManufacturingDataChanged change,
        CancellationToken cancellationToken = default);
}
