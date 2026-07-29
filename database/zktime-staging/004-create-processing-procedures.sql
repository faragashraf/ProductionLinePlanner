SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.usp_ZkWorkerInboxReadSnapshot', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkWorkerInboxReadSnapshot AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkWorkerInboxReadSnapshot
    @Claim bit = 0,
    @BatchSize int = 2000,
    @LeaseMinutes int = 15,
    @MaxAttempts int = 5
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @BatchSize < 1 OR @BatchSize > 10000 THROW 51201, 'BatchSize must be between 1 and 10000.', 1;
    IF @LeaseMinutes < 1 OR @LeaseMinutes > 120 THROW 51202, 'LeaseMinutes must be between 1 and 120.', 1;
    IF @MaxAttempts < 1 OR @MaxAttempts > 100 THROW 51203, 'MaxAttempts must be between 1 and 100.', 1;

    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();
    DECLARE @LeaseId uniqueidentifier = CASE WHEN @Claim = 1 THEN NEWID() ELSE NULL END;
    DECLARE @Claimed TABLE (InboxId bigint NOT NULL PRIMARY KEY);

    IF @Claim = 1
    BEGIN
        BEGIN TRANSACTION;
        DECLARE @WorkerLockResult int;
        EXEC @WorkerLockResult = sys.sp_getapplock
            @Resource = N'Dayoub.ZkTime.WorkerInboxClaim',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @WorkerLockResult < 0
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 51204, 'Another worker staging claim is being issued.', 1;
        END;
        IF EXISTS
        (
            SELECT 1
            FROM dbo.ZkWorkerSyncInbox WITH (UPDLOCK, HOLDLOCK)
            WHERE ProcessingStatus = 'Processing'
              AND ProcessingStartedAtUtc >= DATEADD(MINUTE, -@LeaseMinutes, @NowUtc)
        )
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 51205, 'A worker staging lease is already active.', 1;
        END;

        ;WITH Candidates AS
        (
            SELECT TOP (@BatchSize) InboxId
            FROM dbo.ZkWorkerSyncInbox WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE
                (
                    ProcessingStatus = 'Pending'
                    OR
                    (
                        ProcessingStatus = 'Processing'
                        AND ProcessingStartedAtUtc < DATEADD(MINUTE, -@LeaseMinutes, @NowUtc)
                    )
                )
                AND AttemptCount < @MaxAttempts
            ORDER BY InboxId
        )
        UPDATE Inbox
        SET ProcessingStatus = 'Processing',
            AttemptCount = AttemptCount + 1,
            LastError = NULL,
            ProcessingLeaseId = @LeaseId,
            ProcessingStartedAtUtc = @NowUtc,
            UpdatedAtUtc = @NowUtc
        OUTPUT inserted.InboxId INTO @Claimed (InboxId)
        FROM dbo.ZkWorkerSyncInbox AS Inbox
        INNER JOIN Candidates ON Candidates.InboxId = Inbox.InboxId;
        COMMIT TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM @Claimed) SET @LeaseId = NULL;
    END;

    SELECT @LeaseId AS LeaseId;
    SELECT
        Inbox.InboxId,
        Inbox.SourceUserId,
        Inbox.BadgeNumber,
        Inbox.SourceName,
        Inbox.SourceDefaultDepartmentId,
        Inbox.IsCurrentWorker,
        CONVERT(bit, CASE WHEN Claimed.InboxId IS NULL THEN 0 ELSE 1 END) AS IsClaimed
    FROM dbo.ZkWorkerSyncInbox AS Inbox
    LEFT JOIN @Claimed AS Claimed ON Claimed.InboxId = Inbox.InboxId
    ORDER BY Inbox.SourceUserId;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkWorkerInboxComplete', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkWorkerInboxComplete AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkWorkerInboxComplete
    @LeaseId uniqueidentifier,
    @MaxAttempts int = 5,
    @Results dbo.ZkInboxResolutionResult READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @MaxAttempts < 1 OR @MaxAttempts > 100 THROW 51206, 'MaxAttempts must be between 1 and 100.', 1;
    IF EXISTS (SELECT 1 FROM @Results WHERE ResolutionStatus NOT IN ('Pending', 'Processed', 'Skipped', 'Failed'))
        THROW 51207, 'ResolutionStatus must be Pending, Processed, Skipped, or Failed.', 1;
    IF EXISTS (SELECT 1 FROM @Results WHERE ResolutionStatus IN ('Pending', 'Skipped', 'Failed') AND NULLIF(LTRIM(RTRIM(ResolutionCode)), N'') IS NULL)
        THROW 51208, 'Pending, Skipped, and Failed resolutions require ResolutionCode.', 1;
    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();

    UPDATE Inbox WITH (ROWLOCK)
    SET ProcessingStatus = CASE
            WHEN Result.ResolutionStatus = 'Pending' AND Inbox.AttemptCount >= @MaxAttempts THEN 'Failed'
            ELSE Result.ResolutionStatus
        END,
        LastError = CASE
            WHEN Result.ResolutionStatus = 'Failed'
                 OR (Result.ResolutionStatus = 'Pending' AND Inbox.AttemptCount >= @MaxAttempts)
                THEN LEFT(COALESCE(Result.ResolutionDetails, Result.ResolutionCode, N'ProcessingFailed'), 1000)
            ELSE NULL
        END,
        ResolutionCode = NULLIF(LTRIM(RTRIM(Result.ResolutionCode)), N''),
        ResolutionDetails = NULLIF(LTRIM(RTRIM(Result.ResolutionDetails)), N''),
        ResolvedAtUtc = CASE WHEN Result.ResolutionStatus = 'Pending' AND Inbox.AttemptCount < @MaxAttempts THEN NULL ELSE @NowUtc END,
        ProcessedAtUtc = CASE WHEN Result.ResolutionStatus = 'Processed' THEN @NowUtc ELSE NULL END,
        ProcessingLeaseId = NULL,
        ProcessingStartedAtUtc = NULL,
        UpdatedAtUtc = @NowUtc
    FROM dbo.ZkWorkerSyncInbox AS Inbox
    INNER JOIN @Results AS Result ON Result.InboxId = Inbox.InboxId
    WHERE Inbox.ProcessingStatus = 'Processing'
      AND Inbox.ProcessingLeaseId = @LeaseId;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkAttendanceInboxClaim', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkAttendanceInboxClaim AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkAttendanceInboxClaim
    @StartLocal datetime2(7),
    @EndLocal datetime2(7),
    @BatchSize int = 2000,
    @LeaseMinutes int = 15,
    @MaxAttempts int = 5
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @EndLocal <= @StartLocal THROW 51211, 'EndLocal must be later than StartLocal.', 1;
    IF @BatchSize < 1 OR @BatchSize > 10000 THROW 51212, 'BatchSize must be between 1 and 10000.', 1;
    IF @LeaseMinutes < 1 OR @LeaseMinutes > 120 THROW 51213, 'LeaseMinutes must be between 1 and 120.', 1;
    IF @MaxAttempts < 1 OR @MaxAttempts > 100 THROW 51214, 'MaxAttempts must be between 1 and 100.', 1;

    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();
    DECLARE @LeaseId uniqueidentifier = NEWID();
    DECLARE @Claimed TABLE (InboxId bigint NOT NULL PRIMARY KEY);
    DECLARE @AttendanceLockResource nvarchar(255) = CONCAT(
        N'Dayoub.ZkTime.AttendanceInboxClaim.',
        CONVERT(nvarchar(33), @StartLocal, 126), N'.', CONVERT(nvarchar(33), @EndLocal, 126));

    BEGIN TRANSACTION;
    DECLARE @AttendanceLockResult int;
    EXEC @AttendanceLockResult = sys.sp_getapplock
        @Resource = @AttendanceLockResource,
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 0;
    IF @AttendanceLockResult < 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51215, 'Another attendance staging claim is being issued for this range.', 1;
    END;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.ZkAttendanceSyncInbox WITH (UPDLOCK, HOLDLOCK)
        WHERE SourceCheckTimeLocal >= @StartLocal
          AND SourceCheckTimeLocal < @EndLocal
          AND ProcessingStatus = 'Processing'
          AND ProcessingStartedAtUtc >= DATEADD(MINUTE, -@LeaseMinutes, @NowUtc)
    )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51216, 'An attendance staging lease is already active for this range.', 1;
    END;

    ;WITH Candidates AS
    (
        SELECT TOP (@BatchSize) InboxId
        FROM dbo.ZkAttendanceSyncInbox WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE SourceCheckTimeLocal >= @StartLocal
          AND SourceCheckTimeLocal < @EndLocal
          AND
          (
              ProcessingStatus = 'Pending'
              OR
              (
                  ProcessingStatus = 'Processing'
                  AND ProcessingStartedAtUtc < DATEADD(MINUTE, -@LeaseMinutes, @NowUtc)
              )
          )
          AND AttemptCount < @MaxAttempts
        ORDER BY SourceCheckTimeLocal, InboxId
    )
    UPDATE Inbox
    SET ProcessingStatus = 'Processing',
        AttemptCount = AttemptCount + 1,
        LastError = NULL,
        ProcessingLeaseId = @LeaseId,
        ProcessingStartedAtUtc = @NowUtc,
        UpdatedAtUtc = @NowUtc
    OUTPUT inserted.InboxId INTO @Claimed (InboxId)
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    INNER JOIN Candidates ON Candidates.InboxId = Inbox.InboxId;
    COMMIT TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM @Claimed) SET @LeaseId = NULL;
    SELECT
        @LeaseId AS LeaseId,
        CONVERT(int, COUNT(DISTINCT Inbox.SourceUserId)) AS SourceUsersCount
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    WHERE Inbox.SourceCheckTimeLocal >= @StartLocal
      AND Inbox.SourceCheckTimeLocal < @EndLocal;

    SELECT
        Claimed.InboxId,
        Inbox.SourceUserId,
        Inbox.BadgeNumber,
        Inbox.SourceCheckTimeLocal,
        Inbox.SourceCheckType,
        Inbox.SourceKey
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    LEFT JOIN @Claimed AS Claimed ON Claimed.InboxId = Inbox.InboxId
    WHERE Inbox.SourceCheckTimeLocal >= @StartLocal
      AND Inbox.SourceCheckTimeLocal < @EndLocal
    ORDER BY Inbox.SourceCheckTimeLocal, Inbox.InboxId;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkAttendanceInboxComplete', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkAttendanceInboxComplete AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkAttendanceInboxComplete
    @LeaseId uniqueidentifier,
    @MaxAttempts int = 5,
    @Results dbo.ZkInboxResolutionResult READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @MaxAttempts < 1 OR @MaxAttempts > 100 THROW 51217, 'MaxAttempts must be between 1 and 100.', 1;
    IF EXISTS (SELECT 1 FROM @Results WHERE ResolutionStatus NOT IN ('Pending', 'Processed', 'Skipped', 'Failed'))
        THROW 51218, 'ResolutionStatus must be Pending, Processed, Skipped, or Failed.', 1;
    IF EXISTS (SELECT 1 FROM @Results WHERE ResolutionStatus IN ('Pending', 'Skipped', 'Failed') AND NULLIF(LTRIM(RTRIM(ResolutionCode)), N'') IS NULL)
        THROW 51219, 'Pending, Skipped, and Failed resolutions require ResolutionCode.', 1;
    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();

    UPDATE Inbox WITH (ROWLOCK)
    SET ProcessingStatus = CASE
            WHEN Result.ResolutionStatus = 'Pending' AND Inbox.AttemptCount >= @MaxAttempts THEN 'Failed'
            ELSE Result.ResolutionStatus
        END,
        LastError = CASE
            WHEN Result.ResolutionStatus = 'Failed'
                 OR (Result.ResolutionStatus = 'Pending' AND Inbox.AttemptCount >= @MaxAttempts)
                THEN LEFT(COALESCE(Result.ResolutionDetails, Result.ResolutionCode, N'ProcessingFailed'), 1000)
            ELSE NULL
        END,
        ResolutionCode = NULLIF(LTRIM(RTRIM(Result.ResolutionCode)), N''),
        ResolutionDetails = NULLIF(LTRIM(RTRIM(Result.ResolutionDetails)), N''),
        ResolvedAtUtc = CASE WHEN Result.ResolutionStatus = 'Pending' AND Inbox.AttemptCount < @MaxAttempts THEN NULL ELSE @NowUtc END,
        ProcessedAtUtc = CASE WHEN Result.ResolutionStatus = 'Processed' THEN @NowUtc ELSE NULL END,
        ProcessingLeaseId = NULL,
        ProcessingStartedAtUtc = NULL,
        UpdatedAtUtc = @NowUtc
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    INNER JOIN @Results AS Result ON Result.InboxId = Inbox.InboxId
    WHERE Inbox.ProcessingStatus = 'Processing'
      AND Inbox.ProcessingLeaseId = @LeaseId;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkAttendanceInboxPendingDates', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkAttendanceInboxPendingDates AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkAttendanceInboxPendingDates
    @WorkdayBoundaryTime time(7),
    @MaximumDates int = 3,
    @MaxAttempts int = 5,
    @LeaseMinutes int = 15
AS
BEGIN
    SET NOCOUNT ON;
    IF @MaximumDates < 1 OR @MaximumDates > 31 THROW 51221, 'MaximumDates must be between 1 and 31.', 1;
    IF @MaxAttempts < 1 OR @MaxAttempts > 100 THROW 51222, 'MaxAttempts must be between 1 and 100.', 1;
    IF @LeaseMinutes < 1 OR @LeaseMinutes > 120 THROW 51223, 'LeaseMinutes must be between 1 and 120.', 1;

    -- Shift source-local punches by the configured boundary before deriving the
    -- operational date. AttendanceSyncService uses the same boundary for its claim window.
    SELECT TOP (@MaximumDates)
        CONVERT(date, DATEADD(SECOND, -DATEDIFF(SECOND, CONVERT(time(7), '00:00:00'), @WorkdayBoundaryTime), SourceCheckTimeLocal)) AS ProductionDate
    FROM dbo.ZkAttendanceSyncInbox
    WHERE
        (
            ProcessingStatus = 'Pending'
            OR (ProcessingStatus = 'Processing' AND ProcessingStartedAtUtc < DATEADD(MINUTE, -@LeaseMinutes, SYSUTCDATETIME()))
        )
      AND AttemptCount < @MaxAttempts
    GROUP BY CONVERT(date, DATEADD(SECOND, -DATEDIFF(SECOND, CONVERT(time(7), '00:00:00'), @WorkdayBoundaryTime), SourceCheckTimeLocal))
    ORDER BY ProductionDate;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkInboxRequeueFailed', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkInboxRequeueFailed AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkInboxRequeueFailed
    @InboxType varchar(16),
    @SourceUserId int = NULL,
    @ReasonCode nvarchar(100) = NULL,
    @MaximumRows int = 1000
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @InboxType NOT IN ('Worker', 'Attendance') THROW 51231, 'InboxType must be Worker or Attendance.', 1;
    IF @MaximumRows < 1 OR @MaximumRows > 10000 THROW 51232, 'MaximumRows must be between 1 and 10000.', 1;
    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();

    IF @InboxType = 'Worker'
    BEGIN
        ;WITH RowsToRequeue AS
        (
            SELECT TOP (@MaximumRows) InboxId
            FROM dbo.ZkWorkerSyncInbox WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE ProcessingStatus = 'Failed'
              AND (@SourceUserId IS NULL OR SourceUserId = @SourceUserId)
              AND (@ReasonCode IS NULL OR ResolutionCode = @ReasonCode)
            ORDER BY InboxId
        )
        UPDATE Inbox
        SET ProcessingStatus = 'Pending', AttemptCount = 0, LastError = NULL,
            ResolutionCode = NULL, ResolutionDetails = NULL, ResolvedAtUtc = NULL,
            ProcessingLeaseId = NULL, ProcessingStartedAtUtc = NULL, ProcessedAtUtc = NULL,
            UpdatedAtUtc = @NowUtc
        FROM dbo.ZkWorkerSyncInbox AS Inbox
        INNER JOIN RowsToRequeue ON RowsToRequeue.InboxId = Inbox.InboxId;
        SELECT @@ROWCOUNT AS RequeuedCount;
        RETURN;
    END;

    ;WITH RowsToRequeue AS
    (
        SELECT TOP (@MaximumRows) InboxId
        FROM dbo.ZkAttendanceSyncInbox WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE ProcessingStatus = 'Failed'
          AND (@SourceUserId IS NULL OR SourceUserId = @SourceUserId)
          AND (@ReasonCode IS NULL OR ResolutionCode = @ReasonCode)
        ORDER BY InboxId
    )
    UPDATE Inbox
    SET ProcessingStatus = 'Pending', AttemptCount = 0, LastError = NULL,
        ResolutionCode = NULL, ResolutionDetails = NULL, ResolvedAtUtc = NULL,
        ProcessingLeaseId = NULL, ProcessingStartedAtUtc = NULL, ProcessedAtUtc = NULL,
        UpdatedAtUtc = @NowUtc
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    INNER JOIN RowsToRequeue ON RowsToRequeue.InboxId = Inbox.InboxId;
    SELECT @@ROWCOUNT AS RequeuedCount;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkInboxRequeueSkipped', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkInboxRequeueSkipped AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkInboxRequeueSkipped
    @InboxType varchar(16),
    @SourceUserId int = NULL,
    @ReasonCode nvarchar(100) = NULL,
    @MaximumRows int = 1000
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @InboxType NOT IN ('Worker', 'Attendance') THROW 51241, 'InboxType must be Worker or Attendance.', 1;
    IF @MaximumRows < 1 OR @MaximumRows > 10000 THROW 51242, 'MaximumRows must be between 1 and 10000.', 1;
    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();

    IF @InboxType = 'Worker'
    BEGIN
        ;WITH RowsToRequeue AS
        (
            SELECT TOP (@MaximumRows) InboxId
            FROM dbo.ZkWorkerSyncInbox WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE ProcessingStatus = 'Skipped'
              AND (@SourceUserId IS NULL OR SourceUserId = @SourceUserId)
              AND (@ReasonCode IS NULL OR ResolutionCode = @ReasonCode)
            ORDER BY InboxId
        )
        UPDATE Inbox
        SET ProcessingStatus = 'Pending', AttemptCount = 0, LastError = NULL,
            ResolutionCode = NULL, ResolutionDetails = NULL, ResolvedAtUtc = NULL,
            ProcessingLeaseId = NULL, ProcessingStartedAtUtc = NULL, ProcessedAtUtc = NULL,
            UpdatedAtUtc = @NowUtc
        FROM dbo.ZkWorkerSyncInbox AS Inbox
        INNER JOIN RowsToRequeue ON RowsToRequeue.InboxId = Inbox.InboxId;
        SELECT @@ROWCOUNT AS RequeuedCount;
        RETURN;
    END;

    ;WITH RowsToRequeue AS
    (
        SELECT TOP (@MaximumRows) InboxId
        FROM dbo.ZkAttendanceSyncInbox WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE ProcessingStatus = 'Skipped'
          AND (@SourceUserId IS NULL OR SourceUserId = @SourceUserId)
          AND (@ReasonCode IS NULL OR ResolutionCode = @ReasonCode)
        ORDER BY InboxId
    )
    UPDATE Inbox
    SET ProcessingStatus = 'Pending', AttemptCount = 0, LastError = NULL,
        ResolutionCode = NULL, ResolutionDetails = NULL, ResolvedAtUtc = NULL,
        ProcessingLeaseId = NULL, ProcessingStartedAtUtc = NULL, ProcessedAtUtc = NULL,
        UpdatedAtUtc = @NowUtc
    FROM dbo.ZkAttendanceSyncInbox AS Inbox
    INNER JOIN RowsToRequeue ON RowsToRequeue.InboxId = Inbox.InboxId;
    SELECT @@ROWCOUNT AS RequeuedCount;
END;
GO
