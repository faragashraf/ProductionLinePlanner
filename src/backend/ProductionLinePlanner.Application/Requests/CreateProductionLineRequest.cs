namespace ProductionLinePlanner.Application.Requests;

public sealed class CreateProductionLineRequest
{
    public Guid FactoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? LineCode { get; init; }
    public int SequenceOrder { get; init; }
    public bool IsActive { get; init; } = true;
}
