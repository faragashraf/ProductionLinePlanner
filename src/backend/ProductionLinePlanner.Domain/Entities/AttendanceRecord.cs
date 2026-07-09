using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class AttendanceRecord
{
    private AttendanceRecord() { }

    public AttendanceRecord(
        Guid id,
        Guid workerId,
        DateTime attendanceTimeUtc,
        AttendanceStatus attendanceStatus,
        string? source = null,
        string? sourcePayload = null,
        string? sourceRawId = null,
        string? attendanceUserId = null,
        string? badgeNumber = null,
        DateTime? createdAtUtc = null)
    {
        if (workerId == Guid.Empty)
            throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (attendanceTimeUtc == default)
            throw new ArgumentException("AttendanceTimeUtc is required.", nameof(attendanceTimeUtc));

        Id = id;
        WorkerId = workerId;
        AttendanceTimeUtc = attendanceTimeUtc;
        AttendanceStatus = attendanceStatus;
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        SourceRawId = string.IsNullOrWhiteSpace(sourceRawId) ? null : sourceRawId.Trim();
        AttendanceUserId = string.IsNullOrWhiteSpace(attendanceUserId) ? null : attendanceUserId.Trim();
        BadgeNumber = string.IsNullOrWhiteSpace(badgeNumber) ? null : badgeNumber.Trim();
        SourcePayload = string.IsNullOrWhiteSpace(sourcePayload) ? null : sourcePayload.Trim();
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
    }

    public Guid Id { get; init; }
    public Guid WorkerId { get; init; }
    public Worker? Worker { get; set; }
    public DateTime AttendanceTimeUtc { get; private set; }
    public AttendanceStatus AttendanceStatus { get; private set; }
    public string? Source { get; private set; }
    public string? SourceRawId { get; private set; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? AttendanceUserId { get; private set; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? BadgeNumber { get; private set; }
    public string? SourcePayload { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void UpdateAttendanceStatus(
        DateTime attendanceTimeUtc,
        AttendanceStatus attendanceStatus,
        string? source = null,
        string? sourcePayload = null,
        string? sourceRawId = null,
        string? attendanceUserId = null,
        string? badgeNumber = null,
        DateTime? updatedAtUtc = null)
    {
        if (attendanceTimeUtc == default)
        {
            throw new ArgumentException("AttendanceTimeUtc is required.", nameof(attendanceTimeUtc));
        }

        AttendanceTimeUtc = attendanceTimeUtc;
        AttendanceStatus = attendanceStatus;
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        SourceRawId = string.IsNullOrWhiteSpace(sourceRawId) ? null : sourceRawId.Trim();
        AttendanceUserId = string.IsNullOrWhiteSpace(attendanceUserId) ? AttendanceUserId : attendanceUserId.Trim();
        BadgeNumber = string.IsNullOrWhiteSpace(badgeNumber) ? BadgeNumber : badgeNumber.Trim();
        SourcePayload = string.IsNullOrWhiteSpace(sourcePayload) ? SourcePayload : sourcePayload.Trim();
        CreatedAtUtc = updatedAtUtc ?? DateTime.UtcNow;
    }
}
