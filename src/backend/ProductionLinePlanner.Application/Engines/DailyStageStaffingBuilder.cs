using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public sealed record DailyStaffingCandidate(
    Guid WorkerId,
    string WorkerCode,
    string WorkerName,
    Guid SubStageId,
    string AssignmentType,
    Guid AssignmentId,
    IReadOnlyCollection<UtcTimeWindow> AssignmentWindows);

public sealed record DailyStaffingWorkerSnapshot(
    Guid WorkerId,
    string WorkerCode,
    string WorkerName,
    Guid SubStageId,
    string AssignmentType,
    Guid AssignmentId,
    AttendancePresenceWindowDto? Attendance,
    WorkerContributionResult Contribution);

/// <summary>
/// Builds the immutable daily staffing snapshot from organizational assignment
/// windows and actual attendance. Persistence and UI overrides stay outside
/// this pure capability.
/// </summary>
public static class DailyStageStaffingBuilder
{
    public static IReadOnlyCollection<DailyStaffingWorkerSnapshot> Build(
        IEnumerable<DailyStaffingCandidate> candidates,
        IReadOnlyDictionary<Guid, AttendancePresenceWindowDto> attendanceByWorker)
    {
        return candidates
            .GroupBy(candidate => new { candidate.WorkerId, candidate.SubStageId })
            .Select(group =>
            {
                var ordered = group.OrderBy(candidate => candidate.AssignmentType == "Default" ? 1 : 0)
                    .ThenBy(candidate => candidate.AssignmentId)
                    .ToArray();
                var identity = ordered[0];
                attendanceByWorker.TryGetValue(identity.WorkerId, out var attendance);
                var windows = ordered.SelectMany(candidate => candidate.AssignmentWindows).ToArray();
                return new DailyStaffingWorkerSnapshot(
                    identity.WorkerId,
                    identity.WorkerCode,
                    identity.WorkerName,
                    identity.SubStageId,
                    identity.AssignmentType,
                    identity.AssignmentId,
                    attendance,
                    TimeAwareProductionAllocation.CalculateContribution(identity.WorkerId, windows, attendance));
            })
            .OrderBy(snapshot => snapshot.WorkerCode, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.WorkerId)
            .ToArray();
    }
}
