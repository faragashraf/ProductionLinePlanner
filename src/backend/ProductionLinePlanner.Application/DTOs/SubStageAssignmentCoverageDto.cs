namespace ProductionLinePlanner.Application.DTOs;

/// <summary>
/// Read-only structural staffing coverage for one active sub-stage at a single point in time.
/// Attendance is deliberately excluded; operational readiness is owned by the readiness engine.
/// </summary>
public sealed record SubStageAssignmentCoverageDto(
    Guid SubStageId,
    int AssignedWorkersCount,
    int? RequiredWorkersCount,
    bool HasAuthoritativeRequiredWorkerCount,
    int? AssignmentCoveragePercent,
    string StaffingStatus)
{
    public int MainStageDistinctWorkersCount { get; init; }
    public int DepartmentDistinctWorkersCount { get; init; }
    public int FactoryDistinctWorkersCount { get; init; }
    public IReadOnlyCollection<ProductionLineStaffingCoverageDto> ProductionLines { get; init; } = [];
}

public sealed record ProductionLineStaffingCoverageDto(
    Guid ProductionLineId,
    int AssignedWorkersCount,
    int MainStageDistinctWorkersCount,
    int ProductionLineDistinctWorkersCount);
