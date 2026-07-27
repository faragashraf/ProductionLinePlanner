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

    /// <summary>
    /// Executes the source-to-worker synchronization as part of the attendance pipeline.
    /// It has no end-user audit actor because it is system orchestration rather than a UI action.
    /// </summary>
    Task<Result<WorkerInitialSyncResultDto>> SyncWorkersForAttendanceAsync(
        CancellationToken cancellationToken = default);
}
