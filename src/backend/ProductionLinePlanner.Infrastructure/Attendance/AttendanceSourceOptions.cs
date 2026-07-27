namespace ProductionLinePlanner.Infrastructure.Attendance;

public sealed class AttendanceSourceOptions
{
    public const string SectionName = "AttendanceSource";
    public const string DirectMode = "Direct";
    public const string StagingMode = "Staging";

    public string ConnectionString { get; init; } = string.Empty;
    public string Mode { get; init; } = DirectMode;
    public string SourceName { get; init; } = "AttendanceSync";
    public TimeSpan DayStartTime { get; init; } = new TimeSpan(8, 0, 0);
    public int LateThresholdMinutes { get; init; } = 15;
    public string UserInfoTable { get; init; } = "USERINFO";
    public string CheckInOutTable { get; init; } = "CHECKINOUT";
    public string? DepartmentsTable { get; init; } = "DEPARTMENTS";
    public int SyncReadCommandTimeoutSeconds { get; init; } = 30;
    public int SyncReadTimeoutSeconds { get; init; } = 35;
    public int StagingBatchSize { get; init; } = 2000;
    public int ProcessingLeaseMinutes { get; init; } = 15;
    public int MaxProcessingAttempts { get; init; } = 5;
    public bool StagingProcessorEnabled { get; init; } = true;
    public int StagingProcessorIntervalSeconds { get; init; } = 60;
    public int MaxPendingProductionDates { get; init; } = 3;

    public bool UsesStaging => string.Equals(Mode, StagingMode, StringComparison.OrdinalIgnoreCase);
}
