namespace ProductionLinePlanner.Application.Requests;

public sealed class CreateWorkerRequest
{
    public string EmployeeCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Phone { get; init; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? AttendanceUserId { get; init; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? BadgeNumber { get; init; }
    public bool IsActive { get; init; } = true;
}
