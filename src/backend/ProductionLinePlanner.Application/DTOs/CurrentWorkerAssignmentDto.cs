using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed class CurrentWorkerAssignmentDto
{
    public Guid WorkerId { get; init; }
    public Guid? EffectiveSubStageId { get; init; }
    public AssignmentType? AssignmentType { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? EndsAtUtc { get; init; }
    public Guid? FromSubStageId { get; init; }
    public Guid? ToSubStageId { get; init; }
    public Guid? ReplacementForWorkerId { get; init; }
}
