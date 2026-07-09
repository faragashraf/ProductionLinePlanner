namespace ProductionLinePlanner.Application.DTOs;

public sealed class FactoryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Location { get; init; }
    public bool IsActive { get; init; }
}
