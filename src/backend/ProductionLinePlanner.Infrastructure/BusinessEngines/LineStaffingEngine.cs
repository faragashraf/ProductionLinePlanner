using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Resolves the organizational, permanent staffing plan for a line and model.
/// It intentionally never queries attendance or temporary-assignment records.
/// </summary>
public sealed class LineStaffingEngine(AppDbContext dbContext) : ILineStaffingEngine
{
    public async Task<Result<LineStaffingPlanDto>> GetLineStaffingPlanAsync(
        Guid factoryId,
        Guid productionLineId,
        Guid productModelId,
        DateOnly staffingReferenceDate,
        CancellationToken cancellationToken = default)
    {
        if (factoryId == Guid.Empty || productionLineId == Guid.Empty || productModelId == Guid.Empty)
        {
            return Result<LineStaffingPlanDto>.Failure(new Error("ValidationError", "FactoryId, ProductionLineId, and ProductModelId are required."));
        }

        var factory = await dbContext.Factories
            .AsNoTracking()
            .Where(candidate => candidate.Id == factoryId && candidate.IsActive)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (factory is null)
        {
            return Result<LineStaffingPlanDto>.Failure(new Error("NotFound", "Active factory was not found."));
        }

        var line = await dbContext.ProductionLines
            .AsNoTracking()
            .Where(candidate => candidate.Id == productionLineId && candidate.FactoryId == factoryId && candidate.IsActive)
            .Select(candidate => new { candidate.Id, candidate.Name, candidate.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken);
        if (line is null)
        {
            return Result<LineStaffingPlanDto>.Failure(new Error("NotFound", "Active production line was not found in the selected factory."));
        }
        if (line.DepartmentId is null)
        {
            return Result<LineStaffingPlanDto>.Failure(new Error("ValidationError", "يجب ربط خط الإنتاج بقسم قبل تحميل التسكين."));
        }

        var product = await dbContext.ProductModels
            .AsNoTracking()
            .Where(candidate => candidate.Id == productModelId && candidate.IsActive)
            .Select(candidate => new { candidate.Id, candidate.Code, candidate.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
        {
            return Result<LineStaffingPlanDto>.Failure(new Error("NotFound", "Active product model was not found."));
        }

        var stages = await dbContext.ProductModelStages
            .AsNoTracking()
            .Where(stage => stage.ProductModelId == productModelId
                            && stage.ProductionLineId == productionLineId
                            && stage.IsActive
                            && stage.SubStage != null
                            && stage.SubStage.IsActive
                            && stage.SubStage.MainStage != null
                            && stage.SubStage.MainStage.IsActive
                            && stage.SubStage.DepartmentId == line.DepartmentId.Value)
            .OrderBy(stage => stage.StageOrder)
            .Select(stage => new StageRow(
                stage.Id,
                stage.SubStageId,
                stage.SubStage!.MainStage!.Name,
                stage.SubStage.Code,
                stage.SubStage.Name,
                stage.StageOrder,
                stage.SubStage.Capacity,
                stage.PiecePrice,
                stage.CompensationMode))
            .ToArrayAsync(cancellationToken);

        var workersResult = await GetActiveStaffingWorkersAsync(staffingReferenceDate, cancellationToken);
        if (workersResult.IsFailure)
        {
            return Result<LineStaffingPlanDto>.Failure(workersResult.Error!);
        }
        var workerDtos = workersResult.Value!;

        var defaultCounts = await dbContext.WorkerDefaultAssignments
            .AsNoTracking()
            .Where(assignment => assignment.IsActive && assignment.ProductionLineId == productionLineId && stages.Select(stage => stage.SubStageId).Contains(assignment.SubStageId))
            .GroupBy(assignment => assignment.SubStageId)
            .Select(group => new { SubStageId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.SubStageId, item => item.Count, cancellationToken);
        var stagesWithWorkers = stages.Select(stage => ToStageDto(stage, workerDtos, defaultCounts.GetValueOrDefault(stage.SubStageId))).ToArray();
        var withoutWorkers = stagesWithWorkers.Count(stage => stage.EffectiveAssignedWorkersCount == 0);
        var staffingReview = stagesWithWorkers.Count(stage => stage.StaffingStatus == "NeedsStaffingReview");
        var compensationReview = stagesWithWorkers.Count(stage => stage.IsFinancialReviewPending);
        var staffingComplete = stagesWithWorkers.Length > 0 && withoutWorkers == 0 && staffingReview == 0;
        var overallStatus = stagesWithWorkers.Length == 0
            ? "NoModelStages"
            : staffingComplete ? "StaffingPlanComplete" : "NeedsStaffing";

        return Result<LineStaffingPlanDto>.Success(new LineStaffingPlanDto(
            factoryId,
            factory.Name,
            productionLineId,
            line.Name,
            productModelId,
            product.Code,
            product.Name,
            staffingReferenceDate,
            stagesWithWorkers.Length,
            stagesWithWorkers.Count(stage => stage.EffectiveAssignedWorkersCount > 0),
            withoutWorkers,
            0,
            compensationReview,
            staffingReview,
            overallStatus,
            staffingComplete,
            OperationalAttendanceChecked: false,
            FinancialConfigurationPending: compensationReview > 0,
            stagesWithWorkers,
            workerDtos));
    }

    public async Task<Result<LineStaffingStageRefreshDto>> GetLineStaffingStageRefreshAsync(
        Guid factoryId,
        Guid productionLineId,
        Guid productModelId,
        Guid subStageId,
        DateOnly staffingReferenceDate,
        CancellationToken cancellationToken = default)
    {
        if (subStageId == Guid.Empty)
        {
            return Result<LineStaffingStageRefreshDto>.Failure(new Error("ValidationError", "SubStageId is required."));
        }

        var planResult = await GetLineStaffingPlanAsync(
            factoryId,
            productionLineId,
            productModelId,
            staffingReferenceDate,
            cancellationToken);
        if (planResult.IsFailure)
        {
            return Result<LineStaffingStageRefreshDto>.Failure(planResult.Error!);
        }

        var plan = planResult.Value!;
        var stage = plan.Stages.SingleOrDefault(candidate => candidate.SubStageId == subStageId);
        if (stage is null)
        {
            return Result<LineStaffingStageRefreshDto>.Failure(new Error("NotFound", "The selected stage does not belong to the current line and model."));
        }

        return Result<LineStaffingStageRefreshDto>.Success(new LineStaffingStageRefreshDto(
            stage,
            plan.Stages,
            plan.Workers,
            plan.StagesWithWorkers,
            plan.StagesWithoutWorkers,
            plan.StagesWithTemporaryAssignments,
            plan.StagesNeedingCompensationReview,
            plan.StagesNeedingStaffingReview,
            plan.OverallStaffingStatus,
            plan.StaffingPlanComplete,
            plan.OperationalAttendanceChecked,
            plan.FinancialConfigurationPending));
    }

    public async Task<Result<IReadOnlyCollection<LineStaffingWorkerDto>>> GetActiveStaffingWorkersAsync(
        DateOnly staffingReferenceDate,
        CancellationToken cancellationToken = default)
    {
        var workers = await dbContext.Workers
            .AsNoTracking()
            .Where(worker => worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active)
            .OrderBy(worker => worker.EmployeeCode)
            .Select(worker => new WorkerRow(
                worker.Id,
                worker.EmployeeCode,
                worker.FullName,
                worker.LocalDepartmentName,
                worker.IsActive && worker.EmploymentStatus == EmploymentStatus.Active,
                worker.PhotoReference))
            .ToArrayAsync(cancellationToken);

        if (workers.Length == 0)
        {
            return Result<IReadOnlyCollection<LineStaffingWorkerDto>>.Success([]);
        }

        var workerIds = workers.Select(worker => worker.Id).ToArray();
        var resolvedAssignments = (await dbContext.WorkerDefaultAssignments
                .AsNoTracking()
                .Where(assignment => assignment.IsActive && workerIds.Contains(assignment.WorkerId))
                .Select(assignment => new DefaultAssignmentRow(
                    assignment.Id,
                    assignment.WorkerId,
                    assignment.SubStageId,
                    assignment.AssignedAt))
                .ToArrayAsync(cancellationToken))
            .GroupBy(assignment => assignment.WorkerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<WorkerAssignmentState>)group
                    .Select(assignment => new WorkerAssignmentState(
                        assignment.Id,
                        assignment.WorkerId,
                        AssignmentType.Default,
                        assignment.AssignedAt,
                        null,
                        assignment.SubStageId,
                        null,
                        assignment.SubStageId,
                        null))
                    .ToArray());
        var referencedSubStageIds = resolvedAssignments.Values.SelectMany(assignments => assignments)
            .SelectMany(assignment => new[] { assignment.EffectiveSubStageId, assignment.FromSubStageId })
            .Where(subStageId => subStageId.HasValue)
            .Select(subStageId => subStageId!.Value)
            .Distinct()
            .ToArray();
        var subStageNames = referencedSubStageIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.SubStages
                .AsNoTracking()
                .Where(stage => referencedSubStageIds.Contains(stage.Id))
                .Select(stage => new { stage.Id, stage.Name })
                .ToDictionaryAsync(stage => stage.Id, stage => stage.Name, cancellationToken);

        IReadOnlyCollection<LineStaffingWorkerDto> workerDtos = workers.Select(worker => ToWorkerDto(
                worker,
                resolvedAssignments.GetValueOrDefault(worker.Id) ?? [],
                subStageNames))
            .ToArray();
        return Result<IReadOnlyCollection<LineStaffingWorkerDto>>.Success(workerDtos);
    }

    private static LineStaffingStageDto ToStageDto(StageRow stage, IReadOnlyCollection<LineStaffingWorkerDto> workers, int defaultCount)
    {
        var effectiveWorkers = workers
            .Where(worker => worker.Participations.Any(participation => participation.SubStageId == stage.SubStageId))
            .OrderBy(worker => worker.EmployeeCode)
            .ToArray();
        var hasRequiredWorkers = stage.Capacity > 0;
        var compensationConfigured = stage.PiecePrice >= 0m && Enum.IsDefined(stage.CompensationMode);
        var financialReviewPending = stage.CompensationMode == CompensationMode.SharedPercentage;
        var staffingStatus = effectiveWorkers.Length == 0
            ? "NeedsStaffing"
            : hasRequiredWorkers && effectiveWorkers.Length < stage.Capacity
                ? "NeedsStaffingReview"
                : "Staffed";
        var workerStatusText = effectiveWorkers.Length == 0
            ? "لا يوجد عمال مسكنون"
            : effectiveWorkers.Length == 1 ? "يوجد عامل واحد" : $"يوجد {effectiveWorkers.Length} عمال";

        return new LineStaffingStageDto(
            stage.ProductModelStageId,
            stage.SubStageId,
            stage.MainStageName,
            stage.StageCode,
            stage.StageName,
            stage.StageOrder,
            stage.PiecePrice,
            stage.CompensationMode.ToString(),
            compensationConfigured ? (financialReviewPending ? "FinancialReviewPending" : "Configured") : "NeedsReview",
            financialReviewPending,
            defaultCount,
            effectiveWorkers.Length,
            0,
            hasRequiredWorkers ? stage.Capacity : null,
            hasRequiredWorkers,
            staffingStatus,
            workerStatusText,
            effectiveWorkers.Select(worker => worker.WorkerId).ToArray());
    }

    private static LineStaffingWorkerDto ToWorkerDto(
        WorkerRow worker,
        IReadOnlyCollection<WorkerAssignmentState> effectiveAssignments,
        IReadOnlyDictionary<Guid, string> subStageNames)
    {
        var participations = effectiveAssignments
            .Where(assignment => assignment.AssignmentId.HasValue && assignment.EffectiveSubStageId.HasValue && assignment.AssignmentType.HasValue)
            .Select(assignment => new LineStaffingParticipationDto(
                assignment.AssignmentId!.Value,
                assignment.AssignmentType!.Value.ToString(),
                assignment.EffectiveSubStageId!.Value,
                NameFor(assignment.EffectiveSubStageId, subStageNames),
                assignment.FromSubStageId,
                NameFor(assignment.FromSubStageId, subStageNames),
                assignment.StartsAtUtc,
                assignment.EndsAtUtc,
                assignment.ReplacementForWorkerId,
                assignment.ParticipationMode?.ToString()))
            .OrderBy(participation => participation.SubStageName, StringComparer.Ordinal)
            .ThenBy(participation => participation.SubStageId)
            .ToArray();
        var defaultAssignment = effectiveAssignments
            .Where(assignment => assignment.AssignmentType == AssignmentType.Default)
            .OrderByDescending(assignment => assignment.StartsAtUtc)
            .ThenByDescending(assignment => assignment.AssignmentId)
            .FirstOrDefault();
        var primaryAssignment = defaultAssignment;
        var photoVersion = GetPhotoVersion(worker.PhotoReference);
        var hasPhoto = photoVersion is not null && IsManagedPhotoReference(worker.PhotoReference, worker.Id);
        return new LineStaffingWorkerDto(
            worker.Id,
            worker.EmployeeCode,
            worker.FullName,
            worker.DepartmentName,
            worker.IsOnActiveService,
            hasPhoto,
            hasPhoto ? $"/api/workers/{worker.Id:D}/photo" + (photoVersion is null ? string.Empty : $"?v={photoVersion}") : null,
            photoVersion,
            defaultAssignment?.EffectiveSubStageId,
            NameFor(defaultAssignment?.EffectiveSubStageId, subStageNames),
            primaryAssignment?.AssignmentId,
            primaryAssignment?.AssignmentType?.ToString(),
            primaryAssignment?.EffectiveSubStageId,
            NameFor(primaryAssignment?.EffectiveSubStageId, subStageNames),
            primaryAssignment?.FromSubStageId,
            NameFor(primaryAssignment?.FromSubStageId, subStageNames),
            null,
            null,
            null,
            participations);
    }

    private static string? GetPhotoVersion(string? photoReference)
    {
        if (string.IsNullOrWhiteSpace(photoReference)) return null;
        var marker = photoReference.IndexOf("?v=", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;
        var version = photoReference[(marker + 3)..].Split('&', 2)[0].Trim();
        return version.Length == 0 ? null : version;
    }

    private static bool IsManagedPhotoReference(string? photoReference, Guid workerId) =>
        !string.IsNullOrWhiteSpace(photoReference) &&
        photoReference.StartsWith($"/api/workers/{workerId:D}/photo?v=", StringComparison.OrdinalIgnoreCase);

    private static string? NameFor(Guid? subStageId, IReadOnlyDictionary<Guid, string> names) =>
        subStageId.HasValue && names.TryGetValue(subStageId.Value, out var name) ? name : null;

    private sealed record StageRow(
        Guid ProductModelStageId,
        Guid SubStageId,
        string MainStageName,
        string StageCode,
        string StageName,
        int StageOrder,
        int Capacity,
        decimal PiecePrice,
        CompensationMode CompensationMode);

    private sealed record WorkerRow(
        Guid Id,
        string EmployeeCode,
        string FullName,
        string? DepartmentName,
        bool IsOnActiveService,
        string? PhotoReference);

    private sealed record DefaultAssignmentRow(
        Guid Id,
        Guid WorkerId,
        Guid SubStageId,
        DateTime AssignedAt);
}
