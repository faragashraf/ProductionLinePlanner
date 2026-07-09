using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Services;

public interface IFactoryService
{
    Task<Result<PagedResult<FactoryDto>>> GetFactoriesAsync(
        bool? isActive,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<FactoryDto>> GetFactoryByIdAsync(
        Guid factoryId,
        CancellationToken cancellationToken = default);

    Task<Result<FactoryDto>> CreateFactoryAsync(
        CreateFactoryRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<FactoryDto>> UpdateFactoryAsync(
        Guid factoryId,
        UpdateFactoryRequest request,
        CancellationToken cancellationToken = default);
}
