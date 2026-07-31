using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Infrastructure.Attendance;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

/// <summary>
/// Owns the shared rule that decides whether attendance can support operational truth.
/// Dashboards and readiness views must not infer absence independently.
/// </summary>
public sealed class AttendanceFreshnessEngine(
    AppDbContext dbContext,
    IOptions<AttendanceSourceOptions> sourceOptions,
    IAttendanceWorkdayPolicy workdayPolicy) : IAttendanceFreshnessEngine
{
    private readonly AttendanceSourceOptions options = sourceOptions.Value;

    public async Task<AttendanceSyncFreshnessDto> GetAsync(
        DateOnly operationalDate,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var asOf = asOfUtc.Kind == DateTimeKind.Utc
            ? asOfUtc
            : DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var window = workdayPolicy.GetWindow(operationalDate);
        var state = await dbContext.AttendanceSyncStates.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.SourceName == options.SourceName && item.OperationalDate == operationalDate,
                cancellationToken);
        if (state is null)
        {
            var latestImportedRecordAtUtc = await dbContext.AttendanceRecords.AsNoTracking()
                .Where(record => record.AttendanceTimeUtc >= window.StartUtc && record.AttendanceTimeUtc < window.EndUtc)
                .MaxAsync(record => (DateTime?)record.CreatedAtUtc, cancellationToken);
            if (!latestImportedRecordAtUtc.HasValue)
            {
                return new AttendanceSyncFreshnessDto("NeverSynced", false, null, null, null, null);
            }

            var recordAge = Math.Max(
                0,
                (int)Math.Floor((asOf - DateTime.SpecifyKind(latestImportedRecordAtUtc.Value, DateTimeKind.Utc)).TotalMinutes));
            return new AttendanceSyncFreshnessDto("RecordsAvailable", true, null, null, null, recordAge);
        }

        var age = state.LastSuccessfulAtUtc.HasValue
            ? Math.Max(
                0,
                (int)Math.Floor((asOf - DateTime.SpecifyKind(state.LastSuccessfulAtUtc.Value, DateTimeKind.Utc)).TotalMinutes))
            : (int?)null;
        // A transient retry failure is visible as Failed, but it does not invalidate a
        // successful snapshot that remains inside the configured freshness window.
        var trusted = age.HasValue && age.Value <= options.FreshnessThresholdMinutes;
        var status = !state.LastAttemptSucceeded ? "Failed" : trusted ? "Fresh" : "Stale";
        return new AttendanceSyncFreshnessDto(
            status,
            trusted,
            state.LastAttemptAtUtc,
            state.LastSuccessfulAtUtc,
            state.LastErrorCode,
            age);
    }
}
