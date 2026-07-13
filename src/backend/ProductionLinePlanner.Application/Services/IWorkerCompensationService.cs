using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Services;

public interface IWorkerCompensationService
{
    Task<Result<WorkerSalaryHistoryDto>> GetCurrentSalaryAsync(Guid workerId, CancellationToken cancellationToken = default);

    Task<Result<WorkerSalaryHistoryDto[]>> GetSalaryHistoryAsync(Guid workerId, CancellationToken cancellationToken = default);

    Task<Result<WorkerSalaryHistoryDto>> SetSalaryAsync(
        Guid workerId,
        SetWorkerSalaryRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<WorkerSalaryHistoryDto>> AddHistoricalSalaryAsync(
        Guid workerId,
        SetWorkerSalaryHistoryRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);
}
