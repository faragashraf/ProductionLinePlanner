using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Services;

public interface IEmployeeMasterDataService
{
    Task<PagedResult<WorkerDto>> GetWorkersAsync(
        string? search,
        bool? isActive = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<WorkerDto>> UpdateMasterIdentityAsync(
        Guid workerId,
        UpdateWorkerRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<WorkerDto>> SetEmploymentStatusAsync(
        Guid workerId,
        SetWorkerEmploymentStatusRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<WorkerDto>> GetWorkerAsync(Guid workerId, CancellationToken cancellationToken = default);
}
