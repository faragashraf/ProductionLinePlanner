using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Services;

public interface IWorkerAssignmentService
{
    Task<Result<WorkerAssignmentDto>> CreateDefaultAssignmentAsync(
        CreateDefaultAssignmentRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<WorkerAssignmentDto>> CreateTemporaryAssignmentAsync(
        CreateTemporaryAssignmentRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> CancelTemporaryAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<WorkerAssignmentDto>>> GetWorkerAssignmentHistoryAsync(
        Guid? workerId = null,
        Guid? subStageId = null,
        DateTime? fromDateUtc = null,
        DateTime? toDateUtc = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}
