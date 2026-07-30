using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

internal sealed class AttendanceWorkerIdentityResolver
{
    private readonly Dictionary<string, Guid[]> byAttendanceUserId;
    private readonly Dictionary<string, Guid[]> byBadge;

    public AttendanceWorkerIdentityResolver(IEnumerable<Worker> workers)
    {
        var materialized = workers.ToArray();
        byAttendanceUserId = BuildLookup(materialized, worker => worker.AttendanceUserId);
        byBadge = BuildLookup(materialized, worker => worker.BadgeNumber);
    }

    public AttendanceWorkerIdentityResolution Resolve(string? sourceUserId, string? badgeNumber, out Guid workerId)
    {
        workerId = Guid.Empty;
        var userKey = Normalize(sourceUserId);
        var badgeKey = Normalize(badgeNumber);
        var userMatches = userKey is not null && byAttendanceUserId.TryGetValue(userKey, out var userIds) ? userIds : [];
        var badgeMatches = badgeKey is not null && byBadge.TryGetValue(badgeKey, out var badgeIds) ? badgeIds : [];

        if (userMatches.Length > 1 || badgeMatches.Length > 1)
        {
            return AttendanceWorkerIdentityResolution.Ambiguous;
        }

        if (userMatches.Length == 1)
        {
            if (badgeMatches.Length == 1 && badgeMatches[0] != userMatches[0])
            {
                return AttendanceWorkerIdentityResolution.Ambiguous;
            }

            workerId = userMatches[0];
            return AttendanceWorkerIdentityResolution.Resolved;
        }

        if (badgeMatches.Length == 1)
        {
            workerId = badgeMatches[0];
            return AttendanceWorkerIdentityResolution.Resolved;
        }

        return AttendanceWorkerIdentityResolution.NotResolved;
    }

    private static Dictionary<string, Guid[]> BuildLookup(IEnumerable<Worker> workers, Func<Worker, string?> selector) =>
        workers
            .Select(worker => new { worker.Id, Key = Normalize(selector(worker)) })
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Id).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal enum AttendanceWorkerIdentityResolution
{
    NotResolved,
    Resolved,
    Ambiguous
}
