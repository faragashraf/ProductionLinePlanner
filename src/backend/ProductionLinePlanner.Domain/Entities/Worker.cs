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
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public string EmployeeCode { get; private set; }
    public string FullName { get; private set; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? AttendanceUserId { get; private set; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? BadgeNumber { get; private set; }
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; }
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

    public void UpdateName(string fullName, DateTime? atUtc = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Worker full name is required.", nameof(fullName));

        FullName = fullName.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }
}
