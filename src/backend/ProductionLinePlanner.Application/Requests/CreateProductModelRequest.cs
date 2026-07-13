using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Requests;

public sealed class CreateProductModelRequest
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}
