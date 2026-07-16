using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Engines;

public interface IAssignmentEngine
{
    Task<Result<CurrentWorkerAssignmentDto>> GetCurrentAssignmentAsync(
        Guid workerId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<Dictionary<Guid, WorkerAssignmentState>>> ResolveCurrentAssignmentsAsync(
        IEnumerable<Guid> workerIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);

    Task<Result<AssignmentActionResultDto>> CreateOrUpdateDefaultAssignmentAsync(
        CreateDefaultAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<AssignmentActionResultDto>> RemoveDefaultAssignmentAsync(
        Guid workerId,
        Guid subStageId,
        string reason,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<AssignmentActionResultDto>> CreateTemporaryAssignmentAsync(
        CreateTemporaryAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<AssignmentActionResultDto>> CreateReplacementAssignmentAsync(
        CreateReplacementAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<AssignmentActionResultDto>> MoveCurrentAssignmentAsync(
        MoveCurrentWorkerAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<CancelTemporaryAssignmentResultDto>> CancelTemporaryAssignmentAsync(
        Guid assignmentId,
        string reason,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<AssignmentTimelineDto>>> GetWorkerTimelineAsync(
        Guid workerId,
        int page = 1,
        int pageSize = 50,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<Result<SubStageCurrentWorkersDto>> GetSubStageWorkersAsync(
        Guid subStageId,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);
}

public sealed record WorkerAssignmentState(
    Guid? AssignmentId,
    Guid WorkerId,
    AssignmentType? AssignmentType,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    Guid? EffectiveSubStageId,
    Guid? FromSubStageId,
    Guid? ToSubStageId,
    Guid? ReplacementForWorkerId);
