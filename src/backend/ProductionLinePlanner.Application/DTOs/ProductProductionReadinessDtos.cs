namespace ProductionLinePlanner.Application.DTOs;

public sealed record ProductStageReadinessDto(
    Guid ProductModelStageId,
    Guid SubStageId,
    string StageCode,
    string StageName,
    int StageOrder,
    string Status,
    string WorkerStatusText,
    int AssignedWorkers,
    int EligibleWorkers,
    int? RequiredWorkers,
    bool HasAuthoritativeRequiredWorkerCount,
    bool AttendanceDataAvailable,
    bool IsFinancialReviewPending,
    bool HasStagePriceConfiguration,
    bool HasCompensationConfiguration);

public sealed record ProductProductionReadinessDto(
    Guid ProductModelId,
    string ProductModelCode,
    string ProductModelName,
    Guid ProductionLineId,
    DateOnly ProductionDate,
    int TotalStages,
    int ReadyStages,
    int StagesWithoutWorkers,
    int StagesNeedingCompensationReview,
    int StagesWithoutAttendanceData,
    int IncompleteStages,
    string OverallReadinessState,
    bool ReadyForWorkflowTest,
    bool ReadyForProductionEntry,
    bool ReadyForFinancialApproval,
    IReadOnlyCollection<ProductStageReadinessDto> Stages,
    IReadOnlyCollection<ProductStageReadinessDto> ProblemStages);
