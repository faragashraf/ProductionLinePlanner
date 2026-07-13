namespace ProductionLinePlanner.Application.Requests;

public sealed class UpdateWorkerRequest
{
    public string? FullName { get; init; }
    public int? AttendanceDepartmentId { get; init; }
    public string? Phone { get; init; }
}
