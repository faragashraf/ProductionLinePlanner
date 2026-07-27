SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.usp_ZkStageWorkers', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkStageWorkers AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkStageWorkers
    @RunId uniqueidentifier = NULL,
    @SourceServer sysname = NULL,
    @SourceDatabase sysname = N'ZKTime'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();
    DECLARE @Discovered int = 0;
    DECLARE @Inserted int = 0;
    DECLARE @Changed int = 0;
    DECLARE @Sql nvarchar(max);
    SET @SourceServer = NULLIF(LTRIM(RTRIM(@SourceServer)), N'');
    DECLARE @SourcePrefix nvarchar(1035) = CASE
        WHEN @SourceServer IS NULL THEN QUOTENAME(@SourceDatabase) + N'.dbo.'
        ELSE QUOTENAME(@SourceServer) + N'.' + QUOTENAME(@SourceDatabase) + N'.dbo.'
    END;

    IF @RunId IS NULL SELECT @RunId = ActiveRunId FROM dbo.ZkSyncState WHERE StateId = 1;
    IF @RunId IS NULL THROW 51101, 'A sync run must be started before staging workers.', 1;
    IF @SourceServer IS NULL AND DB_ID(@SourceDatabase) IS NULL THROW 51102, 'The configured local ZKTime source database does not exist.', 1;
    IF @SourceServer IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.servers WHERE name = @SourceServer AND is_linked = 1)
        THROW 51102, 'The configured ZKTime linked server does not exist.', 1;

    CREATE TABLE #SourceWorkers
    (
        SourceUserId int NOT NULL PRIMARY KEY,
        BadgeNumber nvarchar(120) NULL,
        SourceName nvarchar(200) NULL,
        DefaultDepartmentId int NULL,
        SourceRowHash binary(32) NOT NULL
    );

    BEGIN TRY
        SET @Sql = N'
            INSERT #SourceWorkers (SourceUserId, BadgeNumber, SourceName, DefaultDepartmentId, SourceRowHash)
            SELECT
                CONVERT(int, U.USERID),
                NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(120), U.BADGENUMBER))), N''''),
                NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200), U.Name))), N''''),
                TRY_CONVERT(int, U.DEFAULTDEPTID),
                HASHBYTES(''SHA2_256'', CONVERT(varbinary(8000), CONCAT(
                    N''USERID='', CONVERT(nvarchar(20), U.USERID),
                    N''|BADGE='', COALESCE(LTRIM(RTRIM(CONVERT(nvarchar(120), U.BADGENUMBER))), N''''),
                    N''|NAME='', COALESCE(LTRIM(RTRIM(CONVERT(nvarchar(200), U.Name))), N''''),
                    N''|DEPT='', COALESCE(CONVERT(nvarchar(20), U.DEFAULTDEPTID), N'''')
                )))
            FROM ' + @SourcePrefix + N'USERINFO AS U
            WHERE U.USERID IS NOT NULL;';
        EXEC sys.sp_executesql @Sql;
        SELECT @Discovered = COUNT(*) FROM #SourceWorkers;

        BEGIN TRANSACTION;
        DECLARE @LockResult int;
        EXEC @LockResult = sys.sp_getapplock
            @Resource = N'Dayoub.ZkTime.Staging.Workers',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 30000;
        IF @LockResult < 0 THROW 51103, 'Could not acquire the worker staging lock.', 1;

        SELECT @Changed = COUNT(*)
        FROM dbo.ZkWorkerSyncInbox AS Target
        INNER JOIN #SourceWorkers AS Source ON Source.SourceUserId = Target.SourceUserId
        WHERE Target.SourceRowHash <> Source.SourceRowHash;

        UPDATE Target WITH (UPDLOCK, ROWLOCK)
        SET BadgeNumber = CASE WHEN Target.SourceRowHash <> Source.SourceRowHash THEN Source.BadgeNumber ELSE Target.BadgeNumber END,
            SourceName = CASE WHEN Target.SourceRowHash <> Source.SourceRowHash THEN Source.SourceName ELSE Target.SourceName END,
            DefaultDepartmentId = CASE WHEN Target.SourceRowHash <> Source.SourceRowHash THEN Source.DefaultDepartmentId ELSE Target.DefaultDepartmentId END,
            IsCurrentEmployee = 1,
            LastSeenAtUtc = @NowUtc,
            SourceRowHash = Source.SourceRowHash,
            ProcessingStatus = CASE WHEN Target.SourceRowHash <> Source.SourceRowHash THEN 'Pending' ELSE Target.ProcessingStatus END,
            AttemptCount = CASE WHEN Target.SourceRowHash <> Source.SourceRowHash THEN 0 ELSE Target.AttemptCount END,
            LastError = CASE WHEN Target.SourceRowHash <> Source.SourceRowHash THEN NULL ELSE Target.LastError END,
            ProcessingLeaseId = CASE WHEN Target.SourceRowHash <> Source.SourceRowHash THEN NULL ELSE Target.ProcessingLeaseId END,
            ProcessingStartedAtUtc = CASE WHEN Target.SourceRowHash <> Source.SourceRowHash THEN NULL ELSE Target.ProcessingStartedAtUtc END,
            ProcessedAtUtc = CASE WHEN Target.SourceRowHash <> Source.SourceRowHash THEN NULL ELSE Target.ProcessedAtUtc END,
            UpdatedAtUtc = @NowUtc
        FROM dbo.ZkWorkerSyncInbox AS Target
        INNER JOIN #SourceWorkers AS Source ON Source.SourceUserId = Target.SourceUserId;

        INSERT dbo.ZkWorkerSyncInbox
        (
            SourceUserId, BadgeNumber, SourceName, DefaultDepartmentId, IsCurrentEmployee,
            FirstDiscoveredAtUtc, LastSeenAtUtc, SourceRowHash, ProcessingStatus,
            AttemptCount, CreatedAtUtc, UpdatedAtUtc
        )
        SELECT
            Source.SourceUserId, Source.BadgeNumber, Source.SourceName, Source.DefaultDepartmentId, 1,
            @NowUtc, @NowUtc, Source.SourceRowHash, 'Pending', 0, @NowUtc, @NowUtc
        FROM #SourceWorkers AS Source
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.ZkWorkerSyncInbox AS Target WITH (UPDLOCK, HOLDLOCK)
            WHERE Target.SourceUserId = Source.SourceUserId
        );
        SET @Inserted = @@ROWCOUNT;

        UPDATE dbo.ZkSyncRuns
        SET WorkersDiscovered = @Discovered,
            WorkersInserted = @Inserted,
            WorkersChanged = @Changed,
            UpdatedAtUtc = @NowUtc
        WHERE RunId = @RunId;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @Error nvarchar(2000) = ERROR_MESSAGE();
        EXEC dbo.usp_ZkSyncRunRecordError @RunId = @RunId, @ErrorMessage = @Error;
        THROW;
    END CATCH;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkStageAttendance', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkStageAttendance AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkStageAttendance
    @RunId uniqueidentifier = NULL,
    @SourceServer sysname = NULL,
    @SourceDatabase sysname = N'ZKTime',
    @RollingWindowDays int = 3,
    @ThroughLocal datetime2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @NowUtc datetime2(7) = SYSUTCDATETIME();
    DECLARE @Discovered int = 0;
    DECLARE @Inserted int = 0;
    DECLARE @Sql nvarchar(max);
    SET @SourceServer = NULLIF(LTRIM(RTRIM(@SourceServer)), N'');
    DECLARE @SourcePrefix nvarchar(1035) = CASE
        WHEN @SourceServer IS NULL THEN QUOTENAME(@SourceDatabase) + N'.dbo.'
        ELSE QUOTENAME(@SourceServer) + N'.' + QUOTENAME(@SourceDatabase) + N'.dbo.'
    END;
    DECLARE @QualifiedCheckInOut nvarchar(1100) = @SourcePrefix + N'CHECKINOUT';
    DECLARE @VerifyExpression nvarchar(300);
    DECLARE @SensorExpression nvarchar(300);
    DECLARE @WorkCodeExpression nvarchar(300);
    DECLARE @MetadataSql nvarchar(max);
    DECLARE @HasVerifyCode bit = 0;
    DECLARE @HasSensorId bit = 0;
    DECLARE @HasWorkCode bit = 0;

    IF @RunId IS NULL SELECT @RunId = ActiveRunId FROM dbo.ZkSyncState WHERE StateId = 1;
    IF @RunId IS NULL THROW 51111, 'A sync run must be started before staging attendance.', 1;
    IF @SourceServer IS NULL AND DB_ID(@SourceDatabase) IS NULL THROW 51112, 'The configured local ZKTime source database does not exist.', 1;
    IF @SourceServer IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.servers WHERE name = @SourceServer AND is_linked = 1)
        THROW 51112, 'The configured ZKTime linked server does not exist.', 1;
    IF @RollingWindowDays < 1 OR @RollingWindowDays > 14 THROW 51113, 'RollingWindowDays must be between 1 and 14.', 1;

    IF @ThroughLocal IS NULL
        SET @ThroughLocal = CONVERT(datetime2(7), SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'Egypt Standard Time');
    DECLARE @FromLocal datetime2(7) = DATEADD(DAY, -@RollingWindowDays, CONVERT(date, @ThroughLocal));

    BEGIN TRY
        SET @MetadataSql = N'SELECT TOP (0) VERIFYCODE FROM ' + @QualifiedCheckInOut + N'; SET @Exists = 1;';
        EXEC sys.sp_executesql @MetadataSql, N'@Exists bit OUTPUT', @HasVerifyCode OUTPUT;
    END TRY BEGIN CATCH SET @HasVerifyCode = 0; END CATCH;
    BEGIN TRY
        SET @MetadataSql = N'SELECT TOP (0) SENSORID FROM ' + @QualifiedCheckInOut + N'; SET @Exists = 1;';
        EXEC sys.sp_executesql @MetadataSql, N'@Exists bit OUTPUT', @HasSensorId OUTPUT;
    END TRY BEGIN CATCH SET @HasSensorId = 0; END CATCH;
    BEGIN TRY
        SET @MetadataSql = N'SELECT TOP (0) WorkCode FROM ' + @QualifiedCheckInOut + N'; SET @Exists = 1;';
        EXEC sys.sp_executesql @MetadataSql, N'@Exists bit OUTPUT', @HasWorkCode OUTPUT;
    END TRY BEGIN CATCH SET @HasWorkCode = 0; END CATCH;

    SET @VerifyExpression = CASE WHEN @HasVerifyCode = 0
        THEN N'CAST(NULL AS int)' ELSE N'MAX(TRY_CONVERT(int, C.VERIFYCODE))' END;
    SET @SensorExpression = CASE WHEN @HasSensorId = 0
        THEN N'CAST(NULL AS nvarchar(120))' ELSE N'MAX(CONVERT(nvarchar(120), C.SENSORID))' END;
    SET @WorkCodeExpression = CASE WHEN @HasWorkCode = 0
        THEN N'CAST(NULL AS nvarchar(120))' ELSE N'MAX(CONVERT(nvarchar(120), C.WorkCode))' END;

    CREATE TABLE #SourcePunches
    (
        SourceUserId int NOT NULL,
        BadgeNumber nvarchar(120) NULL,
        SourceCheckTimeLocal datetime2(7) NOT NULL,
        SourceCheckType nvarchar(20) NOT NULL,
        VerifyCode int NULL,
        SensorId nvarchar(120) NULL,
        WorkCode nvarchar(120) NULL,
        SourceKey binary(32) NOT NULL,
        PRIMARY KEY (SourceUserId, SourceCheckTimeLocal, SourceCheckType)
    );

    BEGIN TRY
        SET @Sql = N'
            INSERT #SourcePunches
            (
                SourceUserId, BadgeNumber, SourceCheckTimeLocal, SourceCheckType,
                VerifyCode, SensorId, WorkCode, SourceKey
            )
            SELECT
                CONVERT(int, C.USERID),
                MAX(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(120), U.BADGENUMBER))), N'''')),
                CONVERT(datetime2(7), C.CHECKTIME),
                COALESCE(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(20), C.CHECKTYPE))), N''''), N''''),
                ' + @VerifyExpression + N',
                ' + @SensorExpression + N',
                ' + @WorkCodeExpression + N',
                HASHBYTES(''SHA2_256'', CONVERT(varbinary(8000), CONCAT(
                    CONVERT(nvarchar(20), C.USERID), N''|'',
                    CONVERT(nvarchar(33), CONVERT(datetime2(7), C.CHECKTIME), 126), N''|'',
                    COALESCE(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(20), C.CHECKTYPE))), N''''), N'''')
                )))
            FROM ' + @SourcePrefix + N'CHECKINOUT AS C
            LEFT JOIN ' + @SourcePrefix + N'USERINFO AS U
                ON U.USERID = C.USERID
            WHERE C.USERID IS NOT NULL
              AND C.CHECKTIME IS NOT NULL
              AND C.CHECKTIME >= @FromLocal
              AND C.CHECKTIME <= @ThroughLocal
            GROUP BY
                C.USERID,
                CONVERT(datetime2(7), C.CHECKTIME),
                COALESCE(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(20), C.CHECKTYPE))), N''''), N'''');';
        EXEC sys.sp_executesql
            @Sql,
            N'@FromLocal datetime2(7), @ThroughLocal datetime2(7)',
            @FromLocal = @FromLocal,
            @ThroughLocal = @ThroughLocal;
        SELECT @Discovered = COUNT(*) FROM #SourcePunches;

        BEGIN TRANSACTION;
        DECLARE @LockResult int;
        EXEC @LockResult = sys.sp_getapplock
            @Resource = N'Dayoub.ZkTime.Staging.Attendance',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 30000;
        IF @LockResult < 0 THROW 51114, 'Could not acquire the attendance staging lock.', 1;

        UPDATE Target WITH (UPDLOCK, ROWLOCK)
        SET BadgeNumber = COALESCE(Source.BadgeNumber, Target.BadgeNumber),
            VerifyCode = COALESCE(Source.VerifyCode, Target.VerifyCode),
            SensorId = COALESCE(Source.SensorId, Target.SensorId),
            WorkCode = COALESCE(Source.WorkCode, Target.WorkCode),
            UpdatedAtUtc = @NowUtc
        FROM dbo.ZkAttendanceSyncInbox AS Target
        INNER JOIN #SourcePunches AS Source
            ON Source.SourceUserId = Target.SourceUserId
           AND Source.SourceCheckTimeLocal = Target.SourceCheckTimeLocal
           AND Source.SourceCheckType = Target.SourceCheckType
        WHERE Target.ProcessingStatus <> 'Processed';

        INSERT dbo.ZkAttendanceSyncInbox
        (
            SourceUserId, BadgeNumber, SourceCheckTimeLocal, SourceCheckType,
            VerifyCode, SensorId, WorkCode, SourceKey, ProcessingStatus,
            AttemptCount, InsertedAtUtc, CreatedAtUtc, UpdatedAtUtc
        )
        SELECT
            Source.SourceUserId, Source.BadgeNumber, Source.SourceCheckTimeLocal, Source.SourceCheckType,
            Source.VerifyCode, Source.SensorId, Source.WorkCode, Source.SourceKey, 'Pending',
            0, @NowUtc, @NowUtc, @NowUtc
        FROM #SourcePunches AS Source
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.ZkAttendanceSyncInbox AS Target WITH (UPDLOCK, HOLDLOCK)
            WHERE Target.SourceUserId = Source.SourceUserId
              AND Target.SourceCheckTimeLocal = Source.SourceCheckTimeLocal
              AND Target.SourceCheckType = Source.SourceCheckType
        );
        SET @Inserted = @@ROWCOUNT;

        UPDATE dbo.ZkSyncRuns
        SET PunchesDiscovered = @Discovered,
            PunchesInserted = @Inserted,
            UpdatedAtUtc = @NowUtc
        WHERE RunId = @RunId;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @Error nvarchar(2000) = ERROR_MESSAGE();
        EXEC dbo.usp_ZkSyncRunRecordError @RunId = @RunId, @ErrorMessage = @Error;
        THROW;
    END CATCH;
END;
GO

IF OBJECT_ID(N'dbo.usp_ZkSyncExecuteManual', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.usp_ZkSyncExecuteManual AS BEGIN SET NOCOUNT ON; END;');
GO
ALTER PROCEDURE dbo.usp_ZkSyncExecuteManual
    @SourceServer sysname = NULL,
    @SourceDatabase sysname = N'ZKTime',
    @RollingWindowDays int = 3
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RunId uniqueidentifier = NULL;
    BEGIN TRY
        EXEC dbo.usp_ZkSyncRunStart @TriggerType = N'Manual', @RunId = @RunId OUTPUT;
        EXEC dbo.usp_ZkStageWorkers @RunId = @RunId, @SourceServer = @SourceServer, @SourceDatabase = @SourceDatabase;
        EXEC dbo.usp_ZkStageAttendance @RunId = @RunId, @SourceServer = @SourceServer, @SourceDatabase = @SourceDatabase, @RollingWindowDays = @RollingWindowDays;
        EXEC dbo.usp_ZkSyncCleanup;
        EXEC dbo.usp_ZkSyncRunComplete @Succeeded = 1, @RunId = @RunId;
        EXEC dbo.usp_ZkSyncDiagnostics;
    END TRY
    BEGIN CATCH
        IF @RunId IS NOT NULL
        BEGIN
            DECLARE @Error nvarchar(2000) = ERROR_MESSAGE();
            EXEC dbo.usp_ZkSyncRunRecordError @RunId = @RunId, @ErrorMessage = @Error;
            EXEC dbo.usp_ZkSyncRunComplete @Succeeded = 0, @RunId = @RunId, @ErrorMessage = @Error;
        END;
        THROW;
    END CATCH;
END;
GO
