using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Reports.Financial;
using ProductionLinePlanner.Application.Reports.Quantities;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Read-only production-compensation projection. It uses stored snapshots and
/// never recalculates worker allocations or stage values.
/// </summary>
public sealed class ProductionFinancialReportService(AppDbContext db) : IProductionFinancialReportService
{
    private const string CurrencyCode = "EGP";
    private const string Complete = "Complete";
    private const string Incomplete = "Incomplete";
    private const string ReviewRequired = "ReviewRequired";

    public async Task<Result<FinancialReportResultDto>> QueryAsync(
        QuantitiesReportFilterRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(request);
        if (validationError is not null)
            return Result<FinancialReportResultDto>.Failure(validationError);

        var appliedStatus = request.Status ?? StageProductionRecordStatus.Approved;
        var sortBy = request.SortBy ?? DefaultSortBy(request.View);
        var recordsQuery = ProductionReportQuerySupport.ApplyFilters(db, request, appliedStatus);
        var recordData = await recordsQuery
            .Select(record => new FinancialRecordData(
                record.Id, record.ProductionOrderId, record.ProductModelStageId, record.ProductionDate, record.Status,
                record.ProducedQuantity, record.AcceptedQuantity, record.RejectedQuantity,
                record.SnapshotFactoryCode, record.SnapshotFactoryName, record.SnapshotProductionLineCode, record.SnapshotProductionLineName,
                record.SnapshotProductModelCode, record.SnapshotProductModelName, record.SnapshotMainStageName,
                record.SnapshotStageCode, record.SnapshotStageName, record.ProductionOrder!.OrderNumber,
                record.ProductionOrder.SourceReference, record.TotalWorkerEarnings, record.SnapshotPiecePrice, record.SnapshotCompensationMode))
            .ToArrayAsync(cancellationToken);

        var recordIds = recordData.Select(record => record.RecordId).ToArray();
        var allAllocations = recordIds.Length == 0
            ? []
            : await db.Set<StageProductionWorkerAllocation>()
                .AsNoTracking()
                .Where(allocation => recordIds.Contains(allocation.StageProductionRecordId))
                .Select(allocation => new FinancialAllocationData(
                    allocation.Id, allocation.StageProductionRecordId, allocation.WorkerId,
                    allocation.SnapshotWorkerCode, allocation.SnapshotWorkerName,
                    allocation.Percentage, allocation.EquivalentQuantity, allocation.CalculatedEarning))
                .ToArrayAsync(cancellationToken);

        var allocationsByRecord = allAllocations
            .GroupBy(allocation => allocation.RecordId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<FinancialAllocationData>)group.ToArray());
        var records = recordData
            .Select(record =>
            {
                var integrityAllocations = allocationsByRecord.GetValueOrDefault(record.RecordId, []);
                var visibleAllocations = request.WorkerId.HasValue
                    ? integrityAllocations.Where(allocation => allocation.WorkerId == request.WorkerId.Value).ToArray()
                    : integrityAllocations;

                return record with
                {
                    Allocations = visibleAllocations,
                    IntegrityAllocations = integrityAllocations
                };
            })
            .ToArray();
        var summary = BuildSummary(records);
        var rows = BuildRows(request.View, records);
        var sortedRows = ApplySorting(rows, sortBy, request.SortDirection).ToArray();
        var totalCount = sortedRows.Length;
        var offset = (long)(request.Page - 1) * request.PageSize;
        var pageRows = offset > int.MaxValue ? [] : sortedRows.Skip((int)offset).Take(request.PageSize).ToArray();

        return Result<FinancialReportResultDto>.Success(new FinancialReportResultDto(
            summary, pageRows, request.Page, request.PageSize, totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)request.PageSize),
            appliedStatus.ToString(), request.View.ToString(), sortBy.ToString(), request.SortDirection.ToString()));
    }

    private static Error? Validate(QuantitiesReportFilterRequest request)
    {
        var commonError = ProductionReportQuerySupport.ValidateCommon(request);
        if (commonError is not null)
            return commonError;
        var sortBy = request.SortBy ?? DefaultSortBy(request.View);
        return !Enum.IsDefined(sortBy) || !IsSortAllowed(request.View, sortBy)
            ? new Error("ValidationError", "SortBy is not allowed for the selected report view.")
            : null;
    }

    private static FinancialReportSummaryDto BuildSummary(IReadOnlyCollection<FinancialRecordData> records)
    {
        var physicalRecords = ProductionReportQuerySupport.SelectPhysicalRunRecords(
            records, record => record.ProductionOrderId, record => record.IsDailyOperation,
            record => record.ProductModelStageId, record => record.RecordId);
        var financialStatus = ResolveFinancialStatus(records);
        var financialIsComplete = financialStatus == Complete;
        var workerCount = records.SelectMany(record => record.Allocations).Select(allocation => allocation.WorkerId).Distinct().Count();
        decimal? totalEarnings = financialIsComplete ? records.Sum(record => record.Allocations.Sum(allocation => allocation.CalculatedEarning)) : null;
        decimal? totalCost = financialIsComplete ? records.Sum(record => record.TotalWorkerEarnings) : null;
        var physicalQuantity = physicalRecords.Sum(record => record.ProducedQuantity);

        return new FinancialReportSummaryDto(
            physicalQuantity,
            physicalRecords.Sum(record => record.AcceptedQuantity),
            physicalRecords.Sum(record => record.RejectedQuantity),
            records.Count,
            records.Select(record => record.ProductModelStageId).Distinct().Count(),
            workerCount,
            totalEarnings,
            totalCost,
            financialIsComplete && workerCount > 0 ? totalEarnings / workerCount : null,
            financialIsComplete && physicalQuantity > 0 ? totalCost / physicalQuantity : null,
            records.Count(record => RecordFinancialStatus(record) != Complete),
            financialStatus,
            CurrencyCode);
    }

    private static IReadOnlyCollection<FinancialReportRowDto> BuildRows(
        QuantitiesReportView view,
        IReadOnlyCollection<FinancialRecordData> records)
    {
        var participations = records
            .SelectMany(record => record.Allocations.Select(allocation => new Participation(record, allocation)))
            .ToArray();
        return view switch
        {
            QuantitiesReportView.Details => records.Select(ToDetailsRow).ToArray(),
            QuantitiesReportView.ByStage => records.GroupBy(record => record.ProductModelStageId).Select(ToStageRow).ToArray(),
            QuantitiesReportView.ByWorker => participations.GroupBy(participation => participation.Allocation.WorkerId).Select(ToWorkerRow).ToArray(),
            QuantitiesReportView.WorkerStages or QuantitiesReportView.StageWorkers => participations.Select(ToParticipationRow).ToArray(),
            _ => []
        };
    }

    private static FinancialReportRowDto ToDetailsRow(FinancialRecordData record)
    {
        var financialStatus = RecordFinancialStatus(record);
        decimal? amount = financialStatus == Complete ? record.TotalWorkerEarnings : null;
        return ToRow(
            new ReportSourceReferenceDto("StageProductionRecord", record.RecordId, null, record.ProductionOrderId, record.ProductModelStageId, null),
            record, null, null, record.ProducedQuantity, record.AcceptedQuantity, record.RejectedQuantity, null,
            1, 1, record.Allocations.Select(allocation => allocation.WorkerId).Distinct().Count(),
            amount, amount, record.SnapshotPiecePrice, null, record.CompensationMode.ToString(), financialStatus);
    }

    private static FinancialReportRowDto ToStageRow(IGrouping<Guid, FinancialRecordData> group)
    {
        var records = group.ToArray();
        var first = records.OrderBy(record => record.ProductionDate).ThenBy(record => record.RecordId).First();
        var financialStatus = ResolveFinancialStatus(records);
        decimal? amount = financialStatus == Complete ? records.Sum(record => record.TotalWorkerEarnings) : null;
        return ToRow(
            new ReportSourceReferenceDto("ProductModelStage", null, null, null, first.ProductModelStageId, null),
            first, null, null, records.Sum(record => record.ProducedQuantity), records.Sum(record => record.AcceptedQuantity), records.Sum(record => record.RejectedQuantity), null,
            records.Length, 1, records.SelectMany(record => record.Allocations).Select(allocation => allocation.WorkerId).Distinct().Count(),
            amount, amount, null, null, ResolveCompensationMode(records), financialStatus);
    }

    private static FinancialReportRowDto ToWorkerRow(IGrouping<Guid, Participation> group)
    {
        var participations = group.ToArray();
        var first = participations.OrderBy(participation => participation.Record.ProductionDate).ThenBy(participation => participation.Record.RecordId).First();
        var financialStatus = ResolveFinancialStatus(participations.Select(participation => participation.Record));
        return ToRow(
            new ReportSourceReferenceDto("Worker", null, null, null, null, first.Allocation.WorkerId),
            first.Record, first.Allocation.WorkerCode, first.Allocation.WorkerName, null, null, null,
            participations.Sum(participation => participation.Allocation.EquivalentQuantity),
            participations.Select(participation => participation.Record.RecordId).Distinct().Count(),
            participations.Select(participation => participation.Record.ProductModelStageId).Distinct().Count(),
            1, null,
            financialStatus == Complete ? participations.Sum(participation => participation.Allocation.CalculatedEarning) : null,
            null, null, ResolveCompensationMode(participations.Select(participation => participation.Record)), financialStatus);
    }

    private static FinancialReportRowDto ToParticipationRow(Participation participation)
    {
        var financialStatus = RecordFinancialStatus(participation.Record);
        return ToRow(
            new ReportSourceReferenceDto(
                "StageProductionWorkerAllocation",
                participation.Record.RecordId,
                participation.Allocation.AllocationId,
                participation.Record.ProductionOrderId,
                participation.Record.ProductModelStageId,
                participation.Allocation.WorkerId),
            participation.Record, participation.Allocation.WorkerCode, participation.Allocation.WorkerName,
            participation.Record.ProducedQuantity, participation.Record.AcceptedQuantity, participation.Record.RejectedQuantity,
            participation.Allocation.EquivalentQuantity, 1, 1, 1, null,
            financialStatus == Complete ? participation.Allocation.CalculatedEarning : null,
            participation.Record.SnapshotPiecePrice, participation.Allocation.Percentage,
            participation.Record.CompensationMode.ToString(), financialStatus);
    }

    private static FinancialReportRowDto ToRow(
        ReportSourceReferenceDto source,
        FinancialRecordData record,
        string? workerCode,
        string? workerName,
        decimal? producedQuantity,
        decimal? acceptedQuantity,
        decimal? rejectedQuantity,
        decimal? workerAllocatedQuantity,
        int recordCount,
        int stageCount,
        int workerCount,
        decimal? stageProductionCost,
        decimal? productionEarning,
        decimal? stageUnitPrice,
        decimal? workerPercentage,
        string? compensationMode,
        string financialDataStatus) =>
        new(
            source,
            source.SourceType == "ProductModelStage" || source.SourceType == "Worker" ? null : record.ProductionDate,
            record.Status.ToString(),
            source.SourceType == "ProductModelStage" || source.SourceType == "Worker" ? null : record.ProductionOrderNumber,
            source.SourceType == "Worker" ? null : record.FactoryCode,
            source.SourceType == "Worker" ? null : record.FactoryName,
            source.SourceType == "Worker" ? null : record.ProductionLineCode,
            source.SourceType == "Worker" ? null : record.ProductionLineName,
            source.SourceType == "Worker" ? null : record.ProductModelCode,
            source.SourceType == "Worker" ? null : record.ProductModelName,
            source.SourceType == "Worker" ? null : record.MainStageName,
            source.SourceType == "Worker" ? null : record.StageCode,
            source.SourceType == "Worker" ? null : record.StageName,
            workerCode,
            workerName,
            producedQuantity,
            acceptedQuantity,
            rejectedQuantity,
            workerAllocatedQuantity,
            recordCount,
            stageCount,
            workerCount,
            stageProductionCost,
            productionEarning,
            stageUnitPrice,
            workerPercentage,
            compensationMode,
            financialDataStatus);

    private static string RecordFinancialStatus(FinancialRecordData record)
    {
        if (record.IntegrityAllocations.Count == 0)
            return Incomplete;
        return record.TotalWorkerEarnings == record.IntegrityAllocations.Sum(allocation => allocation.CalculatedEarning)
            ? Complete
            : ReviewRequired;
    }

    private static string ResolveFinancialStatus(IEnumerable<FinancialRecordData> records)
    {
        var statuses = records.Select(RecordFinancialStatus).Distinct().ToArray();
        return statuses.Contains(ReviewRequired) ? ReviewRequired :
            statuses.Contains(Incomplete) ? Incomplete : Complete;
    }

    private static string? ResolveCompensationMode(IEnumerable<FinancialRecordData> records)
    {
        var modes = records.Select(record => record.CompensationMode.ToString()).Distinct(StringComparer.Ordinal).ToArray();
        return modes.Length == 1 ? modes[0] : modes.Length == 0 ? null : "Mixed";
    }

    private static IEnumerable<FinancialReportRowDto> ApplySorting(
        IEnumerable<FinancialReportRowDto> rows,
        QuantitiesReportSortBy sortBy,
        QuantitiesReportSortDirection direction) => sortBy switch
    {
        QuantitiesReportSortBy.ProductionDate => Order(rows, row => row.ProductionDate ?? DateOnly.MinValue, direction),
        QuantitiesReportSortBy.StageCode => Order(rows, row => row.StageCode ?? string.Empty, direction, StringComparer.Ordinal),
        QuantitiesReportSortBy.WorkerCode => Order(rows, row => row.WorkerCode ?? string.Empty, direction, StringComparer.Ordinal),
        QuantitiesReportSortBy.ProducedQuantity => Order(rows, row => row.ProducedQuantity ?? 0m, direction),
        QuantitiesReportSortBy.AcceptedQuantity => Order(rows, row => row.AcceptedQuantity ?? 0m, direction),
        QuantitiesReportSortBy.RejectedQuantity => Order(rows, row => row.RejectedQuantity ?? 0m, direction),
        QuantitiesReportSortBy.WorkerAllocatedQuantity => Order(rows, row => row.WorkerAllocatedQuantity ?? 0m, direction),
        QuantitiesReportSortBy.RecordCount => Order(rows, row => row.RecordCount, direction),
        QuantitiesReportSortBy.WorkerCount => Order(rows, row => row.WorkerCount, direction),
        QuantitiesReportSortBy.StageCount => Order(rows, row => row.StageCount, direction),
        _ => rows.OrderBy(StableKey, StringComparer.Ordinal)
    };

    private static IEnumerable<FinancialReportRowDto> Order<TKey>(
        IEnumerable<FinancialReportRowDto> rows,
        Func<FinancialReportRowDto, TKey> selector,
        QuantitiesReportSortDirection direction,
        IComparer<TKey>? comparer = null) =>
        direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(selector, comparer).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(selector, comparer).ThenBy(StableKey, StringComparer.Ordinal);

    private static string StableKey(FinancialReportRowDto row) =>
        row.Source.StageProductionRecordId?.ToString("N") ??
        row.Source.StageProductionWorkerAllocationId?.ToString("N") ??
        row.Source.ProductModelStageId?.ToString("N") ??
        row.Source.WorkerId?.ToString("N") ??
        string.Empty;

    private static QuantitiesReportSortBy DefaultSortBy(QuantitiesReportView view) => view switch
    {
        QuantitiesReportView.ByStage or QuantitiesReportView.StageWorkers => QuantitiesReportSortBy.StageCode,
        QuantitiesReportView.ByWorker or QuantitiesReportView.WorkerStages => QuantitiesReportSortBy.WorkerCode,
        _ => QuantitiesReportSortBy.ProductionDate
    };

    private static bool IsSortAllowed(QuantitiesReportView view, QuantitiesReportSortBy sortBy) => view switch
    {
        QuantitiesReportView.Details => sortBy is QuantitiesReportSortBy.ProductionDate or QuantitiesReportSortBy.StageCode or
            QuantitiesReportSortBy.ProducedQuantity or QuantitiesReportSortBy.AcceptedQuantity or QuantitiesReportSortBy.RejectedQuantity,
        QuantitiesReportView.ByStage => sortBy is QuantitiesReportSortBy.StageCode or QuantitiesReportSortBy.ProducedQuantity or
            QuantitiesReportSortBy.AcceptedQuantity or QuantitiesReportSortBy.RejectedQuantity or QuantitiesReportSortBy.RecordCount or QuantitiesReportSortBy.WorkerCount,
        QuantitiesReportView.ByWorker => sortBy is QuantitiesReportSortBy.WorkerCode or QuantitiesReportSortBy.WorkerAllocatedQuantity or
            QuantitiesReportSortBy.RecordCount or QuantitiesReportSortBy.StageCount,
        QuantitiesReportView.WorkerStages => sortBy is QuantitiesReportSortBy.WorkerCode or QuantitiesReportSortBy.StageCode or
            QuantitiesReportSortBy.ProductionDate or QuantitiesReportSortBy.WorkerAllocatedQuantity,
        QuantitiesReportView.StageWorkers => sortBy is QuantitiesReportSortBy.StageCode or QuantitiesReportSortBy.WorkerCode or
            QuantitiesReportSortBy.ProductionDate or QuantitiesReportSortBy.WorkerAllocatedQuantity,
        _ => false
    };

    private sealed record FinancialRecordData(
        Guid RecordId,
        Guid ProductionOrderId,
        Guid ProductModelStageId,
        DateOnly ProductionDate,
        StageProductionRecordStatus Status,
        decimal ProducedQuantity,
        decimal AcceptedQuantity,
        decimal RejectedQuantity,
        string FactoryCode,
        string FactoryName,
        string ProductionLineCode,
        string ProductionLineName,
        string ProductModelCode,
        string ProductModelName,
        string MainStageName,
        string StageCode,
        string StageName,
        string ProductionOrderNumber,
        string? ProductionOrderSourceReference,
        decimal TotalWorkerEarnings,
        decimal SnapshotPiecePrice,
        CompensationMode CompensationMode)
    {
        public IReadOnlyCollection<FinancialAllocationData> Allocations { get; init; } = [];
        public IReadOnlyCollection<FinancialAllocationData> IntegrityAllocations { get; init; } = [];
        public bool IsDailyOperation => ProductionOrderSourceReference?.StartsWith("DailyProductionOperations/", StringComparison.Ordinal) == true;
    }

    private sealed record FinancialAllocationData(
        Guid AllocationId,
        Guid RecordId,
        Guid WorkerId,
        string WorkerCode,
        string WorkerName,
        decimal? Percentage,
        decimal EquivalentQuantity,
        decimal CalculatedEarning);

    private sealed record Participation(FinancialRecordData Record, FinancialAllocationData Allocation);
}
