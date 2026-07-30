using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public sealed record OperationalWorkerState(
    Guid WorkerId,
    string AttendanceState,
    bool IsLate = false,
    bool HasCheckedOut = false);

/// <summary>
/// Pure readiness math. Persistence, assignment resolution, attendance windows,
/// and DTO hierarchy mapping deliberately stay outside this calculator.
/// </summary>
public static class OperationalReadinessCalculator
{
    public static OperationalReadinessMetricsDto Calculate(
        IEnumerable<Guid> assignedWorkerIds,
        IReadOnlyDictionary<Guid, OperationalWorkerState> attendanceByWorker,
        bool attendanceIsTrusted,
        int childCount)
    {
        var assigned = assignedWorkerIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (assigned.Length == 0)
        {
            return new OperationalReadinessMetricsDto(
                0, 0, 0, 0, 0, 0, null, null, childCount, "NoAssignments");
        }

        if (!attendanceIsTrusted)
        {
            return new OperationalReadinessMetricsDto(
                assigned.Length, 0, 0, 0, 0, assigned.Length, null, null, childCount, "Unknown");
        }

        var assignedStates = assigned.Select(workerId => attendanceByWorker.TryGetValue(workerId, out var state)
            ? state
            : new OperationalWorkerState(workerId, OperationalAttendanceStates.NotCheckedIn)).ToArray();
        var states = assignedStates.Select(state => state.AttendanceState).ToArray();
        var late = assignedStates.Count(state => state.IsLate || state.AttendanceState == OperationalAttendanceStates.Late);
        var present = assignedStates.Count(state => state.AttendanceState is OperationalAttendanceStates.Present or OperationalAttendanceStates.Late or OperationalAttendanceStates.CheckedOut);
        var checkedOut = assignedStates.Count(state => state.HasCheckedOut || state.AttendanceState == OperationalAttendanceStates.CheckedOut);
        var unknown = states.Count(state => state == OperationalAttendanceStates.Unknown);
        var absent = states.Count(state => state is OperationalAttendanceStates.Absent or OperationalAttendanceStates.NotCheckedIn);
        var percentage = decimal.Round(present * 100m / assigned.Length, 1, MidpointRounding.AwayFromZero);
        var shortage = assigned.Length - present;
        var status = percentage >= 90m ? "Ready" : percentage >= 70m ? "Warning" : "Critical";

        return new OperationalReadinessMetricsDto(
            assigned.Length,
            present,
            late,
            absent,
            checkedOut,
            unknown,
            percentage,
            shortage,
            childCount,
            status);
    }
}
