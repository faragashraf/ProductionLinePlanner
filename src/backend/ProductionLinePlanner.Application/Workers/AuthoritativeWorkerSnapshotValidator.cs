using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Workers;

public sealed record WorkerSourceSnapshot(
    IReadOnlyCollection<AttendanceEmployeeRecord> Rows,
    bool IsComplete = false,
    bool AbsenceIsAuthoritative = false,
    bool EmploymentStatusIsAuthoritative = false,
    bool DepartmentIsAuthoritative = false,
    bool ShiftIsAuthoritative = false);

public sealed record WorkerSnapshotValidation(
    IReadOnlyCollection<string> Issues,
    IReadOnlySet<string> DuplicateAttendanceUserIds,
    IReadOnlySet<string> DuplicateBadgeNumbers,
    IReadOnlySet<string> DuplicateEmployeeCodes,
    IReadOnlySet<int> InvalidSourceRowIndexes)
{
    public bool HasStructuralIssues =>
        DuplicateAttendanceUserIds.Count > 0 ||
        DuplicateBadgeNumbers.Count > 0 ||
        DuplicateEmployeeCodes.Count > 0 ||
        InvalidSourceRowIndexes.Count > 0;
}

public interface IAuthoritativeWorkerSnapshotValidator
{
    WorkerSnapshotValidation Inspect(WorkerSourceSnapshot snapshot);
    Result ValidateAuthoritativeApplication(WorkerSourceSnapshot snapshot);
}

/// <summary>
/// Rejects authority claims that the current ZKTime projection cannot prove. Preview may inspect
/// an uncertain snapshot, but absence and employment metadata can never be applied from it.
/// </summary>
public sealed class AuthoritativeWorkerSnapshotValidator : IAuthoritativeWorkerSnapshotValidator
{
    public WorkerSnapshotValidation Inspect(WorkerSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Rows);

        var duplicateAttendanceIds = FindDuplicates(snapshot.Rows.Select(x => WorkerSyncPolicy.Normalize(x.AttendanceUserId)));
        var duplicateBadges = FindDuplicates(snapshot.Rows.Select(x => WorkerSyncPolicy.Normalize(x.BadgeNumber)));
        var duplicateEmployeeCodes = FindDuplicates(snapshot.Rows.Select(x => WorkerSyncPolicy.Normalize(x.EmployeeCode)));
        var invalidRows = new HashSet<int>();
        var issues = new List<string>();

        if (snapshot.Rows.Count == 0)
        {
            issues.Add("EmptySourceSnapshot");
        }

        var rowIndex = 0;
        foreach (var row in snapshot.Rows)
        {
            var attendanceUserId = WorkerSyncPolicy.Normalize(row.AttendanceUserId);
            var badgeNumber = WorkerSyncPolicy.Normalize(row.BadgeNumber);
            var employeeCode = WorkerSyncPolicy.Normalize(row.EmployeeCode);

            if (attendanceUserId is null || !int.TryParse(attendanceUserId, out var parsedUserId) || parsedUserId <= 0)
            {
                invalidRows.Add(rowIndex);
                issues.Add($"InvalidAttendanceUserId:{rowIndex}");
            }

            if (badgeNumber is null || badgeNumber.Length > 120)
            {
                invalidRows.Add(rowIndex);
                issues.Add($"InvalidBadgeNumber:{rowIndex}");
            }

            if (employeeCode is null || employeeCode.Length > 80)
            {
                invalidRows.Add(rowIndex);
                issues.Add($"InvalidEmployeeCode:{rowIndex}");
            }

            rowIndex++;
        }

        if (duplicateAttendanceIds.Count > 0) issues.Add("DuplicateAttendanceUserIds");
        if (duplicateBadges.Count > 0) issues.Add("DuplicateBadgeNumbers");
        if (duplicateEmployeeCodes.Count > 0) issues.Add("DuplicateEmployeeCodes");
        if (!snapshot.IsComplete) issues.Add("SnapshotCompletenessUnproven");
        if (!snapshot.AbsenceIsAuthoritative) issues.Add("AbsenceNotAuthoritative");
        if (!snapshot.EmploymentStatusIsAuthoritative) issues.Add("EmploymentStatusNotAuthoritative");
        if (!snapshot.DepartmentIsAuthoritative) issues.Add("DepartmentNotAuthoritative");
        if (!snapshot.ShiftIsAuthoritative) issues.Add("ShiftNotAuthoritative");

        return new WorkerSnapshotValidation(
            issues.Distinct(StringComparer.Ordinal).ToArray(),
            duplicateAttendanceIds,
            duplicateBadges,
            duplicateEmployeeCodes,
            invalidRows);
    }

    public Result ValidateAuthoritativeApplication(WorkerSourceSnapshot snapshot)
    {
        var validation = Inspect(snapshot);
        if (snapshot.Rows.Count == 0 ||
            validation.HasStructuralIssues ||
            !snapshot.IsComplete ||
            !snapshot.AbsenceIsAuthoritative ||
            !snapshot.EmploymentStatusIsAuthoritative ||
            !snapshot.DepartmentIsAuthoritative ||
            !snapshot.ShiftIsAuthoritative)
        {
            return Result.Failure(new Error(
                "UntrustedWorkerSnapshot",
                "The source snapshot is not authoritative for absence or worker master metadata."));
        }

        return Result.Success();
    }

    private static HashSet<string> FindDuplicates(IEnumerable<string?> values) =>
        values
            .Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
