using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Services;

public interface IRealDataIntakeService
{
    Task<RealDataIntakePreviewDto> PreviewAsync(RealDataIntakeUpload upload, CancellationToken cancellationToken = default);
    Task<RealDataIntakeApplyResultDto> ApplyAsync(RealDataIntakeUpload upload, Guid actorId, CancellationToken cancellationToken = default);
    Task<ProductionDayReviewDto> GetProductionDayReviewAsync(Guid productionOrderId, CancellationToken cancellationToken = default);
    Task<ProductionDayReviewDto> MarkStageNotOperatedAsync(Guid productionOrderId, Guid productModelStageId, string reason, Guid actorId, CancellationToken cancellationToken = default);
    Task<ProductionDayReviewDto> SetParticipantOverrideAsync(Guid productionOrderId, Guid stageProductionRecordId, Guid workerId, string reason, Guid actorId, CancellationToken cancellationToken = default);
    Task<ProductionDayReviewDto> ApproveProductionDayAsync(Guid productionOrderId, Guid actorId, CancellationToken cancellationToken = default);
}
