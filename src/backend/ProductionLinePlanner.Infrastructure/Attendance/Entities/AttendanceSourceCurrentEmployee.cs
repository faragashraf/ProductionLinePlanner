namespace ProductionLinePlanner.Infrastructure.Attendance.Entities;

/// <summary>
/// Read-only ZKTime current-service import. Presence is source-observed metadata only;
/// the table does not prove employment status or make absence authoritative.
/// </summary>
public sealed class AttendanceSourceCurrentEmployee
{
    public string? EmployeeCode { get; init; }
}
