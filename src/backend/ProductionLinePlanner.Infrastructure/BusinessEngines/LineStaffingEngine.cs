using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Resolves the organizational staffing plan for a line and model. It uses the
/// existing assignment engine, but intentionally never queries attendance.
/// </summary>
public sealed class LineStaffingEngine(
    AppDbContext dbContext,
    IAssignmentEngine assignmentEngine) : ILineStaffingEngine
{
    private static readonly TimeZoneInfo EgyptTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

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
            .Select(candidate => new { candidate.Id, candidate.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (line is null)
        {
            return Result<LineStaffingPlanDto>.Failure(new Error("NotFound", "Active production line was not found in the selected factory."));
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

        var stagesWithWorkers = stages.Select(stage => ToStageDto(stage, workerDtos)).ToArray();
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
            stagesWithWorkers.Count(stage => stage.TemporaryAssignedWorkersCount > 0),
            compensationReview,
            staffingReview,
            overallStatus,
            staffingComplete,
            OperationalAttendanceChecked: false,
            FinancialConfigurationPending: compensationReview > 0,
            stagesWithWorkers,
            workerDtos));
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
        var assignmentsResult = await assignmentEngine.ResolveCurrentAssignmentsAsync(
            workerIds,
            StaffingReferenceAtUtc(staffingReferenceDate),
            cancellationToken);
        if (assignmentsResult.IsFailure)
        {
            return Result<IReadOnlyCollection<LineStaffingWorkerDto>>.Failure(assignmentsResult.Error!);
        }

        var defaultAssignments = await dbContext.WorkerDefaultAssignments
            .AsNoTracking()
            .Where(assignment => workerIds.Contains(assignment.WorkerId) && assignment.IsActive)
            .OrderByDescending(assignment => assignment.AssignedAt)
            .ThenByDescending(assignment => assignment.Id)
            .Select(assignment => new { assignment.WorkerId, assignment.SubStageId })
            .ToArrayAsync(cancellationToken);
        var defaultByWorker = defaultAssignments
            .GroupBy(assignment => assignment.WorkerId)
            .ToDictionary(group => group.Key, group => group.First().SubStageId);

        var resolvedAssignments = assignmentsResult.Value!;
        var referencedSubStageIds = defaultByWorker.Values.Select(subStageId => (Guid?)subStageId)
            .Concat(resolvedAssignments.Values.Select(assignment => assignment.EffectiveSubStageId))
            .Concat(resolvedAssignments.Values.Select(assignment => assignment.FromSubStageId))
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
                defaultByWorker.GetValueOrDefault(worker.Id),
                resolvedAssignments.GetValueOrDefault(worker.Id),
                subStageNames))
            .ToArray();
        return Result<IReadOnlyCollection<LineStaffingWorkerDto>>.Success(workerDtos);
    }

    private static LineStaffingStageDto ToStageDto(StageRow stage, IReadOnlyCollection<LineStaffingWorkerDto> workers)
    {
        var effectiveWorkers = workers
            .Where(worker => worker.EffectiveSubStageId == stage.SubStageId)
            .OrderBy(worker => worker.EmployeeCode)
            .ToArray();
        var defaultCount = workers.Count(worker => worker.DefaultSubStageId == stage.SubStageId);
        var temporaryCount = effectiveWorkers.Count(worker => worker.EffectiveAssignmentType is "Temporary" or "Replacement");
        var hasRequiredWorkers = stage.Capacity > 0;
        var compensationConfigured = stage.PiecePrice >= 0m && Enum.IsDefined(stage.CompensationMode);
        var financialReviewPending = stage.CompensationMode == CompensationMode.SharedPercentage;
        var staffingStatus = effectiveWorkers.Length == 0
            ? "NeedsStaffing"
            : hasRequiredWorkers && effectiveWorkers.Length < stage.Capacity
                ? "NeedsStaffingReview"
                : "Staffed";
        var workerStatusText = effectiveWorkers.Length == 0
            ? "لا يوجد عمال معينون"
            : temporaryCount > 0
                ? effectiveWorkers.Length == 1 ? "يوجد تعيين مؤقت" : $"يوجد {effectiveWorkers.Length} عمال، منهم تعيين مؤقت"
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
            temporaryCount,
            hasRequiredWorkers ? stage.Capacity : null,
            hasRequiredWorkers,
            staffingStatus,
            workerStatusText,
            effectiveWorkers.Select(worker => worker.WorkerId).ToArray());
    }

    private static LineStaffingWorkerDto ToWorkerDto(
        WorkerRow worker,
        Guid? defaultSubStageId,
        WorkerAssignmentState? effectiveAssignment,
        IReadOnlyDictionary<Guid, string> subStageNames)
    {
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
            defaultSubStageId,
            NameFor(defaultSubStageId, subStageNames),
            effectiveAssignment?.AssignmentId,
            effectiveAssignment?.AssignmentType?.ToString(),
            effectiveAssignment?.EffectiveSubStageId,
            NameFor(effectiveAssignment?.EffectiveSubStageId, subStageNames),
            effectiveAssignment?.FromSubStageId,
            NameFor(effectiveAssignment?.FromSubStageId, subStageNames),
            effectiveAssignment?.AssignmentType is AssignmentType.Temporary or AssignmentType.Replacement ? effectiveAssignment.StartsAtUtc : null,
            effectiveAssignment?.AssignmentType is AssignmentType.Temporary or AssignmentType.Replacement ? effectiveAssignment.EndsAtUtc : null,
            effectiveAssignment?.ReplacementForWorkerId);
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

    private static DateTime StaffingReferenceAtUtc(DateOnly referenceDate)
    {
        var localEnd = referenceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(localEnd, EgyptTimeZone).AddTicks(-1);
    }

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
}
