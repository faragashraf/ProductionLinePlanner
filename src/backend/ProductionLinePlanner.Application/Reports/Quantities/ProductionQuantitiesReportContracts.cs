using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Reports.Quantities;

public enum QuantitiesReportView
{
    Details = 0,
    ByStage = 1,
    ByWorker = 2,
    WorkerStages = 3,
    StageWorkers = 4
}

public enum QuantitiesReportSortBy
{
    ProductionDate = 0,
    StageCode = 1,
    WorkerCode = 2,
    ProducedQuantity = 3,
    AcceptedQuantity = 4,
    RejectedQuantity = 5,
    WorkerAllocatedQuantity = 6,
    RecordCount = 7,
    WorkerCount = 8,
    StageCount = 9
}

public enum QuantitiesReportSortDirection
{
    Ascending = 0,
    Descending = 1
}

public sealed class QuantitiesReportFilterRequest
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public Guid? FactoryId { get; set; }
    public Guid? ProductionLineId { get; set; }
    public Guid? ProductModelId { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public Guid? ProductModelStageId { get; set; }
    public Guid? WorkerId { get; set; }
    public StageProductionRecordStatus? Status { get; set; }
    public QuantitiesReportView View { get; set; } = QuantitiesReportView.Details;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public QuantitiesReportSortBy? SortBy { get; set; }
    public QuantitiesReportSortDirection SortDirection { get; set; } = QuantitiesReportSortDirection.Ascending;
}

/// <summary>
/// All quantity totals are at stage-production-record grain. They are never a
/// physical finished-product total across multiple stages and are never joined
/// once per worker allocation.
/// </summary>
public sealed record QuantitiesReportSummaryDto(
    decimal TotalStageProducedQuantity,
    decimal TotalAcceptedQuantity,
    decimal TotalRejectedQuantity,
    int RecordCount,
    int StageCount,
    int WorkerCount);

public sealed record ReportSourceReferenceDto(
    string SourceType,
    Guid? StageProductionRecordId,
    Guid? StageProductionWorkerAllocationId,
    Guid? ProductionOrderId,
    Guid? ProductModelStageId,
    Guid? WorkerId);

public sealed record QuantitiesReportRowDto(
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
    int WorkerCount);

public sealed record QuantitiesReportResultDto(
    QuantitiesReportSummaryDto Summary,
    IReadOnlyCollection<QuantitiesReportRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    string AppliedStatus,
    string View,
    string SortBy,
    string SortDirection);
