using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class WorkerTemporaryAssignment
{
    private WorkerTemporaryAssignment() { }

    public WorkerTemporaryAssignment(
        Guid id,
        Guid workerId,
        Guid? fromSubStageId,
        Guid toSubStageId,
        DateTime startAtUtc,
        DateTime endAtUtc,
        Guid assignedByUserId,
        string reason,
        Guid? replacementForWorkerId = null,
        TemporaryAssignmentMode participationMode = TemporaryAssignmentMode.TemporaryMove,
        string status = "Scheduled",
        DateTime? createdAtUtc = null)
    {
        if (workerId == Guid.Empty)
            throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (participationMode == TemporaryAssignmentMode.TemporaryMove && (!fromSubStageId.HasValue || fromSubStageId == Guid.Empty))
            throw new ArgumentException("FromSubStageId is required for a temporary move.", nameof(fromSubStageId));
        if (toSubStageId == Guid.Empty)
            throw new ArgumentException("ToSubStageId is required.", nameof(toSubStageId));
        if (assignedByUserId == Guid.Empty)
            throw new ArgumentException("AssignedByUserId is required.", nameof(assignedByUserId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));
        if (startAtUtc == default || endAtUtc == default)
            throw new ArgumentException("StartAtUtc and EndAtUtc are required.");
        if (startAtUtc >= endAtUtc)
            throw new ArgumentException("StartAtUtc must be earlier than EndAtUtc.");

        Id = id;
        WorkerId = workerId;
        FromSubStageId = fromSubStageId;
        ToSubStageId = toSubStageId;
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        AssignedByUserId = assignedByUserId;
        Reason = reason.Trim();
        ReplacementForWorkerId = replacementForWorkerId;
        ParticipationMode = participationMode;
        Status = status;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid WorkerId { get; init; }
    public Worker? Worker { get; set; }
    public Guid? FromSubStageId { get; init; }
    public SubStage? FromSubStage { get; set; }
    public Guid ToSubStageId { get; init; }
    public SubStage? ToSubStage { get; set; }
    public DateTime StartAtUtc { get; private set; }
    public DateTime EndAtUtc { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public Guid? ReplacementForWorkerId { get; private set; }
    public TemporaryAssignmentMode ParticipationMode { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public AssignmentType AssignmentType => ReplacementForWorkerId.HasValue
        ? AssignmentType.Replacement
        : AssignmentType.Temporary;

    public bool IsScheduledOrActive => Status is "Scheduled" or "Active";

    public bool OverlapsWith(WorkerTemporaryAssignment other)
    {
        if (other.WorkerId != WorkerId)
            return false;
        if (!other.IsScheduledOrActive)
            return false;

        return StartAtUtc < other.EndAtUtc && other.StartAtUtc < EndAtUtc;
    }

    public void AssertNoActiveTemporalOverlap(IEnumerable<WorkerTemporaryAssignment> assignments)
    {
        // Repository/service layer should call this before persistence.
        var hasConflict = assignments.Any(x =>
            x.Id != Id &&
            x.WorkerId == WorkerId &&
            x.IsScheduledOrActive &&
            this.OverlapsWith(x));

        if (hasConflict)
            throw new InvalidOperationException("Worker already has an overlapping scheduled/active temporary assignment.");
    }
}
