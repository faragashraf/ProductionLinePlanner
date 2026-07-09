namespace ProductionLinePlanner.Application.DTOs;

public sealed class CancelTemporaryAssignmentResultDto
{
    public Guid AssignmentId { get; init; }
    public DateTime CancelledAt { get; init; }
    public string Status { get; init; } = string.Empty;
}

