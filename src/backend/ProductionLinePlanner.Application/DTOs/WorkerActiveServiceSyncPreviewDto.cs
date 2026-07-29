namespace ProductionLinePlanner.Application.DTOs;

/// <summary>Read-only worker master synchronization preview. No source snapshot is authoritative in this phase.</summary>
public sealed class WorkerActiveServiceSyncPreviewDto
{
    public bool IsReadOnly { get; init; } = true;
    public bool CanApply { get; init; }
    public int CurrentLocalWorkers { get; init; }
    public int ActiveOnServiceWorkersInZkTime { get; init; }
    public int WorkersToRemainActive { get; init; }
    public int WorkersToReactivate { get; init; }
    public int WorkersToCreate { get; init; }
    public int WorkersToMarkInactiveOrExcluded { get; init; }
    public int WorkersAlreadyInactiveOrExcluded { get; init; }
    /// <summary>No physical deletion is performed by this safety-first correction.</summary>
    public int WorkersSafelyRemovable { get; init; }
    public int WarningCount { get; init; }
    public int IdentityConflictCount { get; init; }
    public int UnsupportedSourceStateCount { get; init; }
    public IReadOnlyCollection<string> SnapshotIssues { get; init; } = [];
    public IReadOnlyCollection<WorkerMasterSyncPreviewRowDto> Rows { get; init; } = [];
}

public sealed record WorkerMasterSyncPreviewRowDto(
    string Action,
    Guid? WorkerId,
    string? LocalEmployeeCode,
    string? LocalDisplayName,
    string? SourceAttendanceUserId,
    string? SourceBadgeNumber,
    string? SourceEmployeeCode,
    string? SourceName,
    string? SourceObservedEmploymentStatus,
    int? SourceObservedDepartmentId,
    string? SourceObservedDepartment,
    string? SourceObservedShift,
    IReadOnlyCollection<string> ProtectedLocalFields,
    IReadOnlyCollection<string> IdentityConflicts,
    string? Reason);
