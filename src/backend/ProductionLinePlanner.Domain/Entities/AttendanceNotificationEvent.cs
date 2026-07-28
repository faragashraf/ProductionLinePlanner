namespace ProductionLinePlanner.Domain.Entities;

public enum WorkerAttendanceNotificationType
{
    CheckIn,
    CheckOut
}

/// <summary>Durable application outbox entry created only after a source punch is accepted.</summary>
public sealed class AttendanceNotificationEvent
{
    private AttendanceNotificationEvent() { }

    public AttendanceNotificationEvent(
        Guid id,
        Guid attendanceRecordId,
        Guid workerId,
        string workerName,
        string employeeCode,
        WorkerAttendanceNotificationType attendanceType,
        DateTime attendanceTimeUtc,
        string source,
        string idempotencyKey,
        DateTime? createdAtUtc = null)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Id is required.", nameof(id)) : id;
        AttendanceRecordId = attendanceRecordId == Guid.Empty ? throw new ArgumentException("AttendanceRecordId is required.", nameof(attendanceRecordId)) : attendanceRecordId;
        WorkerId = workerId == Guid.Empty ? throw new ArgumentException("WorkerId is required.", nameof(workerId)) : workerId;
        WorkerName = Required(workerName, 200, nameof(workerName));
        EmployeeCode = Required(employeeCode, 120, nameof(employeeCode));
        AttendanceType = attendanceType;
        AttendanceTimeUtc = attendanceTimeUtc.Kind == DateTimeKind.Utc
            ? attendanceTimeUtc
            : throw new ArgumentException("AttendanceTimeUtc must be UTC.", nameof(attendanceTimeUtc));
        Source = Required(source, 60, nameof(source));
        IdempotencyKey = Required(idempotencyKey, 200, nameof(idempotencyKey));
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
    }

    public Guid Id { get; init; }
    public Guid AttendanceRecordId { get; private set; }
    public AttendanceRecord? AttendanceRecord { get; private set; }
    public Guid WorkerId { get; private set; }
    public string WorkerName { get; private set; } = string.Empty;
    public string EmployeeCode { get; private set; } = string.Empty;
    public WorkerAttendanceNotificationType AttendanceType { get; private set; }
    public DateTime AttendanceTimeUtc { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public DateTime? LastAttemptAtUtc { get; private set; }
    public string? LastErrorCode { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void MarkProcessed(DateTime atUtc)
    {
        AttemptCount++;
        LastAttemptAtUtc = atUtc;
        LastErrorCode = null;
        ProcessedAtUtc = atUtc;
    }

    public void MarkFailed(string errorCode, DateTime atUtc)
    {
        AttemptCount++;
        LastAttemptAtUtc = atUtc;
        LastErrorCode = Required(errorCode, 100, nameof(errorCode));
    }

    private static string Required(string? value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
            throw new ArgumentException($"{parameterName} must contain 1 to {maximumLength} characters.", parameterName);
        return normalized;
    }
}
