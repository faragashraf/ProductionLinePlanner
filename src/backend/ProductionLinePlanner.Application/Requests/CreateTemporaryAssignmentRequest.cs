using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Requests;

public sealed class CreateTemporaryAssignmentRequest
{
    public Guid WorkerId { get; init; }
    public Guid? FromSubStageId { get; init; }
    public Guid ToSubStageId { get; init; }
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
    public Guid? ReplacementForWorkerId { get; init; }
    public TemporaryAssignmentMode ParticipationMode { get; init; } = TemporaryAssignmentMode.TemporaryMove;
}
