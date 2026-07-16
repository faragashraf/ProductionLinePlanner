namespace ProductionLinePlanner.Infrastructure.Attendance.Entities;

/// <summary>
/// Read-only ZKTime current-service import. Presence of EmployeeCode is the authoritative
/// on-service rule; the source table does not expose a separate status flag.
/// </summary>
public sealed class AttendanceSourceCurrentEmployee
{
    public string? EmployeeCode { get; init; }
}
