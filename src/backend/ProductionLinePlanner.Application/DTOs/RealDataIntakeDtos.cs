namespace ProductionLinePlanner.Application.DTOs;

public sealed record IntakeWorkbookFile(string FileName, byte[] Content);
public sealed record ProductionDayQuantityInput(DateOnly ProductionDate, decimal LineQuantity);
public sealed record RealDataIntakeUpload(
    string FactoryName,
    string ProductionLineName,
    string ProductName,
    IntakeWorkbookFile StagesWorkbook,
    IntakeWorkbookFile SalaryWorkbook,
    IntakeWorkbookFile ProductionWorkbook,
    IReadOnlyCollection<ProductionDayQuantityInput> ProductionDayQuantities);

public sealed record IntakeIssueDto(string Severity, string Code, string Message, int? SourceRow = null);
public sealed record StageIntakePreviewRowDto(int SourceRow, string StageCode, string MainStageName, string SubStageName, decimal PiecePrice, decimal? StandardSeconds, string Action, IReadOnlyCollection<IntakeIssueDto> Issues);
public sealed record ProductStageMappingPreviewRowDto(string StageCode, string MainStageName, string SubStageName, string Action, IReadOnlyCollection<IntakeIssueDto> Issues);
public sealed record WorkerIntakePreviewRowDto(int SourceRow, string EmployeeCode, string SourceName, string? MatchedWorkerCode, string? CurrentDepartment, string? IncomingDepartment, decimal? CurrentSalary, decimal? IncomingSalary, string Action, IReadOnlyCollection<IntakeIssueDto> Issues);
public sealed record ProductionDayHeaderPreviewDto(DateOnly ProductionDate, decimal LineQuantity, string Action, IReadOnlyCollection<IntakeIssueDto> Issues);
public sealed record ProductionStagePreviewRowDto(DateOnly ProductionDate, int SourceRow, string StageName, string? StageCode, int WorkerAllocationCount, string Action, IReadOnlyCollection<IntakeIssueDto> Issues);
public sealed record MissingProductStagePreviewDto(DateOnly ProductionDate, string StageCode, string StageName, string Severity, string Message);

public sealed class RealDataIntakePreviewDto
{
    public string IdempotencyKey { get; init; } = string.Empty;
    public bool CanApply { get; init; }
    public int ParsedStageRows { get; init; }
    public int ParsedWorkerRows { get; init; }
    public int ParsedProductionWorkerRows { get; init; }
    public IReadOnlyCollection<StageIntakePreviewRowDto> Stages { get; init; } = [];
    public IReadOnlyCollection<ProductStageMappingPreviewRowDto> ProductStageMappings { get; init; } = [];
    public IReadOnlyCollection<WorkerIntakePreviewRowDto> Workers { get; init; } = [];
    public IReadOnlyCollection<ProductionDayHeaderPreviewDto> ProductionDays { get; init; } = [];
    public IReadOnlyCollection<ProductionStagePreviewRowDto> ProductionStages { get; init; } = [];
    public IReadOnlyCollection<MissingProductStagePreviewDto> MissingProductStages { get; init; } = [];
    public IReadOnlyCollection<IntakeIssueDto> Issues { get; init; } = [];
}

public sealed record RealDataIntakeApplyResultDto(string BatchId, string IdempotencyKey, bool WasAlreadyApplied, int StagesCreated, int StagesUpdated, int WorkersUpdated, int ProductionDaysCreated, int StageRecordsCreated, int WorkerAllocationsCreated, int OpenReviewIssues);

public sealed record ProductionDayReviewIssueDto(Guid ProductModelStageId, string StageCode, string StageName, string Status, string Message, string? ResolutionReason);
public sealed record ProductionDayReviewAllocationDto(Guid StageProductionRecordId, Guid ProductModelStageId, string StageCode, string StageName, Guid WorkerId, string WorkerCode, string WorkerName, decimal? InputQuantity, decimal CalculatedEarning, string? ManualOverrideReason);
public sealed record ProductionDayReviewDto(Guid ProductionOrderId, DateOnly ProductionDate, DateTime RecordedAtUtc, string Status, decimal LineQuantity, string FactoryName, string ProductionLineName, string ProductName, int StageRecordCount, int WorkerAllocationCount, IReadOnlyCollection<ProductionDayReviewIssueDto> Issues, IReadOnlyCollection<ProductionDayReviewAllocationDto> Allocations);
