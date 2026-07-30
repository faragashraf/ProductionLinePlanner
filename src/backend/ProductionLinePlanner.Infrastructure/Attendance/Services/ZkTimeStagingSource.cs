using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

public interface IZkStagingBacklogReader
{
    Task<Result<DateOnly[]>> GetPendingProductionDatesAsync(
        TimeSpan workdayBoundaryTime,
        int maximumDates,
        CancellationToken cancellationToken = default);
}

public interface IZkStagingSchemaValidator
{
    Task ValidateAsync(CancellationToken cancellationToken = default);
}

public static class ZkTimeStagingSchema
{
    public const int CurrentVersion = 3;

    public static void EnsureCompatible(bool contractInstalled, int installedVersion)
    {
        if (!contractInstalled)
        {
            throw new InvalidOperationException(
                "AttendanceSource:Mode is 'Staging', but the required ZKTime staging schema is not installed. Run the install/upgrade script and verification before starting the API.");
        }

        if (installedVersion < CurrentVersion)
        {
            throw new InvalidOperationException(
                $"AttendanceSource:Mode is 'Staging', but staging schema version {installedVersion} is installed; version {CurrentVersion} is required. Run the ZKTime staging install/upgrade script before starting the API.");
        }
    }
}

/// <summary>
/// Verifies the durable inbox contract before a staging-mode API starts accepting work.
/// Construction is side-effect free, so Direct mode never opens the application database for this check.
/// </summary>
public sealed class ZkStagingSchemaValidator(string appConnectionString) : IZkStagingSchemaValidator
{
    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(appConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var contractCommand = new SqlCommand(
                """
                SELECT CONVERT(int,
                    CASE WHEN
                        OBJECT_ID(N'dbo.ZkSyncSchemaVersions', N'U') IS NOT NULL AND
                        OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NOT NULL AND
                        OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NOT NULL AND
                        COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'ResolutionCode') IS NOT NULL AND
                        COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'ResolutionDetails') IS NOT NULL AND
                        COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'ResolvedAtUtc') IS NOT NULL AND
                        COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'SourceDefaultDepartmentId') IS NOT NULL AND
                        COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'IsCurrentWorker') IS NOT NULL AND
                        COL_LENGTH(N'dbo.ZkAttendanceSyncInbox', N'ResolutionCode') IS NOT NULL AND
                        COL_LENGTH(N'dbo.ZkAttendanceSyncInbox', N'ResolutionDetails') IS NOT NULL AND
                        COL_LENGTH(N'dbo.ZkAttendanceSyncInbox', N'ResolvedAtUtc') IS NOT NULL AND
                        OBJECT_ID(N'dbo.usp_ZkWorkerInboxReadSnapshot', N'P') IS NOT NULL AND
                        OBJECT_ID(N'dbo.usp_ZkWorkerInboxComplete', N'P') IS NOT NULL AND
                        OBJECT_ID(N'dbo.usp_ZkAttendanceInboxClaim', N'P') IS NOT NULL AND
                        OBJECT_ID(N'dbo.usp_ZkAttendanceInboxComplete', N'P') IS NOT NULL AND
                        OBJECT_ID(N'dbo.usp_ZkAttendanceInboxPendingDates', N'P') IS NOT NULL AND
                        TYPE_ID(N'dbo.ZkInboxResolutionResult') IS NOT NULL
                    THEN 1 ELSE 0 END);
                """,
                connection);
            var contractInstalled = Convert.ToInt32(await contractCommand.ExecuteScalarAsync(cancellationToken)) == 1;
            var installedVersion = 0;
            if (contractInstalled)
            {
                await using var versionCommand = new SqlCommand(
                    "SELECT MAX(Version) FROM dbo.ZkSyncSchemaVersions;",
                    connection);
                var versionValue = await versionCommand.ExecuteScalarAsync(cancellationToken);
                installedVersion = versionValue is null or DBNull ? 0 : Convert.ToInt32(versionValue);
            }

            ZkTimeStagingSchema.EnsureCompatible(contractInstalled, installedVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "AttendanceSource:Mode is 'Staging', but the staging schema could not be validated. Verify the AppDatabase connection and run the read-only staging verification script.",
                exception);
        }
    }
}

/// <summary>
/// Durable source adapter over the Dayoub staging inbox. All state transitions are delegated to
/// short SQL Server stored procedures; this class never reads or writes ZKTime directly.
/// </summary>
public sealed class ZkTimeStagingSource(
    string appConnectionString,
    IOptions<AttendanceSourceOptions> sourceOptions,
    ILogger<ZkTimeStagingSource> logger) : IWorkerIdentitySource, IAttendanceSource, IZkStagingBacklogReader
{
    public async Task<Result<AttendanceEmployeeRecord?>> GetByAttendanceUserIdAsync(
        string attendanceUserId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        if (snapshot.IsFailure)
        {
            return Result<AttendanceEmployeeRecord?>.Failure(snapshot.Error!);
        }

        var match = snapshot.Value!.Items
            .Select(item => item.Worker)
            .SingleOrDefault(worker => string.Equals(worker.AttendanceUserId, attendanceUserId.Trim(), StringComparison.OrdinalIgnoreCase));
        return Result<AttendanceEmployeeRecord?>.Success(match);
    }

    public async Task<Result<AttendanceEmployeeRecord[]>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        return snapshot.IsFailure
            ? Result<AttendanceEmployeeRecord[]>.Failure(snapshot.Error!)
            : Result<AttendanceEmployeeRecord[]>.Success(snapshot.Value!.Items.Select(item => item.Worker).ToArray());
    }

    public Task<Result<WorkerIdentitySourceBatch>> ReadSnapshotAsync(CancellationToken cancellationToken = default) =>
        ReadWorkerBatchAsync(claim: false, cancellationToken);

    public Task<Result<WorkerIdentitySourceBatch>> ClaimBatchAsync(CancellationToken cancellationToken = default) =>
        ReadWorkerBatchAsync(claim: true, cancellationToken);

    public async Task<Result> CompleteBatchAsync(
        WorkerIdentitySourceBatch batch,
        IReadOnlyCollection<SourceProcessingOutcome> outcomes,
        CancellationToken cancellationToken = default)
    {
        if (!batch.SupportsAcknowledgement || !batch.LeaseId.HasValue || outcomes.Count == 0)
        {
            return Result.Success();
        }

        return await CompleteAsync(
            "dbo.usp_ZkWorkerInboxComplete",
            batch.LeaseId.Value,
            outcomes,
            cancellationToken);
    }

    public async Task<Result<AttendanceSourceBatch>> ClaimAsync(
        DateTime startLocal,
        DateTime endLocal,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(appConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = StoredProcedure(connection, "dbo.usp_ZkAttendanceInboxClaim");
            command.Parameters.Add(new SqlParameter("@StartLocal", SqlDbType.DateTime2) { Value = startLocal });
            command.Parameters.Add(new SqlParameter("@EndLocal", SqlDbType.DateTime2) { Value = endLocal });
            command.Parameters.Add(new SqlParameter("@BatchSize", SqlDbType.Int) { Value = sourceOptions.Value.StagingBatchSize });
            command.Parameters.Add(new SqlParameter("@LeaseMinutes", SqlDbType.Int) { Value = sourceOptions.Value.ProcessingLeaseMinutes });
            command.Parameters.Add(new SqlParameter("@MaxAttempts", SqlDbType.Int) { Value = sourceOptions.Value.MaxProcessingAttempts });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            Guid? leaseId = null;
            var sourceUsersCount = 0;
            if (await reader.ReadAsync(cancellationToken))
            {
                leaseId = reader.IsDBNull(reader.GetOrdinal("LeaseId")) ? null : reader.GetGuid(reader.GetOrdinal("LeaseId"));
                sourceUsersCount = reader.GetInt32(reader.GetOrdinal("SourceUsersCount"));
            }

            var punches = new List<AttendanceSourcePunch>();
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var sourceKey = (byte[])reader["SourceKey"];
                    punches.Add(new AttendanceSourcePunch(
                        SourceRecordId: reader.IsDBNull(reader.GetOrdinal("InboxId"))
                            ? null
                            : reader.GetInt64(reader.GetOrdinal("InboxId")),
                        UserId: reader.GetInt32(reader.GetOrdinal("SourceUserId")),
                        BadgeNumber: OptionalString(reader, "BadgeNumber"),
                        CheckTimeLocal: reader.GetDateTime(reader.GetOrdinal("SourceCheckTimeLocal")),
                        CheckType: OptionalString(reader, "SourceCheckType"),
                        SourceRawId: Convert.ToHexString(sourceKey)));
                }
            }

            var claimedCount = punches.Count(punch => punch.SourceRecordId.HasValue);
            logger.LogInformation(
                "ZKTime staging attendance rows claimed. claimedCount={ClaimedCount}, leaseId={LeaseId}, startLocal={StartLocal}, endLocal={EndLocal}",
                claimedCount,
                leaseId,
                startLocal,
                endLocal);

            return Result<AttendanceSourceBatch>.Success(new AttendanceSourceBatch(
                leaseId,
                sourceUsersCount,
                punches,
                SupportsAcknowledgement: true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to claim raw attendance records from the staging inbox.");
            return Result<AttendanceSourceBatch>.Failure(new Error(
                "StagingSourceError",
                "Unable to read the attendance staging inbox."));
        }
    }

    public async Task<Result> CompleteAsync(
        AttendanceSourceBatch batch,
        IReadOnlyCollection<SourceProcessingOutcome> outcomes,
        CancellationToken cancellationToken = default)
    {
        if (!batch.SupportsAcknowledgement || !batch.LeaseId.HasValue || outcomes.Count == 0)
        {
            return Result.Success();
        }

        return await CompleteAsync(
            "dbo.usp_ZkAttendanceInboxComplete",
            batch.LeaseId.Value,
            outcomes,
            cancellationToken);
    }

    public async Task<Result<DateOnly[]>> GetPendingProductionDatesAsync(
        TimeSpan workdayBoundaryTime,
        int maximumDates,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(appConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = StoredProcedure(connection, "dbo.usp_ZkAttendanceInboxPendingDates");
            command.Parameters.Add(new SqlParameter("@WorkdayBoundaryTime", SqlDbType.Time) { Value = workdayBoundaryTime });
            command.Parameters.Add(new SqlParameter("@MaximumDates", SqlDbType.Int) { Value = Math.Max(1, maximumDates) });
            command.Parameters.Add(new SqlParameter("@MaxAttempts", SqlDbType.Int) { Value = sourceOptions.Value.MaxProcessingAttempts });
            command.Parameters.Add(new SqlParameter("@LeaseMinutes", SqlDbType.Int) { Value = sourceOptions.Value.ProcessingLeaseMinutes });
            var dates = new List<DateOnly>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                dates.Add(DateOnly.FromDateTime(reader.GetDateTime(0)));
            }

            return Result<DateOnly[]>.Success(dates.ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to read pending attendance dates from the staging inbox.");
            return Result<DateOnly[]>.Failure(new Error(
                "StagingSourceError",
                "Unable to read pending attendance dates."));
        }
    }

    private async Task<Result<WorkerIdentitySourceBatch>> ReadWorkerBatchAsync(
        bool claim,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(appConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = StoredProcedure(connection, "dbo.usp_ZkWorkerInboxReadSnapshot");
            command.Parameters.Add(new SqlParameter("@Claim", SqlDbType.Bit) { Value = claim });
            command.Parameters.Add(new SqlParameter("@BatchSize", SqlDbType.Int) { Value = sourceOptions.Value.StagingBatchSize });
            command.Parameters.Add(new SqlParameter("@LeaseMinutes", SqlDbType.Int) { Value = sourceOptions.Value.ProcessingLeaseMinutes });
            command.Parameters.Add(new SqlParameter("@MaxAttempts", SqlDbType.Int) { Value = sourceOptions.Value.MaxProcessingAttempts });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            Guid? leaseId = null;
            if (await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(reader.GetOrdinal("LeaseId")))
            {
                leaseId = reader.GetGuid(reader.GetOrdinal("LeaseId"));
            }

            var items = new List<WorkerIdentitySourceItem>();
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var userId = reader.GetInt32(reader.GetOrdinal("SourceUserId"));
                    var badge = OptionalString(reader, "BadgeNumber");
                    int? sourceDefaultDepartmentId = reader.IsDBNull(reader.GetOrdinal("SourceDefaultDepartmentId"))
                        ? null
                        : reader.GetInt32(reader.GetOrdinal("SourceDefaultDepartmentId"));
                    var isCurrentWorker = reader.GetBoolean(reader.GetOrdinal("IsCurrentWorker"));
                    items.Add(new WorkerIdentitySourceItem(
                        SourceRecordId: reader.GetInt64(reader.GetOrdinal("InboxId")),
                        Worker: new AttendanceEmployeeRecord(
                            AttendanceUserId: userId.ToString(),
                            DepartmentId: sourceDefaultDepartmentId,
                            BadgeNumber: badge,
                            Name: OptionalString(reader, "SourceName"),
                            IsActive: isCurrentWorker,
                            EmployeeCode: badge?.Trim(),
                            SourceDefaultDepartmentId: sourceDefaultDepartmentId,
                            IsCurrentWorker: isCurrentWorker),
                        IsClaimed: reader.GetBoolean(reader.GetOrdinal("IsClaimed"))));
                }
            }

            return Result<WorkerIdentitySourceBatch>.Success(new WorkerIdentitySourceBatch(
                leaseId,
                items,
                SupportsAcknowledgement: true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to read worker identities from the staging inbox.");
            return Result<WorkerIdentitySourceBatch>.Failure(new Error(
                "StagingSourceError",
                "Unable to read the worker staging inbox."));
        }
    }

    private async Task<Result> CompleteAsync(
        string procedureName,
        Guid leaseId,
        IReadOnlyCollection<SourceProcessingOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        try
        {
            var table = new DataTable();
            table.Columns.Add("InboxId", typeof(long));
            table.Columns.Add("ResolutionStatus", typeof(string));
            table.Columns.Add("ResolutionCode", typeof(string));
            table.Columns.Add("ResolutionDetails", typeof(string));
            foreach (var outcome in outcomes)
            {
                table.Rows.Add(
                    outcome.SourceRecordId,
                    outcome.Disposition.ToString(),
                    Limit(outcome.ResolutionCode, 100),
                    Limit(outcome.ResolutionDetails, 1000));
            }

            await using var connection = new SqlConnection(appConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = StoredProcedure(connection, procedureName);
            command.Parameters.Add(new SqlParameter("@LeaseId", SqlDbType.UniqueIdentifier) { Value = leaseId });
            command.Parameters.Add(new SqlParameter("@MaxAttempts", SqlDbType.Int) { Value = sourceOptions.Value.MaxProcessingAttempts });
            command.Parameters.Add(new SqlParameter("@Results", SqlDbType.Structured)
            {
                TypeName = "dbo.ZkInboxResolutionResult",
                Value = table
            });
            await command.ExecuteNonQueryAsync(cancellationToken);
            var grouped = outcomes
                .GroupBy(outcome => outcome.Disposition)
                .ToDictionary(group => group.Key, group => group.Count());
            var topReasonCodes = outcomes
                .Where(outcome => !string.IsNullOrWhiteSpace(outcome.ResolutionCode))
                .GroupBy(outcome => outcome.ResolutionCode!, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Take(5)
                .Select(group => new { Code = group.Key, Count = group.Count() })
                .ToArray();
            logger.LogInformation(
                "ZKTime staging batch completed. inbox={InboxType}, leaseId={LeaseId}, completedProcessedCount={CompletedProcessedCount}, completedSkippedCount={CompletedSkippedCount}, completedPendingCount={CompletedPendingCount}, completedFailedCount={CompletedFailedCount}, reasonCodes={ReasonCodes}",
                procedureName.Contains("Worker", StringComparison.Ordinal) ? "Worker" : "Attendance",
                leaseId,
                grouped.GetValueOrDefault(SourceProcessingDisposition.Processed),
                grouped.GetValueOrDefault(SourceProcessingDisposition.Skipped),
                grouped.GetValueOrDefault(SourceProcessingDisposition.Pending),
                grouped.GetValueOrDefault(SourceProcessingDisposition.Failed),
                topReasonCodes);
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to acknowledge processed records in the staging inbox.");
            return Result.Failure(new Error(
                "StagingAcknowledgeFailed",
                "Processed staging records could not be acknowledged."));
        }
    }

    private SqlCommand StoredProcedure(SqlConnection connection, string name) => new(name, connection)
    {
        CommandType = CommandType.StoredProcedure,
        CommandTimeout = Math.Max(1, sourceOptions.Value.SyncReadCommandTimeoutSeconds)
    };

    private static string? OptionalString(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string? Limit(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maximumLength ? value : value[..maximumLength];
}
