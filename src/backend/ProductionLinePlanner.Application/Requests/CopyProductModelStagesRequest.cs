using System.ComponentModel.DataAnnotations;

namespace ProductionLinePlanner.Application.Requests;

public sealed class CopyProductModelStagesRequest
{
    public Guid TargetModelId { get; init; }

    [StringLength(4000)]
    public string? Note { get; init; }
}
