using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Requests;

public sealed class SetWorkerEmploymentStatusRequest
{
    public string EmploymentStatus { get; init; } = "Active";
    public DateTime? EmploymentEndDate { get; init; }
    public string? Notes { get; init; }
}
