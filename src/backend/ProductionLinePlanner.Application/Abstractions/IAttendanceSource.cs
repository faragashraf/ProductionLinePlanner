using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Abstractions;

public sealed record AttendanceSourcePunch(
    long? SourceRecordId,
    int? UserId,
    string? BadgeNumber,
    DateTime CheckTimeLocal,
    string? CheckType,
    string SourceRawId);

public sealed record AttendanceSourceBatch(
    Guid? LeaseId,
    int SourceUsersCount,
    IReadOnlyCollection<AttendanceSourcePunch> Punches,
    bool SupportsAcknowledgement);

/// <summary>
/// Source port for raw attendance punches. CHECKTIME remains source-local; UTC conversion belongs
/// to the existing attendance engine so daylight-saving rules are applied exactly once.
/// </summary>
public interface IAttendanceSource
{
    Task<Result<AttendanceSourceBatch>> ClaimAsync(
        DateTime startLocal,
        DateTime endLocal,
        CancellationToken cancellationToken = default);

    Task<Result> CompleteAsync(
        AttendanceSourceBatch batch,
        IReadOnlyCollection<SourceProcessingOutcome> outcomes,
        CancellationToken cancellationToken = default);
}
