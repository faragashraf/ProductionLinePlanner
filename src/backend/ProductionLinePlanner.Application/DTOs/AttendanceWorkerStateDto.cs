using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed class AttendanceWorkerStateDto
{
    public Guid WorkerId { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public AttendanceStatus AttendanceStatus { get; init; }
    public DateTime AttendanceTimeUtc { get; init; }
    public string? Source { get; init; }
    public string? AttendanceUserId { get; init; }
    public string? BadgeNumber { get; init; }
}
