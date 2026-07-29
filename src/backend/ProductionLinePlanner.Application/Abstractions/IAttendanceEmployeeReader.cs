using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Abstractions;

public sealed record WorkerIdentitySourceItem(
    long? SourceRecordId,
    AttendanceEmployeeRecord Worker,
    bool IsClaimed = true);

public sealed record WorkerIdentitySourceBatch(
    Guid? LeaseId,
    IReadOnlyCollection<WorkerIdentitySourceItem> Items,
    bool SupportsAcknowledgement);

/// <summary>
/// Controlled terminal (or retry) disposition of a raw staging row.  These values mirror the
/// inbox contract; callers cannot turn a business skip into a technical failure by overloading an
/// error message.
/// </summary>
public enum SourceProcessingDisposition
{
    Pending,
    Processed,
    Skipped,
    Failed
}

public sealed record SourceProcessingOutcome(
    long SourceRecordId,
    SourceProcessingDisposition Disposition,
    string? ResolutionCode = null,
    string? ResolutionDetails = null)
{
    public static SourceProcessingOutcome Processed(long sourceRecordId, string? resolutionCode = null) =>
        new(sourceRecordId, SourceProcessingDisposition.Processed, resolutionCode);

    public static SourceProcessingOutcome Retry(long sourceRecordId, string resolutionCode, string? details = null) =>
        new(sourceRecordId, SourceProcessingDisposition.Pending, resolutionCode, details);

    public static SourceProcessingOutcome Skipped(long sourceRecordId, string resolutionCode, string? details = null) =>
        new(sourceRecordId, SourceProcessingDisposition.Skipped, resolutionCode, details);

    public static SourceProcessingOutcome Failed(long sourceRecordId, string resolutionCode, string? details = null) =>
        new(sourceRecordId, SourceProcessingDisposition.Failed, resolutionCode, details);
}

/// <summary>
/// Source port for worker identities. Direct sources use the default no-op acknowledgement;
/// durable staging sources override claim and completion to implement leases and retries.
/// </summary>
public interface IWorkerIdentitySource
{
    Task<Result<AttendanceEmployeeRecord?>> GetByAttendanceUserIdAsync(string attendanceUserId, CancellationToken cancellationToken = default);

    Task<Result<AttendanceEmployeeRecord[]>> GetAllAsync(CancellationToken cancellationToken = default);

    async Task<Result<WorkerIdentitySourceBatch>> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetAllAsync(cancellationToken);
        return result.IsFailure
            ? Result<WorkerIdentitySourceBatch>.Failure(result.Error!)
            : Result<WorkerIdentitySourceBatch>.Success(new WorkerIdentitySourceBatch(
                LeaseId: null,
                Items: (result.Value ?? []).Select(worker => new WorkerIdentitySourceItem(null, worker)).ToArray(),
                SupportsAcknowledgement: false));
    }

    Task<Result<WorkerIdentitySourceBatch>> ClaimBatchAsync(CancellationToken cancellationToken = default) =>
        ReadSnapshotAsync(cancellationToken);

    Task<Result> CompleteBatchAsync(
        WorkerIdentitySourceBatch batch,
        IReadOnlyCollection<SourceProcessingOutcome> outcomes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}

/// <summary>Backward-compatible name for the existing direct ZKTime directory reader.</summary>
public interface IAttendanceEmployeeReader : IWorkerIdentitySource
{
}
