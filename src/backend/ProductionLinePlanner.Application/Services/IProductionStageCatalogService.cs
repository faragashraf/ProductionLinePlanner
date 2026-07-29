using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Services;

public interface IProductionStageCatalogService
{
    Task<PagedResult<MainStageDto>> GetMainStagesAsync(
        Guid? departmentId,
        string? search,
        bool? isActive = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<MainStageDto>> GetMainStageAsync(Guid mainStageId, CancellationToken cancellationToken = default);

    Task<PagedResult<SubStageDto>> GetSubStagesAsync(
        Guid? mainStageId,
        string? search = null,
        bool? isActive = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<SubStageDto>> GetSubStageAsync(Guid subStageId, CancellationToken cancellationToken = default);

    Task<PagedResult<SubStageDto>> GetOperationalStagesAsync(
        Guid? factoryId,
        Guid? departmentId,
        string? name,
        string? code,
        bool? isActive,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<SubStageDto>> CreateOperationalStageAsync(
        Guid departmentId,
        string name,
        int capacity,
        bool isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<MainStageDto>> CreateMainStageAsync(
        Guid departmentId,
        string name,
        bool isCritical,
        int sequenceOrder,
        bool isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<MainStageDto>> UpdateMainStageAsync(
        Guid mainStageId,
        string? name,
        bool? isCritical,
        int? sequenceOrder,
        bool? isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateMainStageAsync(
        Guid mainStageId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<SubStageDto>> CreateSubStageAsync(
        Guid mainStageId,
        string code,
        string name,
        int defaultOrder,
        int capacity,
        bool isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<SubStageDto>> UpdateSubStageAsync(
        Guid subStageId,
        string? code,
        string? name,
        int? defaultOrder,
        int? capacity,
        bool? isActive,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<SubStageDto>> DeactivateSubStageAsync(
        Guid subStageId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<StageDependencySummaryDto>> GetSubStageDependenciesAsync(Guid subStageId, CancellationToken cancellationToken = default);

    Task<Result> DeleteSubStageAsync(
        Guid subStageId,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);
}
