namespace ProductionLinePlanner.Application.DTOs;

public sealed class MainStageDto
{
    public Guid Id { get; init; }
    public Guid ProductionLineId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SequenceOrder { get; init; }
    public bool IsCritical { get; init; }
    public bool IsActive { get; init; }
}
