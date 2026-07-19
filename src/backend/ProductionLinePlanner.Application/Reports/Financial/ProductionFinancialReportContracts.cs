using ProductionLinePlanner.Application.Reports.Quantities;

namespace ProductionLinePlanner.Application.Reports.Financial;

/// <summary>
/// Separate secure financial projection for production-compensation snapshots.
/// This contract intentionally has no worker salary data.
/// </summary>
public sealed record FinancialReportSummaryDto(
    decimal TotalPhysicalProducedQuantity,
    decimal TotalPhysicalAcceptedQuantity,
    decimal TotalPhysicalRejectedQuantity,
    int RecordCount,
    int StageCount,
    int WorkerCount,
    decimal? TotalProductionEarnings,
    decimal? TotalStageProductionCost,
    decimal? AverageProductionEarningPerWorker,
    decimal? AverageCostPerPhysicalUnit,
    int IncompleteFinancialRecordCount,
    string FinancialDataStatus,
    string CurrencyCode);

public sealed record FinancialReportRowDto(
    ReportSourceReferenceDto Source,
    DateOnly? ProductionDate,
    string Status,
    string? ProductionOrderNumber,
    string? FactoryCode,
    string? FactoryName,
    string? ProductionLineCode,
    string? ProductionLineName,
    string? ProductModelCode,
    string? ProductModelName,
    string? MainStageName,
    string? StageCode,
    string? StageName,
    string? WorkerCode,
    string? WorkerName,
    decimal? ProducedQuantity,
    decimal? AcceptedQuantity,
    decimal? RejectedQuantity,
    decimal? WorkerAllocatedQuantity,
    int RecordCount,
    int StageCount,
    int WorkerCount,
    decimal? StageProductionCost,
    decimal? ProductionEarning,
    decimal? StageUnitPrice,
    decimal? WorkerPercentage,
    string? CompensationMode,
    string FinancialDataStatus);

public sealed record FinancialReportResultDto(
    FinancialReportSummaryDto Summary,
    IReadOnlyCollection<FinancialReportRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    string AppliedStatus,
    string View,
    string SortBy,
    string SortDirection);
