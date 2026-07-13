namespace ProductionLinePlanner.Application.DTOs;

public sealed class WorkerInitialSyncResultDto
{
    public int SourceCount { get; init; }
    public int CreatedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int UnchangedCount { get; init; }
    public int MissingFromSourceCount { get; init; }
    public int WarningCount { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }
}
