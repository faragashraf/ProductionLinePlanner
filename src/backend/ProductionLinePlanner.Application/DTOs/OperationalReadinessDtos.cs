namespace ProductionLinePlanner.Application.DTOs;

public static class OperationalReadinessNodeTypes
{
    public const string Factory = "Factory";
    public const string Department = "Department";
    public const string ProductionLine = "ProductionLine";
    public const string Stage = "Stage";
}

public static class OperationalAttendanceStates
{
    public const string Present = "Present";
    public const string Late = "Late";
    public const string Absent = "Absent";
    public const string NotCheckedIn = "NotCheckedIn";
    public const string CheckedOut = "CheckedOut";
    public const string Unknown = "Unknown";
}

public sealed record OperationalReadinessWorkdayPolicyDto(
    string DayStartTime,
    int GracePeriodMinutes,
    int FreshnessThresholdMinutes);

public sealed record AttendanceSyncFreshnessDto(
    string Status,
    bool IsTrusted,
    DateTime? LastAttemptAtUtc,
    DateTime? LastSuccessfulAtUtc,
    string? LastErrorCode,
    int? AgeMinutes);

public sealed record OperationalReadinessMetricsDto(
    int AssignedWorkerCount,
    int CurrentlyPresentCount,
    int LateCount,
    int AbsentCount,
    int CheckedOutCount,
    int UnknownCount,
    decimal? OperationalReadinessPercentage,
    int? ContributionToParentShortage,
    int ChildCount,
    string Status);

public sealed record OperationalReadinessFactoryDto(
    Guid Id,
    string Name,
    string Code,
    OperationalReadinessMetricsDto Metrics,
    IReadOnlyList<OperationalReadinessDepartmentDto> Departments);

public sealed record OperationalReadinessDepartmentDto(
    Guid Id,
    Guid FactoryId,
    string Name,
    string Code,
    OperationalReadinessMetricsDto Metrics,
    IReadOnlyList<OperationalReadinessLineDto> ProductionLines);

public sealed record OperationalReadinessLineDto(
    Guid Id,
    Guid FactoryId,
    Guid DepartmentId,
    string Name,
    string? Code,
    OperationalReadinessMetricsDto Metrics,
    IReadOnlyList<string> ModelNames);

public sealed record OperationalReadinessStageDto(
    Guid Id,
    Guid FactoryId,
    Guid DepartmentId,
    Guid ProductionLineId,
    Guid MainStageId,
    string Name,
    string Code,
    string MainStageName,
    OperationalReadinessMetricsDto Metrics,
    IReadOnlyList<string> ModelNames);

public sealed record OperationalReadinessWorkerDto(
    Guid WorkerId,
    Guid ProductionLineId,
    Guid StageId,
    string EmployeeCode,
    string FullName,
    string AttendanceState,
    string AttendanceLabel,
    bool IsOperationallyPresent,
    DateTime? CheckInAtUtc,
    DateTime? CheckOutAtUtc,
    int? LateByMinutes);

public sealed record OperationalReadinessSnapshotDto(
    DateOnly OperationalDate,
    DateTime CalculatedAtUtc,
    OperationalReadinessWorkdayPolicyDto WorkdayPolicy,
    AttendanceSyncFreshnessDto AttendanceSync,
    IReadOnlyList<OperationalReadinessFactoryDto> Factories);

public sealed record OperationalReadinessStagesDto(
    DateOnly OperationalDate,
    DateTime CalculatedAtUtc,
    AttendanceSyncFreshnessDto AttendanceSync,
    Guid FactoryId,
    string FactoryName,
    Guid DepartmentId,
    string DepartmentName,
    Guid ProductionLineId,
    string ProductionLineName,
    IReadOnlyList<OperationalReadinessStageDto> Stages);

public sealed record OperationalReadinessWorkersDto(
    DateOnly OperationalDate,
    DateTime CalculatedAtUtc,
    AttendanceSyncFreshnessDto AttendanceSync,
    Guid FactoryId,
    string FactoryName,
    Guid DepartmentId,
    string DepartmentName,
    Guid ProductionLineId,
    string ProductionLineName,
    Guid StageId,
    string StageName,
    IReadOnlyList<OperationalReadinessWorkerDto> Workers);

public sealed record OperationalReadinessNodePatchDto(
    Guid Id,
    Guid? ParentId,
    string NodeType,
    string Name,
    string? Code,
    OperationalReadinessMetricsDto Metrics,
    IReadOnlyList<string> ModelNames);

public sealed record OperationalReadinessWorkerPatchDto(
    Guid ProductionLineId,
    Guid StageId,
    Guid WorkerId,
    bool IsRemoved,
    OperationalReadinessWorkerDto? Worker);

public sealed record OperationalReadinessDeltaDto(
    Guid EventId,
    DateOnly OperationalDate,
    DateTime CalculatedAtUtc,
    AttendanceSyncFreshnessDto AttendanceSync,
    bool RequiresSnapshotReload,
    IReadOnlyList<OperationalReadinessNodePatchDto> Nodes,
    IReadOnlyList<OperationalReadinessWorkerPatchDto> Workers);
