namespace ProductionLinePlanner.Application.DTOs;

public sealed class ProductionLineDto
{
    public Guid Id { get; init; }
    public Guid FactoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? LineCode { get; init; }
    public int SequenceOrder { get; init; }
    public bool IsActive { get; init; }
}
