using System.ComponentModel.DataAnnotations;

namespace ProductionLinePlanner.Application.Requests;

public sealed record CopyProductModelStagesRequest
{
    public Guid SourceFactoryId { get; init; }

    public Guid SourceDepartmentId { get; init; }

    public Guid SourceProductionLineId { get; init; }

    public Guid TargetModelId { get; init; }

    public Guid TargetFactoryId { get; init; }

    public Guid TargetDepartmentId { get; init; }

    public Guid TargetProductionLineId { get; init; }

    [MinLength(1)]
    [MaxLength(200)]
    public Guid[] SourceProductModelStageIds { get; init; } = [];

    public bool PreviewOnly { get; init; }

    [StringLength(4000)]
    public string? Note { get; init; }
}
