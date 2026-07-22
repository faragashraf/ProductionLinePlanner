using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public interface IManufacturingCommandCenterEngine
{
    Task<Result<ManufacturingCommandCenterDto>> GetAsync(
        ManufacturingCommandCenterQuery query,
        CancellationToken cancellationToken = default);
}
