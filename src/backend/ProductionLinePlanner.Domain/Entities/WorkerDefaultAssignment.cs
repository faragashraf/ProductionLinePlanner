using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class WorkerDefaultAssignment
{
    private WorkerDefaultAssignment() { }

    public WorkerDefaultAssignment(
        Guid id,
        Guid workerId,
        Guid subStageId,
        Guid assignedByUserId,
        DateTime assignedAtUtc,
        string? reason = null,
        bool isActive = true,
        DateTime? createdAtUtc = null,
        Guid productionLineId = default)
    {
        if (workerId == Guid.Empty)
            throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (subStageId == Guid.Empty)
            throw new ArgumentException("SubStageId is required.", nameof(subStageId));
        if (productionLineId == Guid.Empty)
            throw new ArgumentException("ProductionLineId is required.", nameof(productionLineId));
        if (assignedByUserId == Guid.Empty)
            throw new ArgumentException("AssignedByUserId is required.", nameof(assignedByUserId));
        if (assignedAtUtc == default)
            throw new ArgumentException("AssignedAtUtc is required.", nameof(assignedAtUtc));

        Id = id;
        WorkerId = workerId;
        SubStageId = subStageId;
        ProductionLineId = productionLineId;
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
    public Guid ProductionLineId { get; init; }
    public ProductionLine? ProductionLine { get; set; }
    public DateTime AssignedAt { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public bool IsActive { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public AssignmentType AssignmentType => AssignmentType.Default;

    public void Deactivate(DateTime atUtc)
    {
        if (!IsActive)
            throw new InvalidOperationException("Only active default assignments can be removed.");

        IsActive = false;
        UpdatedAtUtc = atUtc;
    }
}
