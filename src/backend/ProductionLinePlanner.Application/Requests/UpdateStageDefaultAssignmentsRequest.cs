namespace ProductionLinePlanner.Application.Requests;

/// <summary>
/// The manager's selected permanent-worker set for one stage. This scope never
/// changes a worker's participations in any other stage.
/// </summary>
public sealed class UpdateStageDefaultAssignmentsRequest
{
    public Guid ProductionLineId { get; init; }
    public IReadOnlyCollection<Guid>? WorkerIds { get; init; }
}
