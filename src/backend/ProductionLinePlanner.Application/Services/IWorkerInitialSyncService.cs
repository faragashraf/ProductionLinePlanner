using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Services;

public interface IWorkerInitialSyncService
{
    Task<Result<WorkerActiveServiceSyncPreviewDto>> PreviewActiveServiceSyncAsync(
        CancellationToken cancellationToken = default);

    Task<Result<WorkerInitialSyncResultDto>> SyncWorkersAsync(
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);
}
