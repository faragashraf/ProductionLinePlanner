using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

/// <summary>In-memory source for the one-time pilot master-data bootstrap. No source file is persisted.</summary>
public sealed record PilotMasterDataBootstrapInput(
    byte[] StagesWorkbook,
    byte[] SalaryWorkbook,
    bool ProductionWorkbookVerified,
    CompensationMode? ExplicitCompensationMode = null);

public sealed record PilotBootstrapIssueDto(string Severity, string Code, string Message);

public sealed class PilotMasterDataBootstrapPreviewDto
{
    public bool CanApply { get; init; }
    public bool ProductionWorkbookDeferred { get; init; } = true;
    public string FactoryAction { get; init; } = "blocked";
    public string ProductionLineAction { get; init; } = "blocked";
    public string ProductAction { get; init; } = "blocked";
    public int SourceStageRows { get; init; }
    public int SourceWorkerRows { get; init; }
    public int SourceDepartmentCount { get; init; }
    public int StagesCreated { get; init; }
    public int StagesUpdated { get; init; }
    public int StagesUnchanged { get; init; }
    public IReadOnlyCollection<string> GeneratedCodes { get; init; } = [];
    public int ProductStageMappingsCreated { get; init; }
    public int ProductStageMappingsUpdated { get; init; }
    public int ProductStageMappingsUnchanged { get; init; }
    /// <summary>Mappings using the controlled provisional SharedPercentage pilot default.</summary>
    public int ProvisionalCompensationMappingsForReview { get; init; }
    public int ExistingProductStageMappingsOutsideTarget { get; init; }
    public IReadOnlyCollection<string> ExistingProductCompensationModes { get; init; } = [];
    public int DepartmentsUpdated { get; init; }
    public int WorkersMatched { get; init; }
    public int WorkersUnmatched { get; init; }
    public int SalariesUpdated { get; init; }
    public int SalariesSetNull { get; init; }
    /// <summary>Development/Super Admin command output only. Contains no names or salary values.</summary>
    public IReadOnlyCollection<string> UnmatchedEmployeeCodes { get; init; } = [];
    public IReadOnlyCollection<PilotBootstrapIssueDto> Issues { get; init; } = [];
}

public sealed record PilotMasterDataBootstrapApplyResultDto(bool WasAlreadyCurrent, PilotMasterDataBootstrapPreviewDto Summary);

/// <summary>
/// Aggregate-only application database reset plan. Protected identity, security and ZKTime data
/// are intentionally not represented as deletions.
/// </summary>
public sealed class PilotMasterDataResetPreviewDto
{
    public int ProductionWorkerAllocations { get; init; }
    public int ProductionStageRecords { get; init; }
    public int ProductionOrders { get; init; }
    public int ProductionDayStageResolutions { get; init; }
    public int ImportBatches { get; init; }
    public int AssignmentTimelineEntries { get; init; }
    public int WorkerTemporaryAssignments { get; init; }
    public int WorkerDefaultAssignments { get; init; }
    public int StageReadinessSnapshots { get; init; }
    public int WorkerSalaryHistories { get; init; }
    public int ProductStageMappings { get; init; }
    public int ProductModels { get; init; }
    public int SubStages { get; init; }
    public int MainStages { get; init; }
    public int ProductionLines { get; init; }
    public int Departments { get; init; }
    public int Factories { get; init; }
    public int WorkersPreserved { get; init; }
    public int AttendanceRecordsPreserved { get; init; }
    public int UsersPreserved { get; init; }
    public int RolesPreserved { get; init; }
    public int PermissionsPreserved { get; init; }
    public int ActiveSuperAdminsPreserved { get; init; }

    public int TotalRecordsToDelete =>
        ProductionWorkerAllocations + ProductionStageRecords + ProductionOrders +
        ProductionDayStageResolutions + ImportBatches + AssignmentTimelineEntries +
        WorkerTemporaryAssignments + WorkerDefaultAssignments + StageReadinessSnapshots +
        WorkerSalaryHistories + ProductStageMappings + ProductModels + SubStages +
        MainStages + ProductionLines + Departments + Factories;
}

public sealed record PilotMasterDataResetApplyResultDto(bool WasAlreadyReset, PilotMasterDataResetPreviewDto Summary);

/// <summary>Aggregate-only post-apply verification for the one-time pilot master data.</summary>
public sealed class PilotMasterDataBootstrapVerificationDto
{
    public int ActiveSuperAdminCount { get; init; }
    public int TargetFactoryCount { get; init; }
    public int TargetProductionLineCount { get; init; }
    public int TargetProductCount { get; init; }
    public int TargetStageCount { get; init; }
    public int TargetProductStageMappingCount { get; init; }
    public bool StageCodesAreUniqueAndStable { get; init; }
    public bool StageIdentitiesAreUnique { get; init; }
    public int SourceRowsWithMissingSeconds { get; init; }
    public int MappingsWithMissingSeconds { get; init; }
    public bool SourcePricesMatch { get; init; }
    public int WorkersMatchedByEmployeeCode { get; init; }
    public int WorkersUnmatchedByEmployeeCode { get; init; }
    public int MatchedSalaryZeroRowsStoredAsNull { get; init; }
    public int ProductionOrders { get; init; }
    public bool SelectionChainAvailable { get; init; }
    public int ProvisionalCompensationMappingsForReview { get; init; }
    public IReadOnlyCollection<string> UnmatchedEmployeeCodes { get; init; } = [];
}
