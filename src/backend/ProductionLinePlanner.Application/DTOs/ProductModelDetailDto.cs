namespace ProductionLinePlanner.Application.DTOs;

public sealed class ProductModelDetailDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public ProductModelStageDto[] Stages { get; init; } = [];
}
