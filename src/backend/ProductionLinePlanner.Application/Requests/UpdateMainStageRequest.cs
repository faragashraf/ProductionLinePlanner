namespace ProductionLinePlanner.Application.Requests;

public sealed class UpdateMainStageRequest
{
    public string? Name { get; init; }
    public bool? IsCritical { get; init; }
    public int? SequenceOrder { get; init; }
    public bool? IsActive { get; init; }
}
