namespace ProductionLinePlanner.Application.DTOs;

public sealed class AttendanceSyncResultDto
{
    public string? CorrelationId { get; init; }
    public string? TriggerType { get; init; }
    public DateTime SyncDateUtc { get; init; }
    public int SourceUsersCount { get; init; }
    public int SourceCheckInsCount { get; init; }
    public int MatchedWorkersCount { get; init; }
    public int UnmatchedSourceUsersCount { get; init; }
    public int WorkersWithoutAttendanceCount { get; init; }
    public int InsertedRecords { get; init; }
    public int UpdatedRecords { get; init; }
    public int SkippedRecords { get; init; }
}
