namespace ProductionLinePlanner.Application.DTOs;

public sealed record ManufacturingCommandCenterQuery(
    DateOnly ProductionDate,
    Guid? FactoryId = null,
    Guid? DepartmentId = null,
    Guid? ProductionLineId = null,
    string? OperationStatus = null);

public sealed class ManufacturingCommandCenterDto
{
    public required CommandCenterScopeDto Scope { get; init; }
    public required CommandCenterStructureCatalogDto FilterCatalog { get; init; }
    public required CommandCenterWorkforceDto Workforce { get; init; }
    public required CommandCenterLineSummaryDto LineSummary { get; init; }
    public required CommandCenterOperationsSummaryDto Operations { get; init; }
    public required CommandCenterDataQualityDto DataQuality { get; init; }
    public required IReadOnlyCollection<CommandCenterFactoryDto> Factories { get; init; }
    public DateTime CalculatedAtUtc { get; init; }
}

public sealed record CommandCenterScopeDto(
    DateOnly ProductionDate,
    Guid? FactoryId,
    Guid? DepartmentId,
    Guid? ProductionLineId,
    string OperationStatus,
    string Description);

public sealed record CommandCenterStructureCatalogDto(
    IReadOnlyCollection<CommandCenterFactoryOptionDto> Factories,
    IReadOnlyCollection<CommandCenterDepartmentOptionDto> Departments,
    IReadOnlyCollection<CommandCenterLineOptionDto> Lines);

public sealed record CommandCenterFactoryOptionDto(Guid Id, string Name, string Code);
public sealed record CommandCenterDepartmentOptionDto(Guid Id, Guid FactoryId, string Name, string Code);
public sealed record CommandCenterLineOptionDto(Guid Id, Guid FactoryId, Guid? DepartmentId, string Name, string? Code);

/// <summary>
/// A percentage is emitted only with its exact numerator and denominator. Percentage is null
/// when Denominator is zero or when the selected scope cannot attribute unassigned workers.
/// </summary>
public sealed record CommandCenterRatioDto(
    int Numerator,
    int Denominator,
    decimal? Percentage,
    string Scope,
    DateOnly Date,
    string ZeroBehavior);

public sealed class CommandCenterWorkforceDto
{
    public int? ActiveWorkers { get; init; }
    public int PresentWorkers { get; init; }
    public int PresentPermanentlyAssignedWorkers { get; init; }
    public int? PresentUnassignedWorkers { get; init; }
    public int PermanentlyAssignedNotPresentWorkers { get; init; }
    public required CommandCenterRatioDto AssignmentCoverage { get; init; }
    public bool AttendanceEvidenceComplete { get; init; }
    public required string AttributionNote { get; init; }
    public required IReadOnlyCollection<CommandCenterWorkerDetailDto> PresentAssignedDetails { get; init; }
    public required IReadOnlyCollection<CommandCenterWorkerDetailDto> PresentUnassignedDetails { get; init; }
    public required IReadOnlyCollection<CommandCenterWorkerDetailDto> AssignedNotPresentDetails { get; init; }
}

public sealed record CommandCenterWorkerDetailDto(
    Guid WorkerId,
    string WorkerCode,
    string WorkerName,
    string AttendanceStatus,
    IReadOnlyCollection<string> PermanentAssignments);

public sealed record CommandCenterLineSummaryDto(
    int ActiveLines,
    int ReadyLines,
    int StaffingShortageLines,
    int JourneyNotConfiguredLines,
    int DataIncompleteLines,
    int ProblemLines,
    int StagesWithoutPresentWorker);

public sealed record CommandCenterOperationsSummaryDto(
    int LinesWithOperation,
    int LinesWithoutOperation,
    int DraftOperations,
    int ApprovedOperations,
    int ApprovalCancelledOperations,
    int CancelledOperations,
    decimal ApprovedRecordedValue,
    IReadOnlyCollection<CommandCenterOperationDto> Items);

public sealed record CommandCenterDataQualityDto(
    int ModelStagesWithoutPrice,
    int ModelStagesWithoutStandardTime,
    int ActiveJourneyStagesWithoutPresentWorker,
    int? ActiveModelsWithoutJourney,
    IReadOnlyCollection<CommandCenterQualityIssueDto> Issues,
    string ModelsWithoutJourneyScopeNote);

public sealed record CommandCenterQualityIssueDto(
    string Type,
    string Title,
    string Detail,
    Guid? FactoryId,
    Guid? DepartmentId,
    Guid? ProductionLineId,
    Guid? ProductModelId,
    Guid? ProductModelStageId);

public sealed record CommandCenterFactoryDto(
    Guid Id,
    string Name,
    string Code,
    int ActiveDepartments,
    int ActiveLines,
    int PresentPermanentlyAssignedWorkers,
    int ProblemLines,
    int DraftOperations,
    int ApprovedOperations,
    IReadOnlyCollection<CommandCenterDepartmentDto> Departments);

public sealed record CommandCenterDepartmentDto(
    Guid? Id,
    string Name,
    string? Code,
    int ActiveLines,
    int PresentPermanentlyAssignedWorkers,
    int PermanentlyAssignedWorkers,
    int? PresentUnassignedWorkers,
    int ReadyLines,
    int NotReadyLines,
    int DraftOperations,
    int ApprovedOperations,
    string WorkforceAttributionNote,
    IReadOnlyCollection<CommandCenterLineDto> Lines);

public sealed record CommandCenterLineDto(
    Guid Id,
    Guid FactoryId,
    Guid? DepartmentId,
    string Name,
    string? Code,
    string ReadinessStatus,
    int PermanentlyAssignedWorkers,
    int PresentPermanentlyAssignedWorkers,
    int RequiredWorkers,
    int JourneyStages,
    int StagesCoveredByPresentWorker,
    int StagesWithoutPresentWorker,
    DateTime LastReliableUpdateUtc,
    IReadOnlyCollection<string> Alerts,
    IReadOnlyCollection<CommandCenterOperationDto> Operations);

public sealed record CommandCenterOperationDto(
    Guid ProductionOrderId,
    Guid ProductionLineId,
    Guid ProductModelId,
    string ProductModelCode,
    string ProductModelName,
    string Status,
    decimal FinalLineQuantity,
    decimal RecordedStageValue,
    int RegisteredStages,
    int JourneyStages,
    CommandCenterRatioDto StageRegistrationCoverage,
    DateTime LastReliableUpdateUtc,
    IReadOnlyCollection<CommandCenterStageDto> Stages);

public sealed record CommandCenterStageDto(
    Guid ProductModelStageId,
    Guid SubStageId,
    string MainStageName,
    string StageCode,
    string StageName,
    int StageOrder,
    int RequiredWorkers,
    int PermanentlyAssignedWorkers,
    int PresentPermanentlyAssignedWorkers,
    bool HasPrice,
    bool HasStandardTime,
    bool IsRegistered,
    IReadOnlyCollection<string> Alerts);
