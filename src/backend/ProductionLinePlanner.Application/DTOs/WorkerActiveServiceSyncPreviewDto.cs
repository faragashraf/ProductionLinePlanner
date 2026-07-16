namespace ProductionLinePlanner.Application.DTOs;

/// <summary>Aggregate-only preview for the authoritative current-service worker projection sync.</summary>
public sealed class WorkerActiveServiceSyncPreviewDto
{
    public int CurrentLocalWorkers { get; init; }
    public int ActiveOnServiceWorkersInZkTime { get; init; }
    public int WorkersToRemainActive { get; init; }
    public int WorkersToReactivate { get; init; }
    public int WorkersToCreate { get; init; }
    public int WorkersToMarkInactiveOrExcluded { get; init; }
    public int WorkersAlreadyInactiveOrExcluded { get; init; }
    /// <summary>No physical deletion is performed by this safety-first correction.</summary>
    public int WorkersSafelyRemovable { get; init; }
    public int WarningCount { get; init; }
}
