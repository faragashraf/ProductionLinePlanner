using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Reports.Quantities;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Read-only quantities report projection. It deliberately selects no salary,
/// price, compensation, earning, entitlement, or currency data.
/// </summary>
public sealed class ProductionQuantitiesReportService(AppDbContext db) : IProductionQuantitiesReportService
{

    public async Task<Result<QuantitiesReportResultDto>> QueryAsync(
        QuantitiesReportFilterRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(request);
        if (validationError is not null)
            return Result<QuantitiesReportResultDto>.Failure(validationError);

        var appliedStatus = request.Status ?? StageProductionRecordStatus.Approved;
        var sortBy = request.SortBy ?? DefaultSortBy(request.View);
        var recordsQuery = ProductionReportQuerySupport.ApplyFilters(db, request, appliedStatus);

        var recordData = await recordsQuery
            .Select(record => new RecordData(
                record.Id,
                record.ProductionOrderId,
                record.ProductModelStageId,
                record.ProductionDate,
                record.Status,
                record.ProducedQuantity,
                record.AcceptedQuantity,
                record.RejectedQuantity,
                record.SnapshotFactoryCode,
                record.SnapshotFactoryName,
                record.SnapshotProductionLineCode,
                record.SnapshotProductionLineName,
                record.SnapshotProductModelCode,
                record.SnapshotProductModelName,
                record.SnapshotMainStageName,
                record.SnapshotStageCode,
                record.SnapshotStageName,
                record.ProductionOrder!.OrderNumber,
                record.ProductionOrder.SourceReference))
            .ToArrayAsync(cancellationToken);

        var recordIds = recordData.Select(record => record.RecordId).ToArray();
        var allocations = recordIds.Length == 0
            ? []
            : await db.Set<StageProductionWorkerAllocation>()
                .AsNoTracking()
                .Where(allocation =>
                    recordIds.Contains(allocation.StageProductionRecordId) &&
                    (!request.WorkerId.HasValue || allocation.WorkerId == request.WorkerId.Value))
                .Select(allocation => new AllocationData(
                    allocation.Id,
                    allocation.StageProductionRecordId,
                    allocation.WorkerId,
                    allocation.SnapshotWorkerCode,
                    allocation.SnapshotWorkerName,
                    allocation.EquivalentQuantity))
                .ToArrayAsync(cancellationToken);

        var allocationsByRecord = allocations
            .GroupBy(allocation => allocation.RecordId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<AllocationData>)group.ToArray());
        var records = recordData
            .Select(record => record with
            {
                Allocations = allocationsByRecord.GetValueOrDefault(record.RecordId, [])
            })
            .ToArray();

        var summary = BuildSummary(records);
        var rows = BuildRows(request.View, records);
        var sortedRows = ApplySorting(rows, sortBy, request.SortDirection).ToArray();
        var totalCount = sortedRows.Length;
        var offset = (long)(request.Page - 1) * request.PageSize;
        var pageRows = offset > int.MaxValue
            ? []
            : sortedRows
                .Skip((int)offset)
                .Take(request.PageSize)
                .ToArray();

        return Result<QuantitiesReportResultDto>.Success(new QuantitiesReportResultDto(
            summary,
            pageRows,
            request.Page,
            request.PageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)request.PageSize),
            appliedStatus.ToString(),
            request.View.ToString(),
            sortBy.ToString(),
            request.SortDirection.ToString()));
    }

    private static Error? Validate(QuantitiesReportFilterRequest request)
    {
        var commonError = ProductionReportQuerySupport.ValidateCommon(request);
        if (commonError is not null)
            return commonError;

        var sortBy = request.SortBy ?? DefaultSortBy(request.View);
        if (!Enum.IsDefined(sortBy) || !IsSortAllowed(request.View, sortBy))
            return new Error("ValidationError", "SortBy is not allowed for the selected report view.");

        return null;
    }

    private static QuantitiesReportSummaryDto BuildSummary(IReadOnlyCollection<RecordData> records)
    {
        var physicalRecords = ProductionReportQuerySupport.SelectPhysicalRunRecords(
            records,
            record => record.ProductionOrderId,
            record => record.IsDailyOperation,
            record => record.ProductModelStageId,
            record => record.RecordId);

        return new QuantitiesReportSummaryDto(
            physicalRecords.Sum(record => record.ProducedQuantity),
            physicalRecords.Sum(record => record.AcceptedQuantity),
            physicalRecords.Sum(record => record.RejectedQuantity),
            records.Sum(record => record.ProducedQuantity),
            records.Sum(record => record.AcceptedQuantity),
            records.Sum(record => record.RejectedQuantity),
            records.Count,
            records.Select(record => record.ProductModelStageId).Distinct().Count(),
            records.SelectMany(record => record.Allocations).Select(allocation => allocation.WorkerId).Distinct().Count());
    }

    private static IReadOnlyCollection<QuantitiesReportRowDto> BuildRows(
        QuantitiesReportView view,
        IReadOnlyCollection<RecordData> records)
    {
        var participations = records
            .SelectMany(record => record.Allocations.Select(allocation => new Participation(record, allocation)))
            .ToArray();

        return view switch
        {
            QuantitiesReportView.Details => records.Select(ToDetailsRow).ToArray(),
            QuantitiesReportView.ByStage => records.GroupBy(record => record.ProductModelStageId).Select(ToStageRow).ToArray(),
            QuantitiesReportView.ByWorker => participations.GroupBy(participation => participation.Allocation.WorkerId).Select(ToWorkerRow).ToArray(),
            QuantitiesReportView.WorkerStages => participations.Select(ToWorkerStageRow).ToArray(),
            QuantitiesReportView.StageWorkers => participations.Select(ToStageWorkerRow).ToArray(),
            _ => []
        };
    }

    private static QuantitiesReportRowDto ToDetailsRow(RecordData record) => new(
        new ReportSourceReferenceDto("StageProductionRecord", record.RecordId, null, record.ProductionOrderId, record.ProductModelStageId, null),
        record.ProductionDate,
        record.Status.ToString(),
        record.ProductionOrderNumber,
        record.FactoryCode,
        record.FactoryName,
        record.ProductionLineCode,
        record.ProductionLineName,
        record.ProductModelCode,
        record.ProductModelName,
        record.MainStageName,
        record.StageCode,
        record.StageName,
        null,
        null,
        record.ProducedQuantity,
        record.AcceptedQuantity,
        record.RejectedQuantity,
        null,
        1,
        1,
        record.Allocations.Select(allocation => allocation.WorkerId).Distinct().Count());

    private static QuantitiesReportRowDto ToStageRow(IGrouping<Guid, RecordData> group)
    {
        var first = group.OrderBy(record => record.ProductionDate).ThenBy(record => record.RecordId).First();
        return new QuantitiesReportRowDto(
            new ReportSourceReferenceDto("ProductModelStage", null, null, null, first.ProductModelStageId, null),
            null,
            first.Status.ToString(),
            null,
            first.FactoryCode,
            first.FactoryName,
            first.ProductionLineCode,
            first.ProductionLineName,
            first.ProductModelCode,
            first.ProductModelName,
            first.MainStageName,
            first.StageCode,
            first.StageName,
            null,
            null,
            group.Sum(record => record.ProducedQuantity),
            group.Sum(record => record.AcceptedQuantity),
            group.Sum(record => record.RejectedQuantity),
            null,
            group.Count(),
            1,
            group.SelectMany(record => record.Allocations).Select(allocation => allocation.WorkerId).Distinct().Count());
    }

    private static QuantitiesReportRowDto ToWorkerRow(IGrouping<Guid, Participation> group)
    {
        var first = group.OrderBy(participation => participation.Record.ProductionDate).ThenBy(participation => participation.Record.RecordId).First();
        return new QuantitiesReportRowDto(
            new ReportSourceReferenceDto("Worker", null, null, null, null, first.Allocation.WorkerId),
            null,
            first.Record.Status.ToString(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            first.Allocation.WorkerCode,
            first.Allocation.WorkerName,
            null,
            null,
            null,
            group.Sum(participation => participation.Allocation.EquivalentQuantity),
            group.Select(participation => participation.Record.RecordId).Distinct().Count(),
            group.Select(participation => participation.Record.ProductModelStageId).Distinct().Count(),
            1);
    }

    private static QuantitiesReportRowDto ToWorkerStageRow(Participation participation) => ToParticipationRow("StageProductionWorkerAllocation", participation);

    private static QuantitiesReportRowDto ToStageWorkerRow(Participation participation) => ToParticipationRow("StageProductionWorkerAllocation", participation);

    private static QuantitiesReportRowDto ToParticipationRow(string sourceType, Participation participation) => new(
        new ReportSourceReferenceDto(
            sourceType,
            participation.Record.RecordId,
            participation.Allocation.AllocationId,
            participation.Record.ProductionOrderId,
            participation.Record.ProductModelStageId,
            participation.Allocation.WorkerId),
        participation.Record.ProductionDate,
        participation.Record.Status.ToString(),
        participation.Record.ProductionOrderNumber,
        participation.Record.FactoryCode,
        participation.Record.FactoryName,
        participation.Record.ProductionLineCode,
        participation.Record.ProductionLineName,
        participation.Record.ProductModelCode,
        participation.Record.ProductModelName,
        participation.Record.MainStageName,
        participation.Record.StageCode,
        participation.Record.StageName,
        participation.Allocation.WorkerCode,
        participation.Allocation.WorkerName,
        participation.Record.ProducedQuantity,
        participation.Record.AcceptedQuantity,
        participation.Record.RejectedQuantity,
        participation.Allocation.EquivalentQuantity,
        1,
        1,
        1);

    private static IEnumerable<QuantitiesReportRowDto> ApplySorting(
        IEnumerable<QuantitiesReportRowDto> rows,
        QuantitiesReportSortBy sortBy,
        QuantitiesReportSortDirection direction) => sortBy switch
    {
        QuantitiesReportSortBy.ProductionDate => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.ProductionDate ?? DateOnly.MinValue).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.ProductionDate ?? DateOnly.MinValue).ThenBy(StableKey, StringComparer.Ordinal),
        QuantitiesReportSortBy.StageCode => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.StageCode ?? string.Empty, StringComparer.Ordinal).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.StageCode ?? string.Empty, StringComparer.Ordinal).ThenBy(StableKey, StringComparer.Ordinal),
        QuantitiesReportSortBy.WorkerCode => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.WorkerCode ?? string.Empty, StringComparer.Ordinal).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.WorkerCode ?? string.Empty, StringComparer.Ordinal).ThenBy(StableKey, StringComparer.Ordinal),
        QuantitiesReportSortBy.ProducedQuantity => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.ProducedQuantity ?? 0m).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.ProducedQuantity ?? 0m).ThenBy(StableKey, StringComparer.Ordinal),
        QuantitiesReportSortBy.AcceptedQuantity => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.AcceptedQuantity ?? 0m).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.AcceptedQuantity ?? 0m).ThenBy(StableKey, StringComparer.Ordinal),
        QuantitiesReportSortBy.RejectedQuantity => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.RejectedQuantity ?? 0m).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.RejectedQuantity ?? 0m).ThenBy(StableKey, StringComparer.Ordinal),
        QuantitiesReportSortBy.WorkerAllocatedQuantity => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.WorkerAllocatedQuantity ?? 0m).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.WorkerAllocatedQuantity ?? 0m).ThenBy(StableKey, StringComparer.Ordinal),
        QuantitiesReportSortBy.RecordCount => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.RecordCount).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.RecordCount).ThenBy(StableKey, StringComparer.Ordinal),
        QuantitiesReportSortBy.WorkerCount => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.WorkerCount).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.WorkerCount).ThenBy(StableKey, StringComparer.Ordinal),
        QuantitiesReportSortBy.StageCount => direction == QuantitiesReportSortDirection.Descending
            ? rows.OrderByDescending(row => row.StageCount).ThenBy(StableKey, StringComparer.Ordinal)
            : rows.OrderBy(row => row.StageCount).ThenBy(StableKey, StringComparer.Ordinal),
        _ => rows.OrderBy(StableKey, StringComparer.Ordinal)
    };

    private static string StableKey(QuantitiesReportRowDto row) =>
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

    private sealed record RecordData(
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
        string? ProductionOrderSourceReference)
    {
        public IReadOnlyCollection<AllocationData> Allocations { get; init; } = [];
        public bool IsDailyOperation => ProductionOrderSourceReference?.StartsWith("DailyProductionOperations/", StringComparison.Ordinal) == true;
    }

    private sealed record AllocationData(
        Guid AllocationId,
        Guid RecordId,
        Guid WorkerId,
        string WorkerCode,
        string WorkerName,
        decimal EquivalentQuantity);

    private sealed record Participation(RecordData Record, AllocationData Allocation);
}
