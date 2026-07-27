SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.usp_ZkSyncRunStart', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkSyncRunStart AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkSyncRunStart
    @TriggerType nvarchar(30),
    @RunId uniqueidentifier OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @RunId = NEWID();
    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();
    DECLARE @LockResult int;
    DECLARE @ExistingRunId uniqueidentifier;

    BEGIN TRANSACTION;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'Dayoub.ZkTime.Staging.Run',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 0;
    IF @LockResult < 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51001, 'A ZKTime staging run is already starting.', 1;
    END;

    SELECT @ExistingRunId = ActiveRunId
    FROM dbo.ZkSyncState WITH (UPDLOCK, HOLDLOCK)
    WHERE StateId = 1;

    IF @ExistingRunId IS NOT NULL
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM dbo.ZkSyncRuns
            WHERE RunId = @ExistingRunId
              AND Status = 'Running'
              AND StartedAtUtc >= DATEADD(MINUTE, -30, @NowUtc)
        )
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 51002, 'A ZKTime staging run is already active.', 1;
        END;

        UPDATE dbo.ZkSyncRuns
        SET Status = 'Failed',
            CompletedAtUtc = COALESCE(CompletedAtUtc, @NowUtc),
            LastError = COALESCE(LastError, N'Stale run recovered before a new staging run.'),
            UpdatedAtUtc = @NowUtc
        WHERE RunId = @ExistingRunId
          AND Status = 'Running';
    END;

    INSERT dbo.ZkSyncRuns
    (
        RunId, TriggerType, Status, StartedAtUtc, CreatedAtUtc, UpdatedAtUtc
    )
    VALUES
    (
        @RunId, LEFT(COALESCE(NULLIF(LTRIM(RTRIM(@TriggerType)), N''), N'Unknown'), 30),
        'Running', @NowUtc, @NowUtc, @NowUtc
    );

    UPDATE dbo.ZkSyncState
    SET ActiveRunId = @RunId,
        UpdatedAtUtc = @NowUtc
    WHERE StateId = 1;

    COMMIT TRANSACTION;
    SELECT @RunId AS RunId;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkSyncRunRecordError', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkSyncRunRecordError AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkSyncRunRecordError
    @RunId uniqueidentifier = NULL,
    @ErrorMessage nvarchar(2000)
AS
BEGIN
    SET NOCOUNT ON;
    IF @RunId IS NULL
        SELECT @RunId = ActiveRunId FROM dbo.ZkSyncState WHERE StateId = 1;

    UPDATE dbo.ZkSyncRuns
    SET Status = 'Failed',
        LastError = LEFT(COALESCE(NULLIF(@ErrorMessage, N''), N'Unknown staging failure.'), 2000),
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE RunId = @RunId;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkSyncRunComplete', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkSyncRunComplete AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkSyncRunComplete
    @Succeeded bit,
    @RunId uniqueidentifier = NULL,
    @ErrorMessage nvarchar(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();

    BEGIN TRANSACTION;
    IF @RunId IS NULL
        SELECT @RunId = ActiveRunId FROM dbo.ZkSyncState WITH (UPDLOCK, HOLDLOCK) WHERE StateId = 1;

    IF @RunId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51003, 'No active ZKTime staging run was found.', 1;
    END;

    UPDATE dbo.ZkSyncRuns
    SET Status = CASE WHEN @Succeeded = 1 THEN 'Succeeded' ELSE 'Failed' END,
        CompletedAtUtc = @NowUtc,
        LastError = CASE
            WHEN @Succeeded = 1 THEN LastError
            ELSE COALESCE(LastError, LEFT(COALESCE(NULLIF(@ErrorMessage, N''), N'ZKTime staging run failed.'), 2000))
        END,
        UpdatedAtUtc = @NowUtc
    WHERE RunId = @RunId;

    UPDATE dbo.ZkSyncState
    SET ActiveRunId = NULL,
        UpdatedAtUtc = @NowUtc
    WHERE StateId = 1 AND ActiveRunId = @RunId;
    COMMIT TRANSACTION;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkSyncCleanup', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkSyncCleanup AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkSyncCleanup
    @ProcessedAttendanceRetentionDays int = 90,
    @RunHistoryRetentionDays int = 180,
    @BatchSize int = 5000
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @ProcessedAttendanceRetentionDays < 30 OR @RunHistoryRetentionDays < 30 OR @BatchSize < 1
        THROW 51004, 'Retention must be at least 30 days and batch size must be positive.', 1;

    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();
    DELETE TOP (@BatchSize)
    FROM dbo.ZkAttendanceSyncInbox WITH (ROWLOCK, READPAST)
    WHERE ProcessingStatus = 'Processed'
      AND ProcessedAtUtc < DATEADD(DAY, -@ProcessedAttendanceRetentionDays, @NowUtc);

    DELETE TOP (@BatchSize)
    FROM dbo.ZkSyncRuns WITH (ROWLOCK, READPAST)
    WHERE Status IN ('Succeeded', 'Failed')
      AND CompletedAtUtc < DATEADD(DAY, -@RunHistoryRetentionDays, @NowUtc)
      AND RunId <> COALESCE((SELECT ActiveRunId FROM dbo.ZkSyncState WHERE StateId = 1), '00000000-0000-0000-0000-000000000000');
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkSyncDiagnostics', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkSyncDiagnostics AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkSyncDiagnostics
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1)
        RunId, TriggerType, Status, StartedAtUtc, CompletedAtUtc,
        WorkersDiscovered, WorkersInserted, WorkersChanged,
        PunchesDiscovered, PunchesInserted, LastError
    FROM dbo.ZkSyncRuns
    ORDER BY StartedAtUtc DESC;

    ;WITH Statuses(ProcessingStatus) AS
    (
        SELECT 'Pending' UNION ALL SELECT 'Processing' UNION ALL SELECT 'Processed' UNION ALL SELECT 'Skipped' UNION ALL SELECT 'Failed'
    )
    SELECT
        Statuses.ProcessingStatus,
        COALESCE(Counts.RecordCount, 0) AS WorkerCount
    FROM Statuses
    LEFT JOIN
    (
        SELECT ProcessingStatus, COUNT_BIG(*) AS RecordCount
        FROM dbo.ZkWorkerSyncInbox
        GROUP BY ProcessingStatus
    ) AS Counts ON Counts.ProcessingStatus = Statuses.ProcessingStatus
    ORDER BY Statuses.ProcessingStatus;

    ;WITH Statuses(ProcessingStatus) AS
    (
        SELECT 'Pending' UNION ALL SELECT 'Processing' UNION ALL SELECT 'Processed' UNION ALL SELECT 'Skipped' UNION ALL SELECT 'Failed'
    )
    SELECT
        Statuses.ProcessingStatus,
        COALESCE(Counts.RecordCount, 0) AS AttendanceCount,
        Counts.OldestSourceCheckTimeLocal,
        Counts.NewestSourceCheckTimeLocal
    FROM Statuses
    LEFT JOIN
    (
        SELECT ProcessingStatus, COUNT_BIG(*) AS RecordCount,
               MIN(SourceCheckTimeLocal) AS OldestSourceCheckTimeLocal,
               MAX(SourceCheckTimeLocal) AS NewestSourceCheckTimeLocal
        FROM dbo.ZkAttendanceSyncInbox
        GROUP BY ProcessingStatus
    ) AS Counts ON Counts.ProcessingStatus = Statuses.ProcessingStatus
    ORDER BY Statuses.ProcessingStatus;

    SELECT N'Worker' AS InboxType,
           MAX(ProcessedAtUtc) AS LatestProcessedAtUtc,
           MAX(CASE WHEN ProcessingStatus = 'Skipped' THEN ResolvedAtUtc END) AS LatestSkippedAtUtc,
           MAX(CASE WHEN ProcessingStatus = 'Failed' THEN ResolvedAtUtc END) AS LatestFailedAtUtc
    FROM dbo.ZkWorkerSyncInbox
    UNION ALL
    SELECT N'Attendance', MAX(ProcessedAtUtc),
           MAX(CASE WHEN ProcessingStatus = 'Skipped' THEN ResolvedAtUtc END),
           MAX(CASE WHEN ProcessingStatus = 'Failed' THEN ResolvedAtUtc END)
    FROM dbo.ZkAttendanceSyncInbox;

    ;WITH InboxHealth AS
    (
        SELECT N'Worker' AS InboxType,
               SUM(CASE WHEN ProcessingStatus = 'Pending' THEN CONVERT(bigint, 1) ELSE 0 END) AS PendingCount,
               SUM(CASE WHEN ProcessingStatus = 'Processing' THEN CONVERT(bigint, 1) ELSE 0 END) AS ProcessingCount,
               SUM(CASE WHEN ProcessingStatus = 'Processed' THEN CONVERT(bigint, 1) ELSE 0 END) AS ProcessedCount,
               SUM(CASE WHEN ProcessingStatus = 'Skipped' THEN CONVERT(bigint, 1) ELSE 0 END) AS SkippedCount,
               SUM(CASE WHEN ProcessingStatus = 'Failed' THEN CONVERT(bigint, 1) ELSE 0 END) AS FailedCount,
               MAX(ProcessedAtUtc) AS LatestProcessedAtUtc,
               MAX(CASE WHEN ProcessingStatus = 'Skipped' THEN ResolvedAtUtc END) AS LatestSkippedAtUtc
        FROM dbo.ZkWorkerSyncInbox
        UNION ALL
        SELECT N'Attendance',
               SUM(CASE WHEN ProcessingStatus = 'Pending' THEN CONVERT(bigint, 1) ELSE 0 END),
               SUM(CASE WHEN ProcessingStatus = 'Processing' THEN CONVERT(bigint, 1) ELSE 0 END),
               SUM(CASE WHEN ProcessingStatus = 'Processed' THEN CONVERT(bigint, 1) ELSE 0 END),
               SUM(CASE WHEN ProcessingStatus = 'Skipped' THEN CONVERT(bigint, 1) ELSE 0 END),
               SUM(CASE WHEN ProcessingStatus = 'Failed' THEN CONVERT(bigint, 1) ELSE 0 END),
               MAX(ProcessedAtUtc),
               MAX(CASE WHEN ProcessingStatus = 'Skipped' THEN ResolvedAtUtc END)
        FROM dbo.ZkAttendanceSyncInbox
    )
    SELECT InboxType, PendingCount, ProcessingCount, ProcessedCount, SkippedCount, FailedCount,
           CASE
               WHEN FailedCount > 0 THEN N'Failures require review'
               WHEN PendingCount > 0 AND LatestProcessedAtUtc IS NULL AND LatestSkippedAtUtc IS NULL THEN N'No backend processing observed'
               WHEN PendingCount > 0 OR ProcessingCount > 0 THEN N'Backlog pending'
               WHEN SkippedCount > 0 THEN N'Completed with skipped records'
               WHEN ProcessedCount > 0 THEN N'Healthy'
               ELSE N'No backend processing observed'
           END AS BackendProcessorObservation
    FROM InboxHealth
    ORDER BY InboxType;

    SELECT N'Worker' AS InboxType, ResolutionCode, COUNT_BIG(*) AS RecordCount, MAX(ResolvedAtUtc) AS LatestOccurrenceAtUtc
    FROM dbo.ZkWorkerSyncInbox WHERE ProcessingStatus = 'Skipped'
    GROUP BY ResolutionCode
    UNION ALL
    SELECT N'Attendance', ResolutionCode, COUNT_BIG(*), MAX(ResolvedAtUtc)
    FROM dbo.ZkAttendanceSyncInbox WHERE ProcessingStatus = 'Skipped'
    GROUP BY ResolutionCode
    ORDER BY InboxType, RecordCount DESC;

    SELECT TOP (20)
        N'Worker' AS InboxType, InboxId, SourceUserId, AttemptCount, ResolutionCode, LastError, UpdatedAtUtc
    FROM dbo.ZkWorkerSyncInbox
    WHERE ProcessingStatus = 'Failed'
    UNION ALL
    SELECT TOP (20)
        N'Attendance', InboxId, SourceUserId, AttemptCount, ResolutionCode, LastError, UpdatedAtUtc
    FROM dbo.ZkAttendanceSyncInbox
    WHERE ProcessingStatus = 'Failed'
    ORDER BY UpdatedAtUtc DESC;
END;
GO
