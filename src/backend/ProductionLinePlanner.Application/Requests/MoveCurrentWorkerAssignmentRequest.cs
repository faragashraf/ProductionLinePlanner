namespace ProductionLinePlanner.Application.Requests;

/// <summary>
/// Moves the assignment effective at a single operational instant. The source
/// assignment identifier is a concurrency guard, not a historical reference.
/// </summary>
public sealed class MoveCurrentWorkerAssignmentRequest
{
    public Guid WorkerId { get; init; }
    public Guid SourceAssignmentId { get; init; }
    public Guid FromSubStageId { get; init; }
    public Guid ToSubStageId { get; init; }
    public DateTime EffectiveAtUtc { get; init; }
    public DateTime? TemporaryEndAtUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
}
