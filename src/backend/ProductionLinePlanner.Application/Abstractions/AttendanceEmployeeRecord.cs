namespace ProductionLinePlanner.Application.Abstractions;

public sealed record AttendanceEmployeeRecord(
    string? AttendanceUserId,
    int? DepartmentId,
    string? BadgeNumber,
    string? Name,
    bool IsActive);
