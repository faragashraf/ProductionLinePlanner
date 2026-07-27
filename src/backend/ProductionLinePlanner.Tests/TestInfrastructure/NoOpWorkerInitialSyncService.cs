using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Tests.TestInfrastructure;

/// <summary>Explicit test double for attendance tests that exercise aggregation only.</summary>
public sealed class NoOpWorkerInitialSyncService : IWorkerInitialSyncService
{
    public Task<Result<WorkerActiveServiceSyncPreviewDto>> PreviewActiveServiceSyncAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<WorkerActiveServiceSyncPreviewDto>.Success(new WorkerActiveServiceSyncPreviewDto()));

    public Task<Result<WorkerInitialSyncResultDto>> SyncWorkersAsync(
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<WorkerInitialSyncResultDto>.Success(new WorkerInitialSyncResultDto()));

    public Task<Result<WorkerInitialSyncResultDto>> SyncWorkersForAttendanceAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<WorkerInitialSyncResultDto>.Success(new WorkerInitialSyncResultDto()));
}
