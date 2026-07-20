using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class StageDependencyInspector(AppDbContext dbContext) : IStageDependencyInspector
{
    public async Task<Result<StageDependencySummaryDto>> InspectAsync(Guid subStageId, CancellationToken cancellationToken = default)
    {
        if (subStageId == Guid.Empty) return Result<StageDependencySummaryDto>.Failure(new Error("ValidationError", "StageId is required."));
        if (!await dbContext.SubStages.AnyAsync(x => x.Id == subStageId, cancellationToken)) return Result<StageDependencySummaryDto>.Failure(new Error("NotFound", "Operational stage not found."));

        var modelStageIds = await dbContext.ProductModelStages.Where(x => x.SubStageId == subStageId).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var active = new List<StageDependencyItemDto>();
        var history = new List<StageDependencyItemDto>();

        Add(active, "active-default-assignments", "تعيينات دائمة نشطة", await dbContext.WorkerDefaultAssignments.CountAsync(x => x.SubStageId == subStageId && x.IsActive, cancellationToken));
        Add(active, "active-temporary-assignments", "تعيينات مؤقتة مجدولة أو نشطة", await dbContext.WorkerTemporaryAssignments.CountAsync(x => (x.Status == "Scheduled" || x.Status == "Active") && (x.FromSubStageId == subStageId || x.ToSubStageId == subStageId), cancellationToken));
        Add(active, "active-model-stages", "إعدادات مراحل موديل نشطة", await dbContext.ProductModelStages.CountAsync(x => x.SubStageId == subStageId && x.IsActive, cancellationToken));
        Add(active, "open-production-records", "مسودات أو عمليات إنتاج مفتوحة", await dbContext.StageProductionRecords.CountAsync(x => modelStageIds.Contains(x.ProductModelStageId) && x.Status == StageProductionRecordStatus.Draft, cancellationToken));
        Add(active, "open-production-orders", "أوامر إنتاج مسودة أو نشطة", await dbContext.ProductionOrders.CountAsync(x => (x.Status == ProductionOrderStatus.Draft || x.Status == ProductionOrderStatus.Active) && dbContext.StageProductionRecords.Any(record => record.ProductionOrderId == x.Id && modelStageIds.Contains(record.ProductModelStageId)), cancellationToken));

        Add(history, "default-assignments", "كل التعيينات الدائمة", await dbContext.WorkerDefaultAssignments.CountAsync(x => x.SubStageId == subStageId, cancellationToken));
        Add(history, "temporary-assignments", "كل التعيينات المؤقتة", await dbContext.WorkerTemporaryAssignments.CountAsync(x => x.FromSubStageId == subStageId || x.ToSubStageId == subStageId, cancellationToken));
        Add(history, "assignment-timeline", "سجل حركة التعيينات", await dbContext.AssignmentTimelineEntries.CountAsync(x => x.FromSubStageId == subStageId || x.ToSubStageId == subStageId, cancellationToken));
        Add(history, "model-stages", "إعدادات مراحل الموديل", modelStageIds.Length);
        Add(history, "production-records", "سجلات الإنتاج ولقطات التسعير والتكلفة", await dbContext.StageProductionRecords.CountAsync(x => modelStageIds.Contains(x.ProductModelStageId), cancellationToken));
        Add(history, "production-worker-allocations", "توزيعات العمال المحفوظة", await dbContext.StageProductionWorkerAllocations.CountAsync(x =>
            dbContext.StageProductionRecords.Any(record => record.Id == x.StageProductionRecordId && modelStageIds.Contains(record.ProductModelStageId)), cancellationToken));
        Add(history, "production-resolutions", "قرارات معالجة مراحل الإنتاج", await dbContext.ProductionDayStageResolutions.CountAsync(x => modelStageIds.Contains(x.ProductModelStageId), cancellationToken));
        Add(history, "readiness-snapshots", "لقطات الجاهزية", await dbContext.StageReadinessSnapshots.CountAsync(x => x.ScopeEntityId == subStageId, cancellationToken));
        Add(history, "audit-logs", "سجل المراجعة", await dbContext.AuditLogs.CountAsync(x => x.EntityId == subStageId.ToString() && x.EntityType == "SubStage", cancellationToken));

        var result = new StageDependencySummaryDto
        {
            StageId = subStageId,
            ActiveBlockers = active,
            HistoricalDependencies = history,
            DisableMessageAr = active.Count == 0 ? "يمكن تعطيل المرحلة." : "لا يمكن تعطيل المرحلة لوجود: " + string.Join("، ", active.Select(x => $"{x.LabelAr} ({x.Count})")),
            DeleteMessageAr = active.Count == 0 && history.Count == 0 ? "يمكن حذف المرحلة." : "لا يمكن حذف المرحلة لوجود بيانات مرتبطة. يُقترح تعطيلها بدلاً من الحذف."
        };
        return Result<StageDependencySummaryDto>.Success(result);
    }

    private static void Add(ICollection<StageDependencyItemDto> destination, string key, string labelAr, int count)
    {
        if (count > 0) destination.Add(new StageDependencyItemDto { Key = key, LabelAr = labelAr, Count = count });
    }
}
