using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Reports.Quantities;

namespace ProductionLinePlanner.Application.Services;

public interface IProductionQuantitiesReportService
{
    Task<Result<QuantitiesReportResultDto>> QueryAsync(
        QuantitiesReportFilterRequest request,
        CancellationToken cancellationToken = default);
}
