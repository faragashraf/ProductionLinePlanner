namespace ProductionLinePlanner.Application.DTOs;

public sealed class AssignmentActionResultDto
{
    public Guid AssignmentId { get; init; }
    public Guid WorkerId { get; init; }
    public Guid? SubStageId { get; init; }
    public Guid? FromSubStageId { get; init; }
    public Guid? ToSubStageId { get; init; }
    public string AssignmentType { get; init; } = string.Empty;
    public DateTime? StartsAtUtc { get; init; }
    public DateTime? EndsAtUtc { get; init; }
    public string? Status { get; init; }
    public Guid? ReplacementForWorkerId { get; init; }
    public bool IsCreated { get; init; }
}

