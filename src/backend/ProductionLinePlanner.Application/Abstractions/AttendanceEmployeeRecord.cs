namespace ProductionLinePlanner.Application.Abstractions;

public sealed record AttendanceEmployeeRecord(
    string? AttendanceUserId,
    int? DepartmentId,
    string? BadgeNumber,
    string? Name,
    bool IsActive,
    string? EmployeeCode = null,
    string? EmploymentStatus = null,
    string? Department = null,
    string? Shift = null);
