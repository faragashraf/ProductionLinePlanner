namespace ProductionLinePlanner.Infrastructure.Attendance;

public sealed class AttendanceSourceOptions
{
    public const string SectionName = "AttendanceSource";

    public string ConnectionString { get; init; } = string.Empty;
    public string SourceName { get; init; } = "AttendanceSync";
    public TimeSpan DayStartTime { get; init; } = new TimeSpan(8, 0, 0);
    public int LateThresholdMinutes { get; init; } = 15;
    public string UserInfoTable { get; init; } = "USERINFO";
    public string CheckInOutTable { get; init; } = "CHECKINOUT";
    public string? DepartmentsTable { get; init; } = "DEPARTMENTS";
}
