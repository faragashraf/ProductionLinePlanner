namespace ProductionLinePlanner.Application.DTOs;

public sealed class SubStageDto
{
    public Guid Id { get; init; }
    public Guid MainStageId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public int SequenceOrder { get; init; }
    public bool IsActive { get; init; }
}
