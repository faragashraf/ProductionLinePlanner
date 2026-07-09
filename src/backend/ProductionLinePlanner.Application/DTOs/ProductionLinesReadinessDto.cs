using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed class ProductionLinesReadinessDto
{
    public string ScopeType { get; init; } = "ProductionLines";
    public Guid ScopeEntityId { get; init; }
    public int RequiredWorkers { get; init; }
    public int AssignedWorkers { get; init; }
    public int PresentWorkers { get; init; }
    public int LateWorkers { get; init; }
    public int AbsentWorkers { get; init; }
    public int UnassignedWorkers { get; init; }
    public decimal ReadinessPercent { get; init; }
    public ReadinessStatus Status { get; init; }
    public DateTime CalculatedAtUtc { get; init; }
    public ProductionLineReadinessItemDto[] Items { get; init; } = [];
}

