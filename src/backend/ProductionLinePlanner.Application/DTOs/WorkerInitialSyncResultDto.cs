namespace ProductionLinePlanner.Application.DTOs;

public sealed class WorkerInitialSyncResultDto
{
    public int SourceCount { get; init; }
    public int CreatedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int UnchangedCount { get; init; }
    public int MissingFromSourceCount { get; init; }
    public int MarkedInactiveCount { get; init; }
    public int ReactivatedCount { get; init; }
    public int WarningCount { get; init; }
    public int PhotosFoundCount { get; init; }
    public int PhotosSynchronizedCount { get; init; }
    public int PhotosCreatedCount { get; init; }
    public int PhotosUpdatedCount { get; init; }
    public int PhotosUnchangedCount { get; init; }
    public int InvalidOrUnsupportedPhotosCount { get; init; }
    public int WorkersWithoutPhotosCount { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }
}
