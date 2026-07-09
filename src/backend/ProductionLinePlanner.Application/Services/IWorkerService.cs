using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Services;

public interface IWorkerService
{
    Task<Result<PagedResult<WorkerDto>>> GetWorkersAsync(
        string? search = null,
        bool? isActive = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<WorkerDto>> GetWorkerByIdAsync(
        Guid workerId,
        CancellationToken cancellationToken = default);

    Task<Result<WorkerDto>> CreateWorkerAsync(
        CreateWorkerRequest request,
        CancellationToken cancellationToken = default);
}
