SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

DECLARE @JobName sysname = N'Dayoub - ZKTime Staging Sync';
DECLARE @ExpectedVersion int = 2;

-- 1. Installed schema version. Dynamic SQL lets this script verify a not-yet-installed target safely.
IF OBJECT_ID(N'dbo.ZkSyncSchemaVersions', N'U') IS NULL
    SELECT DB_NAME() AS TargetDatabase, @ExpectedVersion AS ExpectedVersion, CAST(NULL AS int) AS InstalledVersion,
           CAST(NULL AS datetime2(7)) AS AppliedAtUtc, N'Not installed' AS Description;
ELSE
BEGIN
    DECLARE @VersionSql nvarchar(max) = N'SELECT DB_NAME() AS TargetDatabase, ' + CAST(@ExpectedVersion AS nvarchar(12)) + N' AS ExpectedVersion,
                 Version AS InstalledVersion, AppliedAtUtc, Description
          FROM dbo.ZkSyncSchemaVersions ORDER BY Version DESC;';
    EXEC sys.sp_executesql @VersionSql;
END;

-- 2. Required database objects.
DECLARE @Objects TABLE (ObjectType nvarchar(20), ObjectName sysname, IsInstalled bit);
INSERT @Objects (ObjectType, ObjectName, IsInstalled) VALUES
    (N'Table', N'dbo.ZkSyncSchemaVersions', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.ZkSyncSchemaVersions', N'U') IS NULL THEN 0 ELSE 1 END)),
    (N'Table', N'dbo.ZkSyncRuns', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.ZkSyncRuns', N'U') IS NULL THEN 0 ELSE 1 END)),
    (N'Table', N'dbo.ZkSyncState', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.ZkSyncState', N'U') IS NULL THEN 0 ELSE 1 END)),
    (N'Table', N'dbo.ZkWorkerSyncInbox', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NULL THEN 0 ELSE 1 END)),
    (N'Table', N'dbo.ZkAttendanceSyncInbox', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NULL THEN 0 ELSE 1 END)),
    (N'Column', N'dbo.ZkWorkerSyncInbox.SourceDefaultDepartmentId', CONVERT(bit, CASE WHEN COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'SourceDefaultDepartmentId') IS NULL THEN 0 ELSE 1 END)),
    (N'Column', N'dbo.ZkWorkerSyncInbox.IsCurrentWorker', CONVERT(bit, CASE WHEN COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'IsCurrentWorker') IS NULL THEN 0 ELSE 1 END)),
    (N'Type', N'dbo.ZkInboxProcessingResult', CONVERT(bit, CASE WHEN TYPE_ID(N'dbo.ZkInboxProcessingResult') IS NULL THEN 0 ELSE 1 END)),
    (N'Type', N'dbo.ZkInboxResolutionResult', CONVERT(bit, CASE WHEN TYPE_ID(N'dbo.ZkInboxResolutionResult') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkSyncRunStart', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkSyncRunStart', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkSyncRunRecordError', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkSyncRunRecordError', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkSyncRunComplete', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkSyncRunComplete', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkSyncCleanup', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkSyncCleanup', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkSyncDiagnostics', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkSyncDiagnostics', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkStageWorkers', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkStageWorkers', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkStageAttendance', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkStageAttendance', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkSyncExecuteManual', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkSyncExecuteManual', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkWorkerInboxReadSnapshot', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkWorkerInboxReadSnapshot', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkWorkerInboxComplete', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkWorkerInboxComplete', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkAttendanceInboxClaim', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkAttendanceInboxClaim', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkAttendanceInboxComplete', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkAttendanceInboxComplete', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkAttendanceInboxPendingDates', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkAttendanceInboxPendingDates', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkInboxRequeueFailed', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkInboxRequeueFailed', N'P') IS NULL THEN 0 ELSE 1 END)),
    (N'Procedure', N'dbo.usp_ZkInboxRequeueSkipped', CONVERT(bit, CASE WHEN OBJECT_ID(N'dbo.usp_ZkInboxRequeueSkipped', N'P') IS NULL THEN 0 ELSE 1 END));
SELECT ObjectType, ObjectName, IsInstalled FROM @Objects ORDER BY ObjectType, ObjectName;

-- 3-4. SQL Agent job and schedule. Metadata access failures remain explicit.
BEGIN TRY
    SELECT @JobName AS JobName, CONVERT(bit, CASE WHEN Job.job_id IS NULL THEN 0 ELSE 1 END) AS JobExists,
           Job.enabled AS JobEnabled, Job.date_modified AS JobLastModified
    FROM (VALUES (1)) AS Seed(Value)
    LEFT JOIN msdb.dbo.sysjobs AS Job ON Job.name = @JobName;

    SELECT Schedule.name AS ScheduleName, Schedule.enabled AS ScheduleEnabled,
           Schedule.freq_subday_interval AS EveryMinutes,
           JobSchedule.next_run_date AS NextRunDate, JobSchedule.next_run_time AS NextRunTime
    FROM msdb.dbo.sysjobs AS Job
    INNER JOIN msdb.dbo.sysjobschedules AS JobSchedule ON JobSchedule.job_id = Job.job_id
    INNER JOIN msdb.dbo.sysschedules AS Schedule ON Schedule.schedule_id = JobSchedule.schedule_id
    WHERE Job.name = @JobName;
END TRY
BEGIN CATCH
    SELECT @JobName AS JobName, CAST(NULL AS bit) AS JobExists, CAST(NULL AS bit) AS JobEnabled,
           N'SQL Agent metadata is unavailable to this principal.' AS VerificationNote;
    SELECT CAST(NULL AS sysname) AS ScheduleName, CAST(NULL AS bit) AS ScheduleEnabled,
           N'SQL Agent schedule metadata is unavailable to this principal.' AS VerificationNote;
END CATCH;

-- 5. Latest run, latest success, and latest failure.
IF OBJECT_ID(N'dbo.ZkSyncRuns', N'U') IS NULL
    SELECT N'No staging run history is installed.' AS VerificationNote;
ELSE
    EXEC(N';WITH Ranked AS
    (
        SELECT RunId, Status, StartedAtUtc, CompletedAtUtc, LastError,
               ROW_NUMBER() OVER (ORDER BY StartedAtUtc DESC) AS LatestRank,
               ROW_NUMBER() OVER (PARTITION BY Status ORDER BY StartedAtUtc DESC) AS StatusRank
        FROM dbo.ZkSyncRuns
    )
    SELECT CASE WHEN LatestRank = 1 THEN N''Latest'' WHEN Status = ''Succeeded'' THEN N''Latest success'' ELSE N''Latest failure'' END AS RunKind,
           RunId, Status, StartedAtUtc, CompletedAtUtc, LastError
    FROM Ranked
    WHERE LatestRank = 1 OR (Status IN (''Succeeded'', ''Failed'') AND StatusRank = 1)
    ORDER BY CASE WHEN LatestRank = 1 THEN 0 WHEN Status = ''Succeeded'' THEN 1 ELSE 2 END;');

-- 6. Counts by processing state.
CREATE TABLE #InboxCounts (InboxType varchar(16), ProcessingStatus varchar(16), RecordCount bigint);
IF OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NOT NULL
    INSERT #InboxCounts EXEC(N'SELECT ''Worker'', ProcessingStatus, COUNT_BIG(*) FROM dbo.ZkWorkerSyncInbox GROUP BY ProcessingStatus;');
IF OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NOT NULL
    INSERT #InboxCounts EXEC(N'SELECT ''Attendance'', ProcessingStatus, COUNT_BIG(*) FROM dbo.ZkAttendanceSyncInbox GROUP BY ProcessingStatus;');
;WITH InboxTypes(InboxType) AS (SELECT 'Worker' UNION ALL SELECT 'Attendance'),
Statuses(ProcessingStatus) AS (SELECT 'Pending' UNION ALL SELECT 'Processing' UNION ALL SELECT 'Processed' UNION ALL SELECT 'Skipped' UNION ALL SELECT 'Failed')
SELECT InboxTypes.InboxType, Statuses.ProcessingStatus, COALESCE(Counts.RecordCount, 0) AS RecordCount
FROM InboxTypes
CROSS JOIN Statuses
LEFT JOIN #InboxCounts AS Counts
    ON Counts.InboxType = InboxTypes.InboxType AND Counts.ProcessingStatus = Statuses.ProcessingStatus
ORDER BY InboxTypes.InboxType, Statuses.ProcessingStatus;

-- Worker current/non-worker classification counts contain no identity or profile data.
IF COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'IsCurrentWorker') IS NOT NULL
    EXEC(N'SELECT IsCurrentWorker, COUNT_BIG(*) AS WorkerCount, MAX(LastSeenAtUtc) AS LatestSeenAtUtc
          FROM dbo.ZkWorkerSyncInbox
          GROUP BY IsCurrentWorker
          ORDER BY IsCurrentWorker DESC;');

-- 7. Source freshness and evidence that the backend processor is draining the inboxes.
CREATE TABLE #InboxFreshness
(
    InboxType varchar(16), OldestPendingAtUtc datetime2(7), LatestSourceTimestamp datetime2(7),
    LatestProcessedAtUtc datetime2(7), LatestSkippedAtUtc datetime2(7), LatestFailedAtUtc datetime2(7),
    PendingCount bigint, ProcessingCount bigint, ProcessedCount bigint, SkippedCount bigint, FailedCount bigint
);
IF OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NOT NULL
    INSERT #InboxFreshness EXEC(N'SELECT ''Worker'',
        MIN(CASE WHEN ProcessingStatus = ''Pending'' THEN CreatedAtUtc END), MAX(LastSeenAtUtc), MAX(ProcessedAtUtc),
        MAX(CASE WHEN ProcessingStatus = ''Skipped'' THEN ResolvedAtUtc END), MAX(CASE WHEN ProcessingStatus = ''Failed'' THEN ResolvedAtUtc END),
        SUM(CASE WHEN ProcessingStatus = ''Pending'' THEN CONVERT(bigint, 1) ELSE 0 END),
        SUM(CASE WHEN ProcessingStatus = ''Processing'' THEN CONVERT(bigint, 1) ELSE 0 END),
        SUM(CASE WHEN ProcessingStatus = ''Processed'' THEN CONVERT(bigint, 1) ELSE 0 END),
        SUM(CASE WHEN ProcessingStatus = ''Skipped'' THEN CONVERT(bigint, 1) ELSE 0 END),
        SUM(CASE WHEN ProcessingStatus = ''Failed'' THEN CONVERT(bigint, 1) ELSE 0 END)
        FROM dbo.ZkWorkerSyncInbox;');
IF OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NOT NULL
    INSERT #InboxFreshness EXEC(N'SELECT ''Attendance'',
        MIN(CASE WHEN ProcessingStatus = ''Pending'' THEN InsertedAtUtc END), MAX(SourceCheckTimeLocal), MAX(ProcessedAtUtc),
        MAX(CASE WHEN ProcessingStatus = ''Skipped'' THEN ResolvedAtUtc END), MAX(CASE WHEN ProcessingStatus = ''Failed'' THEN ResolvedAtUtc END),
        SUM(CASE WHEN ProcessingStatus = ''Pending'' THEN CONVERT(bigint, 1) ELSE 0 END),
        SUM(CASE WHEN ProcessingStatus = ''Processing'' THEN CONVERT(bigint, 1) ELSE 0 END),
        SUM(CASE WHEN ProcessingStatus = ''Processed'' THEN CONVERT(bigint, 1) ELSE 0 END),
        SUM(CASE WHEN ProcessingStatus = ''Skipped'' THEN CONVERT(bigint, 1) ELSE 0 END),
        SUM(CASE WHEN ProcessingStatus = ''Failed'' THEN CONVERT(bigint, 1) ELSE 0 END)
        FROM dbo.ZkAttendanceSyncInbox;');
SELECT InboxType, OldestPendingAtUtc, LatestSourceTimestamp, LatestProcessedAtUtc, LatestSkippedAtUtc, LatestFailedAtUtc,
       COALESCE(PendingCount, 0) AS PendingCount, COALESCE(ProcessingCount, 0) AS ProcessingCount,
       COALESCE(ProcessedCount, 0) AS ProcessedCount, COALESCE(SkippedCount, 0) AS SkippedCount, COALESCE(FailedCount, 0) AS FailedCount,
       CASE
           WHEN FailedCount > 0 THEN N'Failures require review'
           WHEN PendingCount > 0 AND LatestProcessedAtUtc IS NULL AND LatestSkippedAtUtc IS NULL THEN N'No backend processing observed'
           WHEN PendingCount > 0 OR ProcessingCount > 0 THEN N'Backlog pending'
           WHEN SkippedCount > 0 THEN N'Completed with skipped records'
           WHEN ProcessedCount > 0 THEN N'Healthy'
           ELSE N'No backend processing observed'
       END AS BackendProcessorObservation
FROM #InboxFreshness ORDER BY InboxType;

-- 8. Resolution reasons are diagnostic metadata, distinct from technical LastError.
IF OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NOT NULL
    EXEC(N'SELECT ''Worker'' AS InboxType, ResolutionCode, COUNT_BIG(*) AS RecordCount, MAX(ResolvedAtUtc) AS LatestOccurrenceAtUtc
          FROM dbo.ZkWorkerSyncInbox
          WHERE ProcessingStatus = ''Skipped''
          GROUP BY ResolutionCode
          ORDER BY RecordCount DESC, ResolutionCode;');
IF OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NOT NULL
    EXEC(N'SELECT ''Attendance'' AS InboxType, ResolutionCode, COUNT_BIG(*) AS RecordCount, MAX(ResolvedAtUtc) AS LatestOccurrenceAtUtc
          FROM dbo.ZkAttendanceSyncInbox
          WHERE ProcessingStatus = ''Skipped''
          GROUP BY ResolutionCode
          ORDER BY RecordCount DESC, ResolutionCode;');
IF OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NOT NULL
    EXEC(N'SELECT ''Worker'' AS InboxType, COALESCE(ResolutionCode, LastError, ''UnclassifiedFailure'') AS ResolutionCode, COUNT_BIG(*) AS RecordCount, MAX(ResolvedAtUtc) AS LatestOccurrenceAtUtc
          FROM dbo.ZkWorkerSyncInbox
          WHERE ProcessingStatus = ''Failed''
          GROUP BY COALESCE(ResolutionCode, LastError, ''UnclassifiedFailure'')
          ORDER BY RecordCount DESC, ResolutionCode;');
IF OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NOT NULL
    EXEC(N'SELECT ''Attendance'' AS InboxType, COALESCE(ResolutionCode, LastError, ''UnclassifiedFailure'') AS ResolutionCode, COUNT_BIG(*) AS RecordCount, MAX(ResolvedAtUtc) AS LatestOccurrenceAtUtc
          FROM dbo.ZkAttendanceSyncInbox
          WHERE ProcessingStatus = ''Failed''
          GROUP BY COALESCE(ResolutionCode, LastError, ''UnclassifiedFailure'')
          ORDER BY RecordCount DESC, ResolutionCode;');
