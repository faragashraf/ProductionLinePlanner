using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class Worker
{
    private Worker() { }

    public Worker(
        Guid id,
        string employeeCode,
        string fullName,
        string? attendanceUserId = null,
        string? badgeNumber = null,
        string? phone = null,
        bool isActive = true,
        EmploymentStatus employmentStatus = EmploymentStatus.Active,
        DateTime? employmentEndDate = null,
        int? attendanceDepartmentId = null,
        string? photoReference = null,
        DateTime? lastExternalSyncAt = null,
        DateTime? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new ArgumentException("EmployeeCode is required.", nameof(employeeCode));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Worker full name is required.", nameof(fullName));

        Id = id;
        EmployeeCode = employeeCode.Trim();
        FullName = fullName.Trim();
        AttendanceUserId = string.IsNullOrWhiteSpace(attendanceUserId) ? null : attendanceUserId.Trim();
        BadgeNumber = string.IsNullOrWhiteSpace(badgeNumber) ? null : badgeNumber.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        IsActive = isActive;
        EmploymentStatus = employmentStatus;
        EmploymentEndDate = employmentEndDate;
        AttendanceDepartmentId = attendanceDepartmentId;
        PhotoReference = string.IsNullOrWhiteSpace(photoReference) ? null : photoReference.Trim();
        LastExternalSyncAt = lastExternalSyncAt;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public string EmployeeCode { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? AttendanceUserId { get; private set; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? BadgeNumber { get; private set; }
    public string? Phone { get; private set; }
    /// <summary>Planner-owned department text imported from the workbook. Never written back to ZKTime.</summary>
    public string? LocalDepartmentName { get; private set; }
    public bool IsActive { get; private set; }
    public EmploymentStatus EmploymentStatus { get; private set; }
    public DateTime? EmploymentEndDate { get; private set; }
    public int? AttendanceDepartmentId { get; private set; }
    public string? PhotoReference { get; private set; }
    public DateTime? LastExternalSyncAt { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public WorkerDefaultAssignment? DefaultAssignment { get; private set; }
    public List<WorkerDefaultAssignment> DefaultAssignmentHistory { get; } = [];
    public List<WorkerTemporaryAssignment> TemporaryAssignments { get; } = [];

    public void SetPhone(string? phone, DateTime? atUtc = null)
    {
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void SetLocalDepartmentName(string? departmentName, DateTime? atUtc = null)
    {
        LocalDepartmentName = string.IsNullOrWhiteSpace(departmentName) ? null : departmentName.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void SetPhotoReference(string? photoReference, DateTime? atUtc = null)
    {
        PhotoReference = string.IsNullOrWhiteSpace(photoReference) ? null : photoReference.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void UpdateName(string fullName, DateTime? atUtc = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Worker full name is required.", nameof(fullName));

        FullName = fullName.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Updates only the stable identities observed in the external attendance directory.
    /// Planner-owned profile, employment, assignment, and compensation fields deliberately
    /// remain outside this synchronization boundary.
    /// </summary>
    public bool SynchronizeAttendanceIdentity(
        string attendanceUserId,
        string badgeNumber,
        DateTime synchronizedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(attendanceUserId))
            throw new ArgumentException("AttendanceUserId is required.", nameof(attendanceUserId));
        if (string.IsNullOrWhiteSpace(badgeNumber))
            throw new ArgumentException("BadgeNumber is required.", nameof(badgeNumber));

        var normalizedAttendanceUserId = attendanceUserId.Trim();
        var normalizedBadgeNumber = badgeNumber.Trim();
        var changed = !string.Equals(AttendanceUserId, normalizedAttendanceUserId, StringComparison.OrdinalIgnoreCase) ||
                      !string.Equals(BadgeNumber, normalizedBadgeNumber, StringComparison.OrdinalIgnoreCase) ||
                      LastExternalSyncAt is null;

        if (!changed)
        {
            return false;
        }

        AttendanceUserId = normalizedAttendanceUserId;
        BadgeNumber = normalizedBadgeNumber;
        LastExternalSyncAt = synchronizedAtUtc;
        UpdatedAtUtc = synchronizedAtUtc;
        return true;
    }

    /// <summary>
    /// Applies the authoritative current-worker signal supplied by the durable attendance staging
    /// contract. A repeated former-worker observation preserves the original employment end date.
    /// </summary>
    public bool SynchronizeAttendanceEmployment(bool isCurrentWorker, DateTime synchronizedAtUtc)
    {
        if (isCurrentWorker)
        {
            if (IsActive && EmploymentStatus == EmploymentStatus.Active && EmploymentEndDate is null)
            {
                return false;
            }

            Activate(synchronizedAtUtc);
        }
        else
        {
            if (!IsActive && EmploymentStatus == EmploymentStatus.LeftEmployment)
            {
                return false;
            }

            SetEmploymentStatus(
                EmploymentStatus.LeftEmployment,
                synchronizedAtUtc,
                EmploymentEndDate ?? synchronizedAtUtc);
        }

        LastExternalSyncAt = synchronizedAtUtc;
        UpdatedAtUtc = synchronizedAtUtc;
        return true;
    }

    public void SetEmploymentStatus(EmploymentStatus status, DateTime? atUtc = null, DateTime? employmentEndDate = null)
    {
        EmploymentStatus = status;

        if (status == EmploymentStatus.LeftEmployment)
        {
            if (!employmentEndDate.HasValue)
            {
                employmentEndDate = atUtc ?? DateTime.UtcNow;
            }
            EmploymentEndDate = employmentEndDate;
            IsActive = false;
        }
        else
        {
            EmploymentEndDate = null;
            IsActive = true;
        }

        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void Suspend(DateTime? atUtc = null)
    {
        EmploymentStatus = EmploymentStatus.Suspended;
        IsActive = false;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void Activate(DateTime? atUtc = null)
    {
        EmploymentStatus = EmploymentStatus.Active;
        EmploymentEndDate = null;
        IsActive = true;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

}
