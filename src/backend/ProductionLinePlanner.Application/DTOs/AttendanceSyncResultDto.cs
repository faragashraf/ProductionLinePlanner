namespace ProductionLinePlanner.Application.DTOs;

public sealed class AttendanceSyncResultDto
{
    public DateTime SyncDateUtc { get; init; }
    public int EvaluatedWorkers { get; init; }
    public int SyncedWorkers { get; init; }
    public int InsertedRecords { get; init; }
    public int UpdatedRecords { get; init; }
    public int UnchangedRecords { get; init; }
    public int UnmatchedSourceRows { get; init; }
    public int WorkersWithMissingMapping { get; init; }
}
