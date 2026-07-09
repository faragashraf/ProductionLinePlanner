namespace ProductionLinePlanner.Application.Requests;

public sealed class UpdateWorkerRequest
{
    public string? FullName { get; init; }
    public string? AttendanceUserId { get; init; }
    public string? BadgeNumber { get; init; }
    public string? Phone { get; init; }
    public bool? IsActive { get; init; }
}
