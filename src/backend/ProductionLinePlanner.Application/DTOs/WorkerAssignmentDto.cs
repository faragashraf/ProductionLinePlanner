using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.DTOs;

public sealed class WorkerAssignmentDto
{
    public Guid Id { get; init; }
    public Guid WorkerId { get; init; }
    public Guid? SubStageId { get; init; }
    public Guid? FromSubStageId { get; init; }
    public Guid? ToSubStageId { get; init; }
    public AssignmentType AssignmentType { get; init; }
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public string? Reason { get; init; }
    public Guid? ReplacementForWorkerId { get; init; }
}
