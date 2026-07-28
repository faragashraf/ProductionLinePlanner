namespace ProductionLinePlanner.Application.DTOs;

public sealed class ProductModelStageDto
{
    public Guid Id { get; init; }
    public Guid ProductModelId { get; init; }
    public Guid ProductionLineId { get; init; }
    public Guid SubStageId { get; init; }
    public Guid DepartmentId { get; init; }
    public string SubStageCode { get; init; } = string.Empty;
    public string SubStageName { get; init; } = string.Empty;
    public int StageOrder { get; init; }
    public decimal PiecePrice { get; init; }
    public decimal? StandardSeconds { get; init; }
    public string CompensationMode { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public bool IsActive { get; init; }
    public DateTime? EffectiveFrom { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
