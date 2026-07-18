using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Reports.Financial;
using ProductionLinePlanner.Application.Reports.Quantities;

namespace ProductionLinePlanner.Application.Services;

public interface IProductionFinancialReportService
{
    Task<Result<FinancialReportResultDto>> QueryAsync(
        QuantitiesReportFilterRequest request,
        CancellationToken cancellationToken = default);
}
