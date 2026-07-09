namespace ProductionLinePlanner.Application.DTOs;

public sealed class SubStageCurrentWorkersDto
{
    public Guid SubStageId { get; init; }
    public int WorkersCount { get; init; }
    public SubStageCurrentWorkerDto[] Items { get; init; } = [];
}

