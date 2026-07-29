using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Services;

public interface IProductModelService
{
    Task<PagedResult<ProductModelDto>> GetModelsAsync(
        string? search,
        bool? isActive = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<ProductModelDto>> GetModelAsync(Guid modelId, CancellationToken cancellationToken = default);

    Task<Result<ProductModelDto>> CreateModelAsync(
        CreateProductModelRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<ProductModelDto>> UpdateModelAsync(
        Guid modelId,
        UpdateProductModelRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result> SetModelActivationAsync(Guid modelId, bool isActive, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default);

    Task<Result> DeleteModelAsync(Guid modelId, Guid actorUserId, string? requestMeta = null, CancellationToken cancellationToken = default);

    Task<Result<ProductModelDeleteEligibilityDto>> GetModelDeleteEligibilityAsync(Guid modelId, CancellationToken cancellationToken = default);

    Task<Result<ProductModelStageDto[]>> GetModelStagesAsync(Guid modelId, Guid productionLineId, CancellationToken cancellationToken = default);

    Task<Result<ProductModelStageDto>> AddModelStageAsync(
        Guid modelId,
        Guid productionLineId,
        UpsertProductModelStageRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<ProductModelStageDto>> UpdateModelStageAsync(
        Guid modelId,
        Guid productionLineId,
        Guid modelStageId,
        UpsertProductModelStageRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateModelStageAsync(
        Guid modelId,
        Guid productionLineId,
        Guid modelStageId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<CopyProductModelStagesSummaryDto>> CopyModelStagesAsync(
        Guid sourceModelId,
        CopyProductModelStagesRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);
}
