using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Reports.Quantities;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Shared read-only filter and physical-run grain rules for production reports.
/// Financial and quantities projections deliberately share these rules while
/// selecting different fields.
/// </summary>
internal static class ProductionReportQuerySupport
{
    internal const int MaximumPageSize = 200;

    internal static Error? ValidateCommon(QuantitiesReportFilterRequest request)
    {
        if (request.From == default || request.To == default)
            return new Error("ValidationError", "From and To are required.");
        if (request.To < request.From)
            return new Error("ValidationError", "To must be on or after From.");
        if (request.Page < 1 || request.PageSize < 1 || request.PageSize > MaximumPageSize)
            return new Error("ValidationError", $"Page must be at least 1 and PageSize must be between 1 and {MaximumPageSize}.");
        if (!Enum.IsDefined(request.View) || !Enum.IsDefined(request.SortDirection))
            return new Error("ValidationError", "Unsupported report view or sort direction.");
        if (request.Status.HasValue && !Enum.IsDefined(request.Status.Value))
            return new Error("ValidationError", "Unsupported production record status.");
        if (HasEmptyId(request.FactoryId) || HasEmptyId(request.ProductionLineId) || HasEmptyId(request.ProductModelId) ||
            HasEmptyId(request.ProductionOrderId) || HasEmptyId(request.ProductModelStageId) || HasEmptyId(request.WorkerId))
            return new Error("ValidationError", "Filter identifiers cannot be empty.");

        return null;
    }

    internal static IQueryable<StageProductionRecord> ApplyFilters(
        AppDbContext db,
        QuantitiesReportFilterRequest request,
        StageProductionRecordStatus appliedStatus) =>
        db.Set<StageProductionRecord>()
            .AsNoTracking()
            .Where(record =>
                record.ProductionDate >= request.From &&
                record.ProductionDate <= request.To &&
                record.Status == appliedStatus &&
                (!request.FactoryId.HasValue || record.ProductionOrder!.ProductionLine!.FactoryId == request.FactoryId.Value) &&
                (!request.ProductionLineId.HasValue || record.ProductionOrder!.ProductionLineId == request.ProductionLineId.Value) &&
                (!request.ProductModelId.HasValue || record.ProductionOrder!.ProductModelId == request.ProductModelId.Value) &&
                (!request.ProductionOrderId.HasValue || record.ProductionOrderId == request.ProductionOrderId.Value) &&
                (!request.ProductModelStageId.HasValue || record.ProductModelStageId == request.ProductModelStageId.Value) &&
                (!request.WorkerId.HasValue || record.WorkerAllocations.Any(allocation => allocation.WorkerId == request.WorkerId.Value)));

    /// <summary>
    /// Daily Operations stores one stage snapshot per stage for one physical
    /// line run. Only that verified aggregate is deduplicated by order identity.
    /// Historical standalone records preserve their own record grain.
    /// </summary>
    internal static IReadOnlyCollection<T> SelectPhysicalRunRecords<T>(
        IEnumerable<T> records,
        Func<T, Guid> productionOrderId,
        Func<T, bool> isDailyOperation,
        Func<T, Guid> productModelStageId,
        Func<T, Guid> recordId)
    {
        var physicalRecords = new List<T>();
        foreach (var group in records.GroupBy(productionOrderId))
        {
            if (group.Any(isDailyOperation))
            {
                physicalRecords.Add(group
                    .OrderBy(productModelStageId)
                    .ThenBy(recordId)
                    .First());
            }
            else
            {
                physicalRecords.AddRange(group);
            }
        }

        return physicalRecords;
    }

    private static bool HasEmptyId(Guid? value) => value.HasValue && value.Value == Guid.Empty;
}
