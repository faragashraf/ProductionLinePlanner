namespace ProductionLinePlanner.Application.Requests;

public sealed class CreateSubStageRequest
{
    public Guid MainStageId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public int SequenceOrder { get; init; }
    public bool IsActive { get; init; } = true;
}
