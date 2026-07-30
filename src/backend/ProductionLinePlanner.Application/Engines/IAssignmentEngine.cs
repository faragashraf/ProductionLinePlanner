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

    /// <summary>
    /// Resolves every effective stage participation for each worker.  The
    /// singular resolver remains for legacy callers that only understand a
    /// primary assignment.
    /// </summary>
    Task<Result<Dictionary<Guid, IReadOnlyCollection<WorkerAssignmentState>>>> ResolveEffectiveAssignmentsAsync(
        IEnumerable<Guid> workerIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes expired temporary assignments as an explicit write operation.
    /// Read resolvers deliberately do not mutate assignment state.
    /// </summary>
    Task<Result<int>> FinalizeCompletedTemporaryAssignmentsAsync(
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<AssignmentActionResultDto>> CreateOrUpdateDefaultAssignmentAsync(
        CreateDefaultAssignmentRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles permanent participation for one stage only. It is additive
    /// across stages: a worker keeps every other active participation.
    /// </summary>
    Task<Result<StageDefaultAssignmentsUpdateResultDto>> UpdateStageDefaultAssignmentsAsync(
        Guid productionLineId,
        Guid subStageId,
        IReadOnlyCollection<Guid>? workerIds,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default);

    Task<Result<AssignmentActionResultDto>> RemoveDefaultAssignmentAsync(
        Guid workerId,
        Guid productionLineId,
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

    /// <summary>
    /// Returns structural staffing coverage for every active sub-stage in one batch.
    /// The result uses the same effective-assignment rules as line staffing and
    /// intentionally does not evaluate attendance.
    /// </summary>
    Task<Result<IReadOnlyCollection<SubStageAssignmentCoverageDto>>> GetActiveSubStageAssignmentCoverageAsync(
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
    Guid? ReplacementForWorkerId,
    TemporaryAssignmentMode? ParticipationMode = null,
    Guid? ProductionLineId = null,
    string? ProductionLineName = null);
