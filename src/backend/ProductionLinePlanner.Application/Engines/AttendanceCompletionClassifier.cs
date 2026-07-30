using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Engines;

public static class AttendanceDayStates
{
    public const string Present = "Present";
    public const string Late = "Late";
    public const string Absent = "Absent";
    public const string Unknown = "Unknown";
}

public sealed record AttendanceCompletionState(
    string State,
    bool CountsAsAttended,
    bool IsLate,
    bool HasCheckedOut,
    DateTime? FirstInUtc,
    DateTime? LastOutUtc);

/// <summary>
/// One attendance-day classification shared by workforce and operational-readiness views.
/// A later checkout is evidence that the worker completed attendance for the day; it never
/// erases the original check-in.
/// </summary>
public static class AttendanceCompletionClassifier
{
    public static AttendanceCompletionState Resolve(
        AttendancePresenceWindowDto? evidence,
        bool attendanceDataIsTrusted)
    {
        if (!attendanceDataIsTrusted)
            return new AttendanceCompletionState(AttendanceDayStates.Unknown, false, false, false, null, null);

        if (evidence is null || !evidence.HasSourceCheckIn ||
            evidence.Status is AttendanceStatus.Absent or AttendanceStatus.Unassigned)
        {
            return new AttendanceCompletionState(AttendanceDayStates.Absent, false, false, false, null, null);
        }

        var isLate = evidence.Status == AttendanceStatus.Late;
        return new AttendanceCompletionState(
            isLate ? AttendanceDayStates.Late : AttendanceDayStates.Present,
            true,
            isLate,
            evidence.LastOutUtc.HasValue,
            EnsureUtc(evidence.FirstInUtc),
            EnsureUtc(evidence.LastOutUtc));
    }

    private static DateTime? EnsureUtc(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        : null;
}
