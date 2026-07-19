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
    string AttendanceStatus);
