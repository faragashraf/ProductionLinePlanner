using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class WorkerDefaultAssignment
{
    public WorkerDefaultAssignment(
        Guid id,
        Guid workerId,
        Guid subStageId,
        Guid assignedByUserId,
        DateTime assignedAtUtc,
        string? reason = null,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        if (workerId == Guid.Empty)
            throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (subStageId == Guid.Empty)
            throw new ArgumentException("SubStageId is required.", nameof(subStageId));
        if (assignedByUserId == Guid.Empty)
            throw new ArgumentException("AssignedByUserId is required.", nameof(assignedByUserId));
        if (assignedAtUtc == default)
            throw new ArgumentException("AssignedAtUtc is required.", nameof(assignedAtUtc));

        Id = id;
        WorkerId = workerId;
        SubStageId = subStageId;
        AssignedByUserId = assignedByUserId;
        AssignedAt = assignedAtUtc;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid WorkerId { get; init; }
    public Worker? Worker { get; set; }
    public Guid SubStageId { get; init; }
    public SubStage? SubStage { get; set; }
    public DateTime AssignedAt { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public bool IsActive { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public AssignmentType AssignmentType => AssignmentType.Default;
}
