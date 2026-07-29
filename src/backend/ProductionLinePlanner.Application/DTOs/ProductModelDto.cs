namespace ProductionLinePlanner.Application.DTOs;

public sealed class ProductModelDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public sealed record ProductModelDeleteEligibilityDto(Guid ModelId, bool CanDelete, string MessageAr);
