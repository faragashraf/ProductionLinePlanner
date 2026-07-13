using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Requests;

public sealed class UpsertProductModelStageRequest
{
    public Guid? SubStageId { get; init; }
    public int? StageOrder { get; init; }
    public decimal? PiecePrice { get; init; }
    public decimal? StandardSeconds { get; init; }
    public CompensationMode? CompensationMode { get; init; }
    public bool? IsRequired { get; init; }
    public bool? IsActive { get; init; }
    public DateTime? EffectiveFrom { get; init; }
}
