namespace ProductionLinePlanner.Application.DTOs;

/// <summary>
/// Read-only organizational staffing plan. It deliberately has no attendance
/// state: attendance eligibility belongs to the later daily-operations flow.
/// </summary>
public sealed record LineStaffingPlanDto(
    Guid FactoryId,
    string FactoryName,
    Guid ProductionLineId,
    string ProductionLineName,
    Guid ProductModelId,
    string ProductModelCode,
    string ProductModelName,
    DateOnly StaffingReferenceDate,
    int TotalStages,
    int StagesWithWorkers,
    int StagesWithoutWorkers,
    int StagesWithTemporaryAssignments,
    int StagesNeedingCompensationReview,
    int StagesNeedingStaffingReview,
    string OverallStaffingStatus,
    bool StaffingPlanComplete,
    bool OperationalAttendanceChecked,
    bool FinancialConfigurationPending,
    IReadOnlyCollection<LineStaffingStageDto> Stages,
    IReadOnlyCollection<LineStaffingWorkerDto> Workers);

/// <summary>
/// Authoritative, narrow refresh payload for one staffing stage after an
/// assignment mutation. Clients merge it without replacing the complete plan.
/// </summary>
public sealed record LineStaffingStageRefreshDto(
    LineStaffingStageDto Stage,
    IReadOnlyCollection<LineStaffingStageDto> Stages,
    IReadOnlyCollection<LineStaffingWorkerDto> Workers,
    int StagesWithWorkers,
    int StagesWithoutWorkers,
    int StagesWithTemporaryAssignments,
    int StagesNeedingCompensationReview,
    int StagesNeedingStaffingReview,
    string OverallStaffingStatus,
    bool StaffingPlanComplete,
    bool OperationalAttendanceChecked,
    bool FinancialConfigurationPending);

public sealed record LineStaffingStageDto(
    Guid ProductModelStageId,
    Guid SubStageId,
    string MainStageName,
    string StageCode,
    string StageName,
    int StageOrder,
    decimal PiecePrice,
    string CompensationMode,
    string CompensationConfigurationStatus,
    bool IsFinancialReviewPending,
    int DefaultAssignedWorkersCount,
    int EffectiveAssignedWorkersCount,
    int TemporaryAssignedWorkersCount,
    int? RequiredWorkers,
    bool HasAuthoritativeRequiredWorkerCount,
    string StaffingStatus,
    string WorkerStatusText,
    IReadOnlyCollection<Guid> EffectiveWorkerIds);

public sealed record LineStaffingWorkerDto(
    Guid WorkerId,
    string EmployeeCode,
    string FullName,
    string? DepartmentName,
    bool IsOnActiveService,
    bool HasPhoto,
    string? PhotoReference,
    string? PhotoVersion,
    Guid? DefaultSubStageId,
    string? DefaultSubStageName,
    Guid? EffectiveAssignmentId,
    string? EffectiveAssignmentType,
    Guid? EffectiveSubStageId,
    string? EffectiveSubStageName,
    Guid? FromSubStageId,
    string? FromSubStageName,
    DateTime? TemporaryStartsAtUtc,
    DateTime? TemporaryEndsAtUtc,
    Guid? ReplacementForWorkerId,
    IReadOnlyCollection<LineStaffingParticipationDto> Participations);

/// <summary>
/// One effective stage participation.  A worker can intentionally have more
/// than one item for the same staffing reference date.
/// </summary>
public sealed record LineStaffingParticipationDto(
    Guid AssignmentId,
    string AssignmentType,
    Guid ProductionLineId,
    Guid SubStageId,
    string? SubStageName,
    Guid? FromSubStageId,
    string? FromSubStageName,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    Guid? ReplacementForWorkerId,
    string? TemporaryParticipationMode);
