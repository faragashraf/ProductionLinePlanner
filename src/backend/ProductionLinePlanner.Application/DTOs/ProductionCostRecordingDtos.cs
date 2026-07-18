namespace ProductionLinePlanner.Application.DTOs;

public sealed record ProductionOrderDto(Guid Id, string OrderNumber, Guid ProductModelId, string ProductModelCode, Guid? ProductionLineId, DateOnly ProductionDate, decimal PlannedQuantity, string Status, string? Notes, bool IsImported, DateTime RecordedAtUtc, DateTime? ApprovedAtUtc);
public sealed record ProductionWorkerAllocationDto(Guid WorkerId, string WorkerCode, string WorkerName, decimal? Percentage, decimal? FixedAmount, decimal? InputQuantity, decimal EquivalentQuantity, decimal CalculatedEarning, string? Notes, string? ManualOverrideReason);
public sealed record StageProductionRecordDto(Guid Id, Guid ProductionOrderId, Guid ProductModelStageId, DateOnly ProductionDate, decimal ProducedQuantity, decimal AcceptedQuantity, decimal RejectedQuantity, string Status, string StageCode, string StageName, string ProductModelCode, string ProductModelName, string FactoryCode, string FactoryName, string ProductionLineCode, string ProductionLineName, string MainStageName, decimal PiecePrice, decimal? StandardSeconds, string CompensationMode, decimal TotalWorkerEarnings, Guid ConcurrencyToken, IReadOnlyCollection<ProductionWorkerAllocationDto> Workers, string? Notes, Guid? ApprovedByUserId, DateTime? ApprovedAtUtc, Guid? ApprovalCancelledByUserId, DateTime? ApprovalCancelledAtUtc, string? ApprovalCancellationReason);
public sealed record DailyProductionCostReportRowDto(Guid RecordId, DateOnly ProductionDate, string OrderNumber, string ModelCode, string StageCode, string StageName, decimal ProducedQuantity, decimal AcceptedQuantity, decimal RejectedQuantity, decimal StageCost, string CompensationMode, string Status, IReadOnlyCollection<ProductionWorkerAllocationDto> Workers);

/// <summary>
/// Daily operations resolves organizational staffing and attendance separately.
/// A missing attendance row is NoSourceCheckIn; it is not silently reported as
/// an employment or assignment failure.
/// </summary>
public sealed record DailyProductionWorkerDto(
    Guid WorkerId,
    string WorkerCode,
    string WorkerName,
    bool IsOnActiveService,
    string? EffectiveAssignmentType,
    string AttendanceStatus,
    bool HasSourceCheckIn,
    bool IsPresent,
    bool RequiresAuthorizedOverride,
    decimal? SuggestedPercentage,
    DateTime? ContributionStartsAtUtc,
    DateTime? ContributionEndsAtUtc,
    int WorkerMinutes,
    bool IsProductionReady,
    string? ExclusionReason,
    bool IsAssignedWorker,
    bool IsDailyOverride);

public sealed record DailyProductionStageDto(
    Guid ProductModelStageId,
    Guid SubStageId,
    string MainStageName,
    string StageCode,
    string StageName,
    int StageOrder,
    decimal PiecePrice,
    string CompensationMode,
    string StaffingStatus,
    string AttendanceStatus,
    bool HasAbsentWorkers,
    bool HasNoSourceCheckInWorkers,
    bool IsFinancialReviewPending,
    bool IsReady,
    IReadOnlyCollection<DailyProductionWorkerDto> Workers);

public sealed record DailyProductionOperationsDto(
    Guid FactoryId,
    string FactoryName,
    Guid ProductionLineId,
    string ProductionLineName,
    Guid ProductModelId,
    string ProductModelCode,
    string ProductModelName,
    DateOnly ProductionDate,
    string StaffingContextVersion,
    int TotalStages,
    int ReadyStages,
    int StagesWithAbsentWorkers,
    int StagesWithNoSourceCheckIn,
    int StagesWithoutStaffing,
    int StagesRequiringCostReview,
    IReadOnlyCollection<DailyProductionStageDto> Stages,
    IReadOnlyCollection<DailyProductionWorkerDto> ActiveWorkers,
    DailyProductionDraftDto? ExistingDraft);

public sealed record DailyProductionStagePreviewDto(
    Guid ProductModelStageId,
    string StageCode,
    string StageName,
    decimal StageQuantity,
    decimal StageCost,
    string CompensationMode,
    IReadOnlyCollection<ProductionWorkerAllocationDto> Workers,
    IReadOnlyCollection<string> Warnings);

public sealed record DailyProductionPreviewDto(
    DateOnly ProductionDate,
    decimal LineQuantity,
    string PreviewToken,
    decimal TotalWorkerEntitlements,
    IReadOnlyCollection<DailyProductionStagePreviewDto> Stages,
    IReadOnlyCollection<DailyProductionWorkerTotalDto> WorkerTotals,
    IReadOnlyCollection<string> Warnings);

/// <summary>
/// Aggregate display total only. Stage allocations remain the source of truth
/// so the same worker can retain an entitlement on multiple stages.
/// </summary>
public sealed record DailyProductionWorkerTotalDto(
    Guid WorkerId,
    string WorkerCode,
    string WorkerName,
    decimal TotalEntitlement);

public sealed record DailyProductionDraftDto(
    Guid ProductionOrderId,
    string OrderNumber,
    DateOnly ProductionDate,
    DateTime RecordedAtUtc,
    decimal LineQuantity,
    bool WasAlreadySaved,
    IReadOnlyCollection<StageProductionRecordDto> Stages);

public sealed record DailyProductionApprovalDto(
    Guid ProductionOrderId,
    string OrderStatus,
    DateTime ApprovedAtUtc,
    int ApprovedStageCount);
