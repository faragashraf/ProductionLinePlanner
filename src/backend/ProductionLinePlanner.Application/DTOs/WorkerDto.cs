namespace ProductionLinePlanner.Application.DTOs;

public sealed class WorkerDto
{
    public Guid Id { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? AttendanceUserId { get; init; }
    /// <summary>External attendance identifiers from ZKTeco USERINFO.</summary>
    public string? BadgeNumber { get; init; }
    public string? Phone { get; init; }
    public int? AttendanceDepartmentId { get; init; }
    /// <summary>Planner-owned department label. This is not read from or written to ZKTime.</summary>
    public string? LocalDepartmentName { get; init; }
    public Guid? OrganizationalDepartmentId { get; init; }
    public string? OrganizationalDepartmentName { get; init; }
    public Guid? OrganizationalFactoryId { get; init; }
    public string? OrganizationalFactoryName { get; init; }
    public Guid OrganizationalDepartmentConcurrencyToken { get; init; }
    public string EmploymentStatus { get; init; } = string.Empty;
    public DateTime? EmploymentEndDate { get; init; }
    public string? PhotoReference { get; init; }
    public bool HasPhoto { get; init; }
    public string? PhotoVersion { get; init; }
    public bool IsActive { get; init; }
    public Guid? DefaultSubStageId { get; init; }
    public DateTime? LastExternalSyncAt { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public IReadOnlyCollection<WorkerPermanentAssignmentDto> PermanentAssignments { get; init; } = [];
}

public sealed class WorkerPermanentAssignmentDto
{
    public Guid Id { get; init; }
    public Guid FactoryId { get; init; }
    public string FactoryName { get; init; } = string.Empty;
    public Guid ProductionLineId { get; init; }
    public string ProductionLineName { get; init; } = string.Empty;
    public Guid DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public Guid MainStageId { get; init; }
    public string MainStageName { get; init; } = string.Empty;
    public Guid SubStageId { get; init; }
    public string SubStageName { get; init; } = string.Empty;
    public DateTime AssignedAtUtc { get; init; }
}
