using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class StageReadinessSnapshot
{
    private StageReadinessSnapshot() { }

    public StageReadinessSnapshot(
        Guid id,
        string scopeType,
        Guid scopeEntityId,
        int requiredWorkers,
        int presentWorkers,
        int lateWorkers,
        int absentWorkers,
        int unassignedWorkers,
        DateTime calculatedAtUtc,
        DateTime? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(scopeType))
            throw new ArgumentException("ScopeType is required.", nameof(scopeType));
        if (scopeEntityId == Guid.Empty)
            throw new ArgumentException("ScopeEntityId is required.", nameof(scopeEntityId));
        if (requiredWorkers < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredWorkers), "RequiredWorkers must be zero or positive.");
        if (presentWorkers < 0 || lateWorkers < 0 || absentWorkers < 0 || unassignedWorkers < 0)
            throw new ArgumentOutOfRangeException("Presence and absence counters must be zero or positive.");
        if (calculatedAtUtc == default)
            throw new ArgumentException("CalculatedAtUtc is required.", nameof(calculatedAtUtc));

        Id = id;
        ScopeType = scopeType.Trim();
        ScopeEntityId = scopeEntityId;
        RequiredWorkers = requiredWorkers;
        PresentWorkers = presentWorkers;
        LateWorkers = lateWorkers;
        AbsentWorkers = absentWorkers;
        UnassignedWorkers = unassignedWorkers;
        CalculatedAtUtc = calculatedAtUtc;
        ReadinessPercent = CalculateReadinessPercent(requiredWorkers, presentWorkers, lateWorkers, absentWorkers, unassignedWorkers);
        ReadinessStatus = ReadinessFromPercent(ReadinessPercent);
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
    }

    public Guid Id { get; init; }
    public string ScopeType { get; private set; }
    public Guid ScopeEntityId { get; private set; }
    public DateTime CalculatedAtUtc { get; private set; }
    public int RequiredWorkers { get; private set; }
    public int PresentWorkers { get; private set; }
    public int LateWorkers { get; private set; }
    public int AbsentWorkers { get; private set; }
    public int UnassignedWorkers { get; private set; }
    public decimal ReadinessPercent { get; private set; }
    public ReadinessStatus ReadinessStatus { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static decimal CalculateReadinessPercent(
        int requiredWorkers,
        int presentWorkers,
        int lateWorkers,
        int absentWorkers,
        int unassignedWorkers)
    {
        if (requiredWorkers <= 0)
            return 100m;

        var readyCount = presentWorkers; // Late is intentionally not counted as fully ready yet.
        var percent = (decimal)readyCount / requiredWorkers * 100m;
        return Math.Clamp(percent, 0m, 100m);
    }

    public static ReadinessStatus ReadinessFromPercent(decimal percent) =>
        percent switch
        {
            >= 100m => ReadinessStatus.Normal,
            >= 70m => ReadinessStatus.Warning,
            > 0m => ReadinessStatus.Critical,
            _ => ReadinessStatus.Unknown
        };
}
