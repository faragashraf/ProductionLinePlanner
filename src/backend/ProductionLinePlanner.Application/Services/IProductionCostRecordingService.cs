using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Services;

public interface IProductionCostRecordingService
{
    Task<ProductionOrderDto> CreateOrderAsync(CreateProductionOrderRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProductionOrderDto>> ListOrdersAsync(ProductionOrderStatus? status, CancellationToken cancellationToken);
    Task<ProductionOrderDto> UpdateOrderAsync(Guid id, UpdateProductionOrderRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<ProductionOrderDto> TransitionOrderAsync(Guid id, ProductionOrderStatus status, Guid actorId, CancellationToken cancellationToken);
    Task<StageProductionRecordDto> CreateDraftAsync(CreateStageProductionRecordRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<StageProductionRecordDto> CalculatePreviewAsync(CreateStageProductionRecordRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<StageProductionRecordDto> UpdateDraftAsync(Guid id, UpdateStageProductionRecordRequest request, Guid actorId, CancellationToken cancellationToken);
    Task<StageProductionRecordDto> GetRecordAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<StageProductionRecordDto>> ListRecordsAsync(DateOnly? from, DateOnly? to, StageProductionRecordStatus? status, CancellationToken cancellationToken);
    Task<StageProductionRecordDto> ApproveAsync(Guid id, Guid concurrencyToken, Guid actorId, CancellationToken cancellationToken);
    Task<StageProductionRecordDto> CancelAsync(Guid id, Guid concurrencyToken, Guid actorId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DailyProductionCostReportRowDto>> DailyReportAsync(DateOnly from, DateOnly to, Guid? orderId, Guid? modelId, Guid? workerId, CancellationToken cancellationToken);
}
