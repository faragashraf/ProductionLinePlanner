using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed record AttendanceWorkforceQuery(
    DateOnly ProductionDate,
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? FactoryId = null,
    Guid? ProductionLineId = null,
    Guid? MainStageId = null,
    Guid? SubStageId = null,
    string? Department = null,
    string? AttendanceFilter = null,
    string? AssignmentFilter = null,
    string? OperationalFilter = null,
    string? SortBy = null,
    string? SortDirection = null);

public sealed record AttendanceWorkforceAssignmentDto(
    Guid AssignmentId,
    AssignmentType AssignmentType,
    Guid SubStageId,
    Guid MainStageId,
    Guid ProductionLineId,
    Guid FactoryId,
    string FactoryName,
    string ProductionLineName,
    string MainStageName,
    string SubStageName,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    string? Reason);

public sealed record AttendanceWorkforceRowDto(
    Guid WorkerId,
    string EmployeeCode,
    string FullName,
    string? DepartmentName,
    string? PhotoReference,
    bool HasPhoto,
    string AttendanceStatus,
    DateTime? FirstCheckInUtc,
    DateTime? LastCheckOutUtc,
    bool HasAttendanceData,
    bool HasSinglePunch,
    IReadOnlyCollection<AttendanceWorkforceAssignmentDto> Assignments,
    bool IsAssigned,
    bool HasTemporaryAssignment,
    bool NeedsReview);

public sealed record AttendanceWorkforceSummaryDto(
    int TotalWorkers,
    int PresentWorkers,
    int AbsentWorkers,
    int LateWorkers,
    int IncompleteWorkers,
    int UnassignedPresentWorkers,
    int AssignedAbsentWorkers,
    int ReviewRequiredWorkers,
    bool AttendanceDataAvailable,
    string Scope);

public sealed record AttendanceWorkforcePageDto(
    DateOnly ProductionDate,
    IReadOnlyCollection<AttendanceWorkforceRowDto> Items,
    AttendanceWorkforceSummaryDto Summary,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AttendanceWorkforceDetailDto(
    Guid WorkerId,
    DateOnly ProductionDate,
    IReadOnlyCollection<AttendanceWorkforcePunchDto> AttendanceRecords,
    IReadOnlyCollection<AttendanceWorkforceAssignmentDto> Assignments);

/// <summary>
/// A user-facing attendance evidence point. OccurredAtUtc is always an explicitly UTC value;
/// presentation is responsible for applying the configured Cairo time zone.
/// </summary>
public sealed record AttendanceWorkforcePunchDto(DateTime OccurredAtUtc, string Label);
