namespace ProductionLinePlanner.Application.Requests;

public sealed class UpdateSubStageRequest
{
    public string? Name { get; init; }
    public int? Capacity { get; init; }
    public int? SequenceOrder { get; init; }
    public bool? IsActive { get; init; }
}
