namespace ProductionLinePlanner.Domain.Entities;

public class AssignmentTimelineEntry
{
    private AssignmentTimelineEntry() { }

    public AssignmentTimelineEntry(
        Guid id,
        Guid workerId,
        Guid? fromSubStageId,
        Guid? toSubStageId,
        string assignmentType,
        string actionType,
        string? reason,
        DateTime startAtUtc,
        DateTime? endAtUtc,
        Guid performedByUserId,
        bool isAutomatic,
        Guid? relatedTemporaryAssignmentId = null,
        Guid? replacementForWorkerId = null,
        DateTime? createdAtUtc = null)
    {
        if (workerId == Guid.Empty)
            throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (performedByUserId == Guid.Empty)
            throw new ArgumentException("PerformedByUserId is required.", nameof(performedByUserId));
        if (string.IsNullOrWhiteSpace(assignmentType))
            throw new ArgumentException("AssignmentType is required.", nameof(assignmentType));
        if (string.IsNullOrWhiteSpace(actionType))
            throw new ArgumentException("ActionType is required.", nameof(actionType));
        if (startAtUtc == default)
            throw new ArgumentException("StartAtUtc is required.", nameof(startAtUtc));
        if (endAtUtc.HasValue && endAtUtc.Value <= startAtUtc)
            throw new ArgumentException("EndAtUtc must be after StartAtUtc.", nameof(endAtUtc));

        Id = id;
        WorkerId = workerId;
        FromSubStageId = fromSubStageId;
        ToSubStageId = toSubStageId;
        AssignmentType = assignmentType.Trim();
        ActionType = actionType.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        PerformedByUserId = performedByUserId;
        IsAutomatic = isAutomatic;
        RelatedTemporaryAssignmentId = relatedTemporaryAssignmentId;
        ReplacementForWorkerId = replacementForWorkerId;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
    }

    public Guid Id { get; init; }
    public Guid WorkerId { get; init; }
    public Worker? Worker { get; set; }
    public Guid? FromSubStageId { get; init; }
    public SubStage? FromSubStage { get; set; }
    public Guid? ToSubStageId { get; init; }
    public SubStage? ToSubStage { get; set; }
    public string AssignmentType { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public DateTime StartAtUtc { get; init; }
    public DateTime? EndAtUtc { get; init; }
    public Guid PerformedByUserId { get; init; }
    public AppUser? PerformedByUser { get; set; }
    public bool IsAutomatic { get; init; }
    public Guid? RelatedTemporaryAssignmentId { get; init; }
    public Guid? ReplacementForWorkerId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
