namespace ProductionLinePlanner.Application.DTOs;

public sealed record ProcessedAttendanceOrphanQuery(
    DateOnly FromOperationalDate,
    DateOnly ToOperationalDate,
    int? SourceUserId = null,
    string? BadgeNumber = null,
    int MaximumRows = 100);

public sealed record ProcessedAttendanceOrphanItemDto(
    long InboxId,
    int SourceUserId,
    string? BadgeNumber,
    DateTime SourceCheckTimeLocal,
    string SourceCheckType,
    int AttemptCount,
    DateTime ExpectedAttendanceTimeUtc,
    DateOnly OperationalDate,
    Guid WorkerId,
    string WorkerName,
    string ReasonCode);

public sealed record ProcessedAttendanceOrphanGroupDto(
    DateOnly OperationalDate,
    Guid WorkerId,
    string WorkerName,
    string? BadgeNumber,
    int Count);

public sealed record ProcessedAttendanceOrphanPreviewDto(
    ProcessedAttendanceOrphanQuery Query,
    int Count,
    bool ScanLimitReached,
    IReadOnlyList<ProcessedAttendanceOrphanGroupDto> Groups,
    IReadOnlyList<ProcessedAttendanceOrphanItemDto> Items);

public sealed record ProcessedAttendanceOrphanRepairRequest(
    DateOnly FromOperationalDate,
    DateOnly ToOperationalDate,
    int? SourceUserId = null,
    string? BadgeNumber = null,
    int MaximumRows = 100,
    bool Execute = false,
    string? Confirmation = null,
    IReadOnlyList<long>? InboxIds = null);

public sealed record ProcessedAttendanceOrphanRepairItemDto(
    long InboxId,
    string Result,
    string? ResolutionCode,
    string? Details);

public sealed record ProcessedAttendanceOrphanRepairDto(
    bool Executed,
    ProcessedAttendanceOrphanPreviewDto Preview,
    IReadOnlyList<ProcessedAttendanceOrphanRepairItemDto> Results);
