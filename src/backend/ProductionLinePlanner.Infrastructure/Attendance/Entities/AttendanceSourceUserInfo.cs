namespace ProductionLinePlanner.Infrastructure.Attendance.Entities;

public sealed class AttendanceSourceUserInfo
{
    public string? UserId { get; init; }
    public string? BadgeNumber { get; init; }
    public string? Name { get; init; }
    public int? DepartmentId { get; init; }
}
