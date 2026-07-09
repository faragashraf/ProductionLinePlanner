namespace ProductionLinePlanner.Application.DTOs;

public sealed class AssignmentTimelineDto
{
    public Guid Id { get; init; }
    public Guid WorkerId { get; init; }
    public Guid? FromSubStageId { get; init; }
    public Guid? ToSubStageId { get; init; }
    public string AssignmentType { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public DateTime StartAtUtc { get; init; }
    public DateTime? EndAtUtc { get; init; }
    public Guid PerformedByUserId { get; init; }
    public bool IsAutomatic { get; init; }
    public Guid? RelatedTemporaryAssignmentId { get; init; }
    public Guid? ReplacementForWorkerId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
