using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed class StageReadinessDto
{
    public Guid ScopeEntityId { get; init; }
    public string ScopeType { get; init; } = string.Empty;
    public decimal ReadinessPercent { get; init; }
    public int RequiredWorkers { get; init; }
    public int PresentWorkers { get; init; }
    public int LateWorkers { get; init; }
    public int AbsentWorkers { get; init; }
    public int UnassignedWorkers { get; init; }
    public ReadinessStatus Status { get; init; }
    public DateTime CalculatedAtUtc { get; init; }
}
