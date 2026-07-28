using System.ComponentModel.DataAnnotations;

namespace ProductionLinePlanner.Application.Requests;

public sealed record CopyProductModelStagesRequest
{
    public Guid SourceProductionLineId { get; init; }

    public Guid TargetModelId { get; init; }

    public Guid TargetProductionLineId { get; init; }

    [MinLength(1)]
    [MaxLength(200)]
    public Guid[] SourceProductModelStageIds { get; init; } = [];

    public bool PreviewOnly { get; init; }

    [StringLength(4000)]
    public string? Note { get; init; }
}
