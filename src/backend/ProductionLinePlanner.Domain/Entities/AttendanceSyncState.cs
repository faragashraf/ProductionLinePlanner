namespace ProductionLinePlanner.Domain.Entities;

/// <summary>
/// Durable evidence of attendance synchronization. A successful run with zero
/// punches is still evidence and cannot be inferred from AttendanceRecords.
/// </summary>
public sealed class AttendanceSyncState
{
    private AttendanceSyncState() { }

    public AttendanceSyncState(Guid id, string sourceName, DateOnly operationalDate)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(sourceName)) throw new ArgumentException("SourceName is required.", nameof(sourceName));
        if (operationalDate == default) throw new ArgumentException("OperationalDate is required.", nameof(operationalDate));

        Id = id;
        SourceName = sourceName.Trim();
        OperationalDate = operationalDate;
    }

    public Guid Id { get; init; }
    public string SourceName { get; private set; } = string.Empty;
    public DateOnly OperationalDate { get; init; }
    public DateTime LastAttemptAtUtc { get; private set; }
    public DateTime? LastSuccessfulAtUtc { get; private set; }
    public bool LastAttemptSucceeded { get; private set; }
    public string? LastErrorCode { get; private set; }

    public void RecordSuccess(DateTime atUtc)
    {
        LastAttemptAtUtc = atUtc;
        LastSuccessfulAtUtc = atUtc;
        LastAttemptSucceeded = true;
        LastErrorCode = null;
    }

    public void RecordFailure(DateTime atUtc, string? errorCode)
    {
        LastAttemptAtUtc = atUtc;
        LastAttemptSucceeded = false;
        LastErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "AttendanceSyncFailed" : errorCode.Trim();
    }
}
