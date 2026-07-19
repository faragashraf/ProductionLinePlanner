using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Calculates the operational readiness of one product on one line for one production date.
/// The calculation is read-only and deliberately keeps operational readiness separate from
/// financial review of the pilot's provisional SharedPercentage mappings.
/// </summary>
public sealed class ProductionReadinessEngine(
    AppDbContext dbContext,
    IAssignmentEngine assignmentEngine,
    IAttendanceEngine attendanceEngine,
    ICairoTimeZoneProvider cairoTimeZoneProvider) : IProductionReadinessEngine
{
    public async Task<Result<ProductProductionReadinessDto>> GetProductReadinessAsync(
        Guid productModelId,
        Guid productionLineId,
        DateOnly productionDate,
        CancellationToken cancellationToken = default)
    {
        if (productModelId == Guid.Empty || productionLineId == Guid.Empty)
        {
            return Result<ProductProductionReadinessDto>.Failure(new Error("ValidationError", "ProductModelId and ProductionLineId are required."));
        }

        var product = await dbContext.ProductModels
            .AsNoTracking()
            .Where(model => model.Id == productModelId && model.IsActive)
            .Select(model => new { model.Id, model.Code, model.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return Result<ProductProductionReadinessDto>.Failure(new Error("NotFound", "Active product model was not found."));
        }

        var stageRows = await dbContext.ProductModelStages
            .AsNoTracking()
            .Where(stage => stage.ProductModelId == productModelId
                            && stage.IsActive
                            && stage.SubStage != null
                            && stage.SubStage.IsActive
                            && stage.SubStage.MainStage != null
                            && stage.SubStage.MainStage.IsActive
                            && stage.SubStage.MainStage.ProductionLineId == productionLineId)
            .OrderBy(stage => stage.StageOrder)
            .Select(stage => new StageRow(
                stage.Id,
                stage.SubStageId,
                stage.SubStage!.Code,
                stage.SubStage.Name,
                stage.StageOrder,
                stage.SubStage.Capacity,
                stage.PiecePrice,
                stage.CompensationMode))
            .ToArrayAsync(cancellationToken);

        var activeWorkers = await dbContext.Workers
            .AsNoTracking()
            .Where(worker => worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active)
            .Select(worker => worker.Id)
            .ToArrayAsync(cancellationToken);

        var evidenceAtUtc = ProductionDateEvidenceAtUtc(productionDate);
        var assignmentsResult = await assignmentEngine.ResolveCurrentAssignmentsAsync(activeWorkers, evidenceAtUtc, cancellationToken);
        if (assignmentsResult.IsFailure)
        {
            return Result<ProductProductionReadinessDto>.Failure(assignmentsResult.Error!);
        }

        var attendanceResult = await attendanceEngine.GetLatestAttendanceStatusByWorkerAsync(activeWorkers, evidenceAtUtc, cancellationToken);
        if (attendanceResult.IsFailure)
        {
            return Result<ProductProductionReadinessDto>.Failure(attendanceResult.Error!);
        }

        var assignments = assignmentsResult.Value!;
        var attendance = attendanceResult.Value!;
        var stageReadiness = stageRows.Select(stage => ToStageReadiness(stage, assignments, attendance)).ToArray();
        var readyStages = stageReadiness.Count(stage => stage.Status == "Ready");
        var withoutWorkers = stageReadiness.Count(stage => stage.Status == "NeedsAssignment");
        var withoutAttendance = stageReadiness.Count(stage => stage.Status == "AttendanceUnavailable");
        var incomplete = stageReadiness.Count(stage => stage.Status == "Incomplete");
        var compensationReview = stageReadiness.Count(stage => stage.IsFinancialReviewPending);
        var workflowReady = stageReadiness.Length > 0;
        var productionReady = stageReadiness.Length > 0 && readyStages == stageReadiness.Length;
        var financialReady = productionReady && compensationReview == 0;

        var overallState = !workflowReady
            ? "NoActiveProductStages"
            : productionReady
                ? compensationReview > 0 ? "ReadyWithFinancialReview" : "Ready"
                : withoutWorkers > 0 ? "NeedsAssignment"
                    : withoutAttendance > 0 ? "AttendanceUnavailable"
                        : "Incomplete";

        return Result<ProductProductionReadinessDto>.Success(new ProductProductionReadinessDto(
            product.Id,
            product.Code,
            product.Name,
            productionLineId,
            productionDate,
            stageReadiness.Length,
            readyStages,
            withoutWorkers,
            compensationReview,
            withoutAttendance,
            incomplete,
            overallState,
            workflowReady,
            productionReady,
            financialReady,
            stageReadiness,
            stageReadiness.Where(stage => stage.Status != "Ready" || stage.IsFinancialReviewPending).ToArray()));
    }

    private static ProductStageReadinessDto ToStageReadiness(
        StageRow stage,
        IReadOnlyDictionary<Guid, WorkerAssignmentState> assignments,
        IReadOnlyDictionary<Guid, AttendanceStatusRecord> attendance)
    {
        var assignedWorkerIds = assignments.Values
            .Where(assignment => assignment.EffectiveSubStageId == stage.SubStageId)
            .Select(assignment => assignment.WorkerId)
            .Distinct()
            .ToArray();
        var attendanceStates = assignedWorkerIds
            .Where(attendance.ContainsKey)
            .Select(workerId => attendance[workerId])
            .ToArray();
        var eligibleWorkers = attendanceStates.Count(state => state.Status is AttendanceStatus.Present or AttendanceStatus.Late);
        var attendanceDataAvailable = attendanceStates.Length > 0;
        var hasAuthoritativeRequiredWorkerCount = stage.Capacity > 0;
        int? requiredWorkers = hasAuthoritativeRequiredWorkerCount ? stage.Capacity : null;
        var hasPriceConfiguration = stage.PiecePrice >= 0;
        var hasCompensationConfiguration = Enum.IsDefined(stage.CompensationMode);
        var status = !hasPriceConfiguration || !hasCompensationConfiguration
            ? "CompensationNeedsReview"
            : assignedWorkerIds.Length == 0
                ? "NeedsAssignment"
                : !attendanceDataAvailable
                    ? "AttendanceUnavailable"
                    : eligibleWorkers == 0 || (hasAuthoritativeRequiredWorkerCount && eligibleWorkers < stage.Capacity)
                        ? "Incomplete"
                        : "Ready";

        var workerStatusText = assignedWorkerIds.Length == 0
            ? "لا يوجد عمال مسكنون"
            : hasAuthoritativeRequiredWorkerCount
                ? $"{eligibleWorkers} / {stage.Capacity} عمال مؤهلون للإنتاج"
                : eligibleWorkers == 1
                    ? "يوجد عامل واحد مؤهل للإنتاج"
                    : $"يوجد {eligibleWorkers} عمال مؤهلون للإنتاج";

        // The pilot bootstrap persisted SharedPercentage as its explicit provisional default.
        // There is no separate financial-approval field on legacy mappings, so keep every
        // current SharedPercentage mapping visible for financial review rather than hiding it.
        var isFinancialReviewPending = stage.CompensationMode == CompensationMode.SharedPercentage;

        return new ProductStageReadinessDto(
            stage.ProductModelStageId,
            stage.SubStageId,
            stage.StageCode,
            stage.StageName,
            stage.StageOrder,
            status,
            workerStatusText,
            assignedWorkerIds.Length,
            eligibleWorkers,
            requiredWorkers,
            hasAuthoritativeRequiredWorkerCount,
            attendanceDataAvailable,
            isFinancialReviewPending,
            hasPriceConfiguration,
            hasCompensationConfiguration);
    }

    private DateTime ProductionDateEvidenceAtUtc(DateOnly productionDate)
    {
        var localEnd = productionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(localEnd, cairoTimeZoneProvider.TimeZone).AddTicks(-1);
    }

    private sealed record StageRow(
        Guid ProductModelStageId,
        Guid SubStageId,
        string StageCode,
        string StageName,
        int StageOrder,
        int Capacity,
        decimal PiecePrice,
        CompensationMode CompensationMode);
}
