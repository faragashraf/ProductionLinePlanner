using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

/// <summary>Read-only adapter for the legacy direct ZKTime mode.</summary>
public sealed class ZkTimeDirectAttendanceSource(
    AttendanceDbContext attendanceDbContext,
    IOptions<AttendanceSourceOptions> sourceOptions,
    ILogger<ZkTimeDirectAttendanceSource> logger) : IAttendanceSource
{
    public async Task<Result<AttendanceSourceBatch>> ClaimAsync(
        DateTime startLocal,
        DateTime endLocal,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (attendanceDbContext.Database.IsRelational())
            {
                attendanceDbContext.Database.SetCommandTimeout(Math.Max(1, sourceOptions.Value.SyncReadCommandTimeoutSeconds));
            }

            var sourceUsers = await attendanceDbContext.UserInfos
                .AsNoTracking()
                .Select(user => new { user.UserId, user.BadgeNumber })
                .ToArrayAsync(cancellationToken);
            var badgeByUserId = sourceUsers
                .Where(user => user.UserId.HasValue)
                .GroupBy(user => user.UserId!.Value)
                .ToDictionary(group => group.Key, group => group.First().BadgeNumber);
            var punches = await attendanceDbContext.CheckInOuts
                .AsNoTracking()
                .Where(punch => punch.CheckTime >= startLocal && punch.CheckTime < endLocal && punch.UserId != null)
                .Select(punch => new { punch.UserId, punch.CheckTime, punch.CheckType })
                .ToArrayAsync(cancellationToken);

            return Result<AttendanceSourceBatch>.Success(new AttendanceSourceBatch(
                LeaseId: null,
                SourceUsersCount: sourceUsers.Length,
                Punches: punches.Select(punch => new AttendanceSourcePunch(
                    SourceRecordId: null,
                    UserId: punch.UserId,
                    BadgeNumber: punch.UserId.HasValue && badgeByUserId.TryGetValue(punch.UserId.Value, out var badge) ? badge : null,
                    CheckTimeLocal: punch.CheckTime,
                    CheckType: punch.CheckType,
                    SourceRawId: $"{punch.UserId}:{punch.CheckTime:O}:{Normalize(punch.CheckType)}")).ToArray(),
                SupportsAcknowledgement: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to read raw attendance punches from direct ZKTime source.");
            return Result<AttendanceSourceBatch>.Failure(new Error(
                "AttendanceSourceError",
                "Unable to connect to the configured attendance source."));
        }
    }

    public Task<Result> CompleteAsync(
        AttendanceSourceBatch batch,
        IReadOnlyCollection<SourceProcessingOutcome> outcomes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
