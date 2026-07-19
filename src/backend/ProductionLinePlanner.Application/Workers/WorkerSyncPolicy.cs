using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Application.Workers;

public static class WorkerSyncActions
{
    public const string NewWorkerCandidate = "NewWorkerCandidate";
    public const string ExistingWorkerUnchanged = "ExistingWorkerUnchanged";
    public const string IdentityConflict = "IdentityConflict";
    public const string UnsupportedSourceState = "UnsupportedSourceState";
}

public sealed record WorkerSyncPolicyDecision(
    string Action,
    IReadOnlyCollection<string> ProtectedLocalFields,
    IReadOnlyCollection<string> IdentityConflicts,
    IReadOnlyCollection<string> SourceObservedFields);

public interface IWorkerSyncPolicy
{
    IReadOnlyCollection<string> InitializableFields { get; }
    IReadOnlyCollection<string> ProtectedLocalFields { get; }
    IReadOnlyCollection<string> SourceObservedOnlyFields { get; }

    WorkerSyncPolicyDecision EvaluateExistingWorker(Worker worker, AttendanceEmployeeRecord source);
    Result<Worker> CreateNewWorker(AttendanceEmployeeRecord source, DateTime createdAtUtc);
}

/// <summary>
/// Central ownership boundary for worker master synchronization. The attendance source may
/// initialize a new local identity, but it never mutates planner-owned fields on an existing worker.
/// </summary>
public sealed class WorkerSyncPolicy : IWorkerSyncPolicy
{
    private static readonly string[] Initializable =
    [
        nameof(Worker.EmployeeCode),
        nameof(Worker.FullName),
        nameof(Worker.AttendanceUserId),
        nameof(Worker.BadgeNumber)
    ];

    private static readonly string[] Protected =
    [
        "ArabicName",
        "LocalDisplayName",
        nameof(Worker.FullName),
        "LocalPhoto",
        nameof(Worker.PhotoReference),
        "Salary",
        "Assignments",
        "Factory",
        "ProductionLine",
        "Stages",
        "Production",
        "Reports",
        "HistoricalLocalData"
    ];

    private static readonly string[] SourceObservedOnly =
    [
        nameof(Worker.EmploymentStatus),
        "Department",
        "Shift"
    ];

    public IReadOnlyCollection<string> InitializableFields => Initializable;
    public IReadOnlyCollection<string> ProtectedLocalFields => Protected;
    public IReadOnlyCollection<string> SourceObservedOnlyFields => SourceObservedOnly;

    public WorkerSyncPolicyDecision EvaluateExistingWorker(Worker worker, AttendanceEmployeeRecord source)
    {
        ArgumentNullException.ThrowIfNull(worker);

        var conflicts = new List<string>();
        var sourceAttendanceUserId = Normalize(source.AttendanceUserId);
        var sourceBadge = Normalize(source.BadgeNumber);
        var sourceEmployeeCode = Normalize(source.EmployeeCode);

        if (sourceAttendanceUserId is not null &&
            Normalize(worker.AttendanceUserId) is { } localAttendanceUserId &&
            !string.Equals(localAttendanceUserId, sourceAttendanceUserId, StringComparison.OrdinalIgnoreCase))
        {
            conflicts.Add("AttendanceUserIdConflict");
        }

        if (sourceBadge is not null &&
            Normalize(worker.BadgeNumber) is { } localBadge &&
            !string.Equals(localBadge, sourceBadge, StringComparison.OrdinalIgnoreCase))
        {
            conflicts.Add("BadgeNumberConflict");
        }

        if (sourceEmployeeCode is not null &&
            !string.Equals(worker.EmployeeCode, sourceEmployeeCode, StringComparison.OrdinalIgnoreCase))
        {
            conflicts.Add("EmployeeCodeConflict");
        }

        return new WorkerSyncPolicyDecision(
            conflicts.Count == 0 ? WorkerSyncActions.ExistingWorkerUnchanged : WorkerSyncActions.IdentityConflict,
            Protected,
            conflicts,
            SourceObservedOnly);
    }

    public Result<Worker> CreateNewWorker(AttendanceEmployeeRecord source, DateTime createdAtUtc)
    {
        var attendanceUserId = Normalize(source.AttendanceUserId);
        var badgeNumber = Normalize(source.BadgeNumber);
        var employeeCode = Normalize(source.EmployeeCode) ?? badgeNumber ?? attendanceUserId;

        if (employeeCode is null)
        {
            return Result<Worker>.Failure(new Error(
                "UnsupportedSourceIdentity",
                "A new worker candidate requires a source employee code, badge number, or attendance user id."));
        }

        var sourceName = Normalize(source.Name);
        var initialLocalName = IsUsableInitialName(sourceName) ? sourceName! : employeeCode;

        return Result<Worker>.Success(new Worker(
            id: Guid.NewGuid(),
            employeeCode: employeeCode,
            fullName: initialLocalName,
            attendanceUserId: attendanceUserId,
            badgeNumber: badgeNumber,
            isActive: true,
            attendanceDepartmentId: null,
            lastExternalSyncAt: createdAtUtc,
            createdAtUtc: createdAtUtc));
    }

    private static bool IsUsableInitialName(string? value) =>
        value is not null && value.Any(character => !char.IsDigit(character));

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
