namespace ProductionLinePlanner.Application.Requests;

public sealed class CreateDefaultAssignmentRequest
{
    public Guid WorkerId { get; init; }
    public Guid SubStageId { get; init; }
    public string? Reason { get; init; }
}
