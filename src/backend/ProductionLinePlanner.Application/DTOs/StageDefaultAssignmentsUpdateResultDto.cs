namespace ProductionLinePlanner.Application.DTOs;

public sealed record StageDefaultAssignmentsUpdateResultDto(
    Guid SubStageId,
    int AddedWorkersCount,
    int RemovedWorkersCount,
    IReadOnlyCollection<Guid> ActiveWorkerIds);
