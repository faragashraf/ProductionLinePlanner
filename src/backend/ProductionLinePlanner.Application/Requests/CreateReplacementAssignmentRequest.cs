namespace ProductionLinePlanner.Application.Requests;

public sealed class CreateReplacementAssignmentRequest
{
    public Guid ReplacementWorkerId { get; init; }
    public Guid ReplacedWorkerId { get; init; }
    public Guid SubStageId { get; init; }
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
}
