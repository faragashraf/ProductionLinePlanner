using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed class ProductionLineReadinessItemDto
{
    public string ScopeType { get; init; } = string.Empty;
    public Guid ScopeEntityId { get; init; }
    public string LineName { get; init; } = string.Empty;
    public int RequiredWorkers { get; init; }
    public int AssignedWorkers { get; init; }
    public int PresentWorkers { get; init; }
    public int LateWorkers { get; init; }
    public int AbsentWorkers { get; init; }
    public int UnassignedWorkers { get; init; }
    public decimal ReadinessPercent { get; init; }
    public decimal AssignmentCoveragePercent { get; init; }
    public string AttendanceDataStatus { get; init; } = "Unknown";
    public ReadinessStatus Status { get; init; }
}
