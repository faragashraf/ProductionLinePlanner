using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

internal interface IProcessedAttendanceInboxStore
{
    Task<IReadOnlyList<ProcessedAttendanceInboxRow>> ReadProcessedAsync(
        DateTime fromLocal,
        DateTime toLocal,
        int? sourceUserId,
        string? badgeNumber,
        int maximumRows,
        CancellationToken cancellationToken);

    Task<ProcessedAttendanceInboxRow?> ReadForUpdateAsync(long inboxId, CancellationToken cancellationToken);
    Task<bool> RequeueAsync(ProcessedAttendanceInboxRow row, string details, CancellationToken cancellationToken);
    Task<bool> MarkAlreadyImportedAsync(ProcessedAttendanceInboxRow row, string details, CancellationToken cancellationToken);
    Task<ProcessedAttendanceInboxState?> ReadStateAsync(long inboxId, CancellationToken cancellationToken);
}

internal sealed record ProcessedAttendanceInboxRow(
    long InboxId,
    int SourceUserId,
    string? BadgeNumber,
    DateTime SourceCheckTimeLocal,
    string SourceCheckType,
    string SourceRawId,
    string ProcessingStatus,
    int AttemptCount,
    string? ResolutionCode,
    string? ResolutionDetails,
    byte[] RowVersion);

internal sealed record ProcessedAttendanceInboxState(
    long InboxId,
    string ProcessingStatus,
    string? ResolutionCode,
    string? ResolutionDetails);

internal sealed class SqlProcessedAttendanceInboxStore(
    AppDbContext dbContext,
    IOptions<AttendanceSourceOptions> sourceOptions) : IProcessedAttendanceInboxStore
{
    public Task<IReadOnlyList<ProcessedAttendanceInboxRow>> ReadProcessedAsync(
        DateTime fromLocal,
        DateTime toLocal,
        int? sourceUserId,
        string? badgeNumber,
        int maximumRows,
        CancellationToken cancellationToken) => ReadRowsAsync(
            """
            SELECT TOP (@MaximumRows)
                InboxId, SourceUserId, BadgeNumber, SourceCheckTimeLocal, SourceCheckType,
                CONVERT(varchar(64), SourceKey, 2) AS SourceRawId,
                ProcessingStatus, AttemptCount, ResolutionCode, ResolutionDetails, RowVersion
            FROM dbo.ZkAttendanceSyncInbox
            WHERE ProcessingStatus = 'Processed'
              AND ProcessedAtUtc IS NOT NULL
              AND SourceCheckTimeLocal >= @FromLocal
              AND SourceCheckTimeLocal < @ToLocal
              AND (@SourceUserId IS NULL OR SourceUserId = @SourceUserId)
              AND
              (
                  @BadgeNumber IS NULL
                  OR LTRIM(RTRIM(BadgeNumber)) = @BadgeNumber
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.Workers AS Worker
                      WHERE LTRIM(RTRIM(Worker.BadgeNumber)) = @BadgeNumber
                        AND
                        (
                            LTRIM(RTRIM(Worker.AttendanceUserId)) = CONVERT(nvarchar(20), dbo.ZkAttendanceSyncInbox.SourceUserId)
                            OR LTRIM(RTRIM(Worker.BadgeNumber)) = LTRIM(RTRIM(dbo.ZkAttendanceSyncInbox.BadgeNumber))
                        )
                  )
              )
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.Workers AS MappedWorker
                  WHERE MappedWorker.IsActive = 1
                    AND MappedWorker.EmploymentStatus = 1
                    AND
                    (
                        LTRIM(RTRIM(MappedWorker.AttendanceUserId)) = CONVERT(nvarchar(20), dbo.ZkAttendanceSyncInbox.SourceUserId)
                        OR LTRIM(RTRIM(MappedWorker.BadgeNumber)) = LTRIM(RTRIM(dbo.ZkAttendanceSyncInbox.BadgeNumber))
                    )
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.Workers AS EvidenceWorker
                  INNER JOIN dbo.AttendanceRecords AS Evidence
                      ON Evidence.WorkerId = EvidenceWorker.Id
                  WHERE EvidenceWorker.IsActive = 1
                    AND EvidenceWorker.EmploymentStatus = 1
                    AND
                    (
                        LTRIM(RTRIM(EvidenceWorker.AttendanceUserId)) = CONVERT(nvarchar(20), dbo.ZkAttendanceSyncInbox.SourceUserId)
                        OR LTRIM(RTRIM(EvidenceWorker.BadgeNumber)) = LTRIM(RTRIM(dbo.ZkAttendanceSyncInbox.BadgeNumber))
                    )
                    AND Evidence.Source = @SourceName
                    AND
                    (
                        (
                            UPPER(LTRIM(RTRIM(dbo.ZkAttendanceSyncInbox.SourceCheckType))) = N'I'
                            AND Evidence.SourceRawId = CONVERT(varchar(64), dbo.ZkAttendanceSyncInbox.SourceKey, 2)
                            AND TRY_CONVERT(datetime2(7), JSON_VALUE(Evidence.SourcePayload, N'$.FirstInUtc')) =
                                CONVERT(datetime2(7), dbo.ZkAttendanceSyncInbox.SourceCheckTimeLocal AT TIME ZONE 'Egypt Standard Time' AT TIME ZONE 'UTC')
                        )
                        OR
                        (
                            UPPER(LTRIM(RTRIM(dbo.ZkAttendanceSyncInbox.SourceCheckType))) = N'O'
                            AND TRY_CONVERT(datetime2(7), JSON_VALUE(Evidence.SourcePayload, N'$.LastOutUtc')) =
                                CONVERT(datetime2(7), dbo.ZkAttendanceSyncInbox.SourceCheckTimeLocal AT TIME ZONE 'Egypt Standard Time' AT TIME ZONE 'UTC')
                        )
                    )
              )
            ORDER BY SourceCheckTimeLocal, InboxId;
            """,
            command =>
            {
                command.Parameters.Add(new SqlParameter("@MaximumRows", SqlDbType.Int) { Value = maximumRows });
                command.Parameters.Add(new SqlParameter("@FromLocal", SqlDbType.DateTime2) { Value = fromLocal });
                command.Parameters.Add(new SqlParameter("@ToLocal", SqlDbType.DateTime2) { Value = toLocal });
                command.Parameters.Add(new SqlParameter("@SourceUserId", SqlDbType.Int) { Value = (object?)sourceUserId ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@BadgeNumber", SqlDbType.NVarChar, 120) { Value = (object?)badgeNumber ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@SourceName", SqlDbType.NVarChar, 120) { Value = sourceOptions.Value.SourceName });
            },
            cancellationToken);

    public async Task<ProcessedAttendanceInboxRow?> ReadForUpdateAsync(long inboxId, CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(
            """
            SELECT InboxId, SourceUserId, BadgeNumber, SourceCheckTimeLocal, SourceCheckType,
                CONVERT(varchar(64), SourceKey, 2) AS SourceRawId,
                ProcessingStatus, AttemptCount, ResolutionCode, ResolutionDetails, RowVersion
            FROM dbo.ZkAttendanceSyncInbox WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE InboxId = @InboxId;
            """,
            command => command.Parameters.Add(new SqlParameter("@InboxId", SqlDbType.BigInt) { Value = inboxId }),
            cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<bool> RequeueAsync(ProcessedAttendanceInboxRow row, string details, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE dbo.ZkAttendanceSyncInbox WITH (ROWLOCK)
            SET ProcessingStatus = 'Pending', AttemptCount = 0, LastError = NULL,
                ResolutionCode = N'ProcessedOrphanRequeued', ResolutionDetails = @Details,
                ResolvedAtUtc = NULL, ProcessingLeaseId = NULL, ProcessingStartedAtUtc = NULL,
                ProcessedAtUtc = NULL, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE InboxId = @InboxId AND ProcessingStatus = 'Processed' AND RowVersion = @RowVersion;
            """,
            row,
            details,
            cancellationToken);

    public Task<bool> MarkAlreadyImportedAsync(ProcessedAttendanceInboxRow row, string details, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE dbo.ZkAttendanceSyncInbox WITH (ROWLOCK)
            SET ResolutionCode = N'AlreadyImported', ResolutionDetails = @Details,
                ResolvedAtUtc = COALESCE(ResolvedAtUtc, SYSUTCDATETIME()), UpdatedAtUtc = SYSUTCDATETIME()
            WHERE InboxId = @InboxId AND ProcessingStatus = 'Processed' AND RowVersion = @RowVersion;
            """,
            row,
            details,
            cancellationToken);

    public async Task<ProcessedAttendanceInboxState?> ReadStateAsync(long inboxId, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(
                "SELECT InboxId, ProcessingStatus, ResolutionCode, ResolutionDetails FROM dbo.ZkAttendanceSyncInbox WHERE InboxId = @InboxId;",
                connection,
                CurrentTransaction());
            command.Parameters.Add(new SqlParameter("@InboxId", SqlDbType.BigInt) { Value = inboxId });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new ProcessedAttendanceInboxState(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3))
                : null;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private async Task<IReadOnlyList<ProcessedAttendanceInboxRow>> ReadRowsAsync(
        string sql,
        Action<SqlCommand> configure,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(sql, connection, CurrentTransaction());
            configure(command);
            var rows = new List<ProcessedAttendanceInboxRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ProcessedAttendanceInboxRow(
                    reader.GetInt64(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetDateTime(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    (byte[])reader[10]));
            }
            return rows;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private async Task<bool> ExecuteAsync(
        string sql,
        ProcessedAttendanceInboxRow row,
        string details,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(sql, connection, CurrentTransaction());
            command.Parameters.Add(new SqlParameter("@InboxId", SqlDbType.BigInt) { Value = row.InboxId });
            command.Parameters.Add(new SqlParameter("@RowVersion", SqlDbType.Timestamp) { Value = row.RowVersion });
            command.Parameters.Add(new SqlParameter("@Details", SqlDbType.NVarChar, 1000) { Value = details });
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private SqlTransaction? CurrentTransaction() =>
        dbContext.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
}
