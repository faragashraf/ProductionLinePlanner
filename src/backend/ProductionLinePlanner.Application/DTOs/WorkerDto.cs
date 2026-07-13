namespace ProductionLinePlanner.Application.DTOs;

public sealed class WorkerDto
{
    public Guid Id { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? AttendanceUserId { get; init; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? BadgeNumber { get; init; }
    public string? Phone { get; init; }
    public int? AttendanceDepartmentId { get; init; }
    public string EmploymentStatus { get; init; } = string.Empty;
    public DateTime? EmploymentEndDate { get; init; }
    public string? PhotoReference { get; init; }
    public bool IsActive { get; init; }
    public Guid? DefaultSubStageId { get; init; }
}
