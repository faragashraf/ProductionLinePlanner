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

    public void UpdateContactInfo(string? zkEmployeeCode, string? phone, DateTime? atUtc = null)
    {
        AttendanceUserId = string.IsNullOrWhiteSpace(zkEmployeeCode) ? null : zkEmployeeCode.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void SetAttendanceDepartmentId(int? attendanceDepartmentId, DateTime? atUtc = null)
    {
        AttendanceDepartmentId = attendanceDepartmentId;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
        LastExternalSyncAt = UpdatedAtUtc;
    }

    public void SetPhone(string? phone, DateTime? atUtc = null)
    {
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
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

    public void MarkExternalSync(DateTime syncedAt, int? attendanceDepartmentId = null)
    {
        LastExternalSyncAt = syncedAt;
        if (attendanceDepartmentId.HasValue)
        {
            AttendanceDepartmentId = attendanceDepartmentId;
        }
        UpdatedAtUtc = syncedAt;
    }
}
