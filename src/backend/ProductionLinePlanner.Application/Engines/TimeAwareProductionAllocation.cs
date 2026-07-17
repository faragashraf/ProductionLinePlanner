using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Engines;

public sealed record UtcTimeWindow(DateTime StartUtc, DateTime EndUtc);

public sealed record WorkerContributionResult(
    Guid WorkerId,
    DateTime? ContributionStartsAtUtc,
    DateTime? ContributionEndsAtUtc,
    int WorkerMinutes,
    string? ExclusionReason)
{
    public bool IsProductionReady => WorkerMinutes > 0 && ExclusionReason is null;
}

public sealed record WorkerQuantityShare(
    Guid WorkerId,
    int WorkerMinutes,
    decimal Percentage,
    decimal Quantity);

/// <summary>
/// Shared, deterministic capability for assignment/attendance intersections and
/// minute-weighted stage allocation. It has no persistence or UI dependency.
/// </summary>
public static class TimeAwareProductionAllocation
{
    private const decimal PercentageStep = 0.0001m;
    private const decimal QuantityStep = 0.001m;

    public static WorkerContributionResult CalculateContribution(
        Guid workerId,
        IEnumerable<UtcTimeWindow> assignmentWindows,
        AttendancePresenceWindowDto? attendance)
    {
        var windows = Merge(assignmentWindows);
        if (windows.Count == 0)
            return Excluded(workerId, "OutsideAssignmentWindow");
        if (attendance is null || !attendance.HasSourceCheckIn)
            return Excluded(workerId, attendance?.Status.ToString() == "Absent" ? "Absent" : "NotProductionReady");
        if (!attendance.FirstInUtc.HasValue || !attendance.LastOutUtc.HasValue || attendance.FirstInUtc >= attendance.LastOutUtc)
            return Excluded(workerId, "IncompleteAttendance");

        var intersections = windows
            .Select(window => Intersect(window, new UtcTimeWindow(attendance.FirstInUtc.Value, attendance.LastOutUtc.Value)))
            .Where(window => window is not null)
            .Select(window => window!)
            .ToArray();
        if (intersections.Length == 0)
            return Excluded(workerId, "NoTemporalIntersection");

        var merged = Merge(intersections);
        var minutes = merged.Sum(window => (int)Math.Floor((window.EndUtc - window.StartUtc).TotalMinutes));
        return minutes <= 0
            ? Excluded(workerId, "NoTemporalIntersection")
            : new WorkerContributionResult(workerId, merged.Min(window => window.StartUtc), merged.Max(window => window.EndUtc), minutes, null);
    }

    public static IReadOnlyCollection<WorkerQuantityShare> AllocateByMinutes(
        decimal stageQuantity,
        IEnumerable<WorkerContributionResult> contributions)
    {
        var eligible = contributions
            .Where(contribution => contribution.IsProductionReady)
            .OrderBy(contribution => contribution.WorkerId)
            .ToArray();
        var totalMinutes = eligible.Sum(contribution => contribution.WorkerMinutes);
        if (eligible.Length == 0 || totalMinutes <= 0)
            return [];

        var percentages = AllocateUnits(100m, PercentageStep, eligible, totalMinutes);
        var quantities = AllocateUnits(decimal.Round(stageQuantity, 3, MidpointRounding.AwayFromZero), QuantityStep, eligible, totalMinutes);
        return eligible.Select(contribution => new WorkerQuantityShare(
            contribution.WorkerId,
            contribution.WorkerMinutes,
            percentages[contribution.WorkerId],
            quantities[contribution.WorkerId])).ToArray();
    }

    private static Dictionary<Guid, decimal> AllocateUnits(
        decimal total,
        decimal step,
        IReadOnlyCollection<WorkerContributionResult> contributions,
        int totalMinutes)
    {
        var targetUnits = decimal.ToInt32(total / step);
        var rows = contributions.Select(contribution =>
        {
            var rawUnits = total * contribution.WorkerMinutes / totalMinutes / step;
            var floorUnits = decimal.ToInt32(decimal.Floor(rawUnits));
            return new { contribution.WorkerId, FloorUnits = floorUnits, Remainder = rawUnits - floorUnits };
        }).ToArray();
        var units = rows.ToDictionary(row => row.WorkerId, row => row.FloorUnits);
        var remaining = targetUnits - units.Values.Sum();
        foreach (var row in rows.OrderByDescending(row => row.Remainder).ThenBy(row => row.WorkerId).Take(remaining))
            units[row.WorkerId]++;
        return units.ToDictionary(pair => pair.Key, pair => pair.Value * step);
    }

    private static UtcTimeWindow? Intersect(UtcTimeWindow left, UtcTimeWindow right)
    {
        var start = left.StartUtc > right.StartUtc ? left.StartUtc : right.StartUtc;
        var end = left.EndUtc < right.EndUtc ? left.EndUtc : right.EndUtc;
        return start < end ? new UtcTimeWindow(start, end) : null;
    }

    private static IReadOnlyList<UtcTimeWindow> Merge(IEnumerable<UtcTimeWindow> windows)
    {
        var ordered = windows.Where(window => window.StartUtc < window.EndUtc).OrderBy(window => window.StartUtc).ThenBy(window => window.EndUtc).ToArray();
        if (ordered.Length == 0) return [];
        var merged = new List<UtcTimeWindow> { ordered[0] };
        foreach (var window in ordered.Skip(1))
        {
            var last = merged[^1];
            if (window.StartUtc <= last.EndUtc)
                merged[^1] = new UtcTimeWindow(last.StartUtc, window.EndUtc > last.EndUtc ? window.EndUtc : last.EndUtc);
            else
                merged.Add(window);
        }
        return merged;
    }

    private static WorkerContributionResult Excluded(Guid workerId, string reason) => new(workerId, null, null, 0, reason);
}
