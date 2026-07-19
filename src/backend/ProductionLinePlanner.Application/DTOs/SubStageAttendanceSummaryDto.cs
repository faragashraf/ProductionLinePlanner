namespace ProductionLinePlanner.Application.DTOs;

/// <summary>
/// Read-only daily attendance evidence for the workers structurally assigned to one sub-stage.
/// This is intentionally separate from assignment coverage and contains no worker identities.
/// </summary>
public sealed record SubStageAttendanceSummaryDto(
    Guid SubStageId,
    int AssignedWorkersCount,
    int PresentAssignedWorkersCount,
    int LateAssignedWorkersCount,
    int AbsentAssignedWorkersCount,
    int UnresolvedAssignedWorkersCount,
    string AttendanceDataStatus,
    string AttendanceStatus)
{
    public int MainStageDistinctAssignedWorkersCount { get; init; }
    public int MainStageDistinctPresentWorkersCount { get; init; }
    public int MainStageDistinctAbsentWorkersCount { get; init; }
    public int ProductionLineDistinctAssignedWorkersCount { get; init; }
    public int ProductionLineDistinctPresentWorkersCount { get; init; }
    public int ProductionLineDistinctAbsentWorkersCount { get; init; }
    public int FactoryDistinctAssignedWorkersCount { get; init; }
    public int FactoryDistinctPresentWorkersCount { get; init; }
    public int FactoryDistinctAbsentWorkersCount { get; init; }
}
