SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TargetSchemaVersion int = 2;
DECLARE @TargetDatabase sysname =
    NULLIF(LTRIM(RTRIM(N'$(TargetDatabase)')), N'');

DECLARE @SourceServer sysname =
    NULLIF(LTRIM(RTRIM(N'$(SourceServer)')), N'');

DECLARE @SourceDatabase sysname =
    NULLIF(LTRIM(RTRIM(N'$(SourceDatabase)')), N'');

DECLARE @InstallAgentJob bit =
    TRY_CONVERT(bit, N'$(InstallAgentJob)');

DECLARE @MajorVersion int =
    TRY_CONVERT(int, SERVERPROPERTY('ProductMajorVersion'));

DECLARE @EngineEdition int =
    TRY_CONVERT(int, SERVERPROPERTY('EngineEdition'));

DECLARE @SourcePrefix nvarchar(1035);
DECLARE @Sql nvarchar(max);
DECLARE @SourceHasWorkers bit = 0;
DECLARE @SourceHasPunches bit = 0;


/* ============================================================================
   1. Validate supplied SQLCMD variables
============================================================================ */

IF @TargetDatabase IS NULL
   OR @TargetDatabase LIKE N'%<%'
   OR @TargetDatabase LIKE N'%>%'
   OR @TargetDatabase LIKE N'%REPLACE%'
   OR @TargetDatabase LIKE N'%' + NCHAR(36) + N'(%'
BEGIN
    THROW 51301,
          'TargetDatabase must be an explicit non-placeholder database name.',
          1;
END;

IF @SourceDatabase IS NULL
   OR @SourceDatabase LIKE N'%<%'
   OR @SourceDatabase LIKE N'%>%'
   OR @SourceDatabase LIKE N'%REPLACE%'
   OR @SourceDatabase LIKE N'%' + NCHAR(36) + N'(%'
BEGIN
    THROW 51302,
          'SourceDatabase must be an explicit non-placeholder database name.',
          1;
END;

IF @SourceServer IS NOT NULL
   AND
   (
       @SourceServer LIKE N'%<%'
       OR @SourceServer LIKE N'%>%'
       OR @SourceServer LIKE N'%REPLACE%'
       OR @SourceServer LIKE N'%' + NCHAR(36) + N'(%'
   )
BEGIN
    THROW 51303,
          'SourceServer must be empty for a local source or an explicit linked-server name.',
          1;
END;

IF @InstallAgentJob IS NULL
BEGIN
    THROW 51304,
          'InstallAgentJob must be explicitly set to 0 or 1.',
          1;
END;

IF DB_NAME() <> @TargetDatabase
BEGIN
    THROW 51305,
          'The current database does not match TargetDatabase. No objects were changed.',
          1;
END;

IF @MajorVersion IS NULL OR @MajorVersion < 13
BEGIN
    THROW 51306,
          'SQL Server 2016 or newer is required.',
          1;
END;


/* ============================================================================
   2. Validate deployment permissions
============================================================================ */

IF COALESCE
   (
       HAS_PERMS_BY_NAME
       (
           DB_NAME(),
           'DATABASE',
           'CREATE TABLE'
       ),
       0
   ) <> 1
   OR COALESCE
      (
          HAS_PERMS_BY_NAME
          (
              DB_NAME(),
              'DATABASE',
              'CREATE PROCEDURE'
          ),
          0
      ) <> 1
   OR COALESCE
      (
          HAS_PERMS_BY_NAME
          (
              DB_NAME(),
              'DATABASE',
              'CREATE TYPE'
          ),
          0
      ) <> 1
   OR
   (
       COALESCE
       (
           HAS_PERMS_BY_NAME
           (
               N'dbo',
               'SCHEMA',
               'ALTER'
           ),
           0
       ) <> 1
       AND
       COALESCE
       (
           HAS_PERMS_BY_NAME
           (
               DB_NAME(),
               'DATABASE',
               'ALTER ANY SCHEMA'
           ),
           0
       ) <> 1
   )
BEGIN
    THROW 51307,
          'The deployment principal lacks CREATE TABLE, CREATE PROCEDURE, CREATE TYPE, or ALTER dbo permissions.',
          1;
END;


/* ============================================================================
   3. Validate expected table names
============================================================================ */

DECLARE @ExpectedTables TABLE
(
    ObjectName sysname NOT NULL PRIMARY KEY
);

INSERT INTO @ExpectedTables
(
    ObjectName
)
VALUES
    (N'ZkSyncSchemaVersions'),
    (N'ZkSyncRuns'),
    (N'ZkSyncState'),
    (N'ZkWorkerSyncInbox'),
    (N'ZkAttendanceSyncInbox');

IF EXISTS
(
    SELECT 1
    FROM @ExpectedTables AS Expected
    CROSS APPLY
    (
        SELECT OBJECT_ID
               (
                   N'dbo.' + Expected.ObjectName
               ) AS ObjectId
    ) AS Resolved
    WHERE Resolved.ObjectId IS NOT NULL
      AND OBJECTPROPERTYEX
          (
              Resolved.ObjectId,
              'IsUserTable'
          ) <> 1
)
BEGIN
    THROW 51308,
          'A required staging table name is already used by a different object type.',
          1;
END;


/* ============================================================================
   4. Validate expected procedure names
============================================================================ */

DECLARE @ExpectedProcedures TABLE
(
    ObjectName sysname NOT NULL PRIMARY KEY
);

INSERT INTO @ExpectedProcedures
(
    ObjectName
)
VALUES
    (N'usp_ZkSyncRunStart'),
    (N'usp_ZkSyncRunRecordError'),
    (N'usp_ZkSyncRunComplete'),
    (N'usp_ZkSyncCleanup'),
    (N'usp_ZkSyncDiagnostics'),
    (N'usp_ZkStageWorkers'),
    (N'usp_ZkStageAttendance'),
    (N'usp_ZkSyncExecuteManual'),
    (N'usp_ZkWorkerInboxReadSnapshot'),
    (N'usp_ZkWorkerInboxComplete'),
    (N'usp_ZkAttendanceInboxClaim'),
    (N'usp_ZkAttendanceInboxComplete'),
    (N'usp_ZkAttendanceInboxPendingDates'),
    (N'usp_ZkInboxRequeueFailed'),
    (N'usp_ZkInboxRequeueSkipped');

IF EXISTS
(
    SELECT 1
    FROM @ExpectedProcedures AS Expected
    CROSS APPLY
    (
        SELECT OBJECT_ID
               (
                   N'dbo.' + Expected.ObjectName
               ) AS ObjectId
    ) AS Resolved
    WHERE Resolved.ObjectId IS NOT NULL
      AND OBJECTPROPERTYEX
          (
              Resolved.ObjectId,
              'IsProcedure'
          ) <> 1
)
BEGIN
    THROW 51309,
          'A required staging procedure name is already used by a different object type.',
          1;
END;

DECLARE @ResolutionResultTypeId int =
    TYPE_ID(N'dbo.ZkInboxResolutionResult');

DECLARE @ResolutionResultTypeObjectId int =
(
    SELECT TT.type_table_object_id
    FROM sys.table_types AS TT
    WHERE TT.user_type_id = @ResolutionResultTypeId
);

IF @ResolutionResultTypeId IS NOT NULL
   AND @ResolutionResultTypeObjectId IS NULL
BEGIN
    THROW 51325,
          'dbo.ZkInboxResolutionResult exists but is not a table type.',
          1;
END;

IF @ResolutionResultTypeObjectId IS NOT NULL
   AND
   (
       (SELECT COUNT(*) FROM sys.columns WHERE object_id = @ResolutionResultTypeObjectId) <> 4
       OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @ResolutionResultTypeObjectId AND name = N'InboxId' AND TYPE_NAME(user_type_id) = N'bigint' AND is_nullable = 0)
       OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @ResolutionResultTypeObjectId AND name = N'ResolutionStatus' AND TYPE_NAME(user_type_id) = N'varchar' AND max_length = 16 AND is_nullable = 0)
       OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @ResolutionResultTypeObjectId AND name = N'ResolutionCode' AND TYPE_NAME(user_type_id) = N'nvarchar' AND max_length = 200 AND is_nullable = 1)
       OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @ResolutionResultTypeObjectId AND name = N'ResolutionDetails' AND TYPE_NAME(user_type_id) = N'nvarchar' AND max_length = 2000 AND is_nullable = 1)
   )
BEGIN
    THROW 51326,
          'dbo.ZkInboxResolutionResult has an incompatible shape and cannot be upgraded in place.',
          1;
END;


/* ============================================================================
   5. Validate table type if it already exists
============================================================================ */

DECLARE @ProcessingResultTypeId int =
    TYPE_ID(N'dbo.ZkInboxProcessingResult');

DECLARE @ProcessingResultTypeObjectId int =
(
    SELECT TT.type_table_object_id
    FROM sys.table_types AS TT
    WHERE TT.user_type_id = @ProcessingResultTypeId
);

IF @ProcessingResultTypeId IS NOT NULL
   AND @ProcessingResultTypeObjectId IS NULL
BEGIN
    THROW 51310,
          'dbo.ZkInboxProcessingResult exists but is not a table type.',
          1;
END;

IF @ProcessingResultTypeObjectId IS NOT NULL
   AND
   (
       (
           SELECT COUNT(*)
           FROM sys.columns
           WHERE object_id = @ProcessingResultTypeObjectId
       ) <> 3

       OR NOT EXISTS
          (
              SELECT 1
              FROM sys.columns
              WHERE object_id = @ProcessingResultTypeObjectId
                AND name = N'InboxId'
                AND TYPE_NAME(user_type_id) = N'bigint'
                AND is_nullable = 0
          )

       OR NOT EXISTS
          (
              SELECT 1
              FROM sys.columns
              WHERE object_id = @ProcessingResultTypeObjectId
                AND name = N'IsSuccessful'
                AND TYPE_NAME(user_type_id) = N'bit'
                AND is_nullable = 0
          )

       OR NOT EXISTS
          (
              SELECT 1
              FROM sys.columns
              WHERE object_id = @ProcessingResultTypeObjectId
                AND name = N'ErrorMessage'
                AND TYPE_NAME(user_type_id) = N'nvarchar'
                AND max_length = 2000
                AND is_nullable = 1
          )
   )
BEGIN
    THROW 51311,
          'dbo.ZkInboxProcessingResult has an incompatible shape and cannot be upgraded in place.',
          1;
END;


/* ============================================================================
   6. Validate columns in any staging tables that already exist
============================================================================ */

DECLARE @ExpectedColumns TABLE
(
    TableName sysname NOT NULL,
    ColumnName sysname NOT NULL,
    PRIMARY KEY
    (
        TableName,
        ColumnName
    )
);

INSERT INTO @ExpectedColumns
(
    TableName,
    ColumnName
)
VALUES
    (N'ZkSyncSchemaVersions', N'Version'),
    (N'ZkSyncSchemaVersions', N'AppliedAtUtc'),
    (N'ZkSyncSchemaVersions', N'Description'),

    (N'ZkSyncRuns', N'RunId'),
    (N'ZkSyncRuns', N'TriggerType'),
    (N'ZkSyncRuns', N'Status'),
    (N'ZkSyncRuns', N'StartedAtUtc'),
    (N'ZkSyncRuns', N'CompletedAtUtc'),
    (N'ZkSyncRuns', N'WorkersDiscovered'),
    (N'ZkSyncRuns', N'WorkersInserted'),
    (N'ZkSyncRuns', N'WorkersChanged'),
    (N'ZkSyncRuns', N'PunchesDiscovered'),
    (N'ZkSyncRuns', N'PunchesInserted'),
    (N'ZkSyncRuns', N'LastError'),
    (N'ZkSyncRuns', N'CreatedAtUtc'),
    (N'ZkSyncRuns', N'UpdatedAtUtc'),

    (N'ZkSyncState', N'StateId'),
    (N'ZkSyncState', N'ActiveRunId'),
    (N'ZkSyncState', N'UpdatedAtUtc'),

    (N'ZkWorkerSyncInbox', N'InboxId'),
    (N'ZkWorkerSyncInbox', N'SourceUserId'),
    (N'ZkWorkerSyncInbox', N'BadgeNumber'),
    (N'ZkWorkerSyncInbox', N'SourceName'),
    (N'ZkWorkerSyncInbox', N'DefaultDepartmentId'),
    (N'ZkWorkerSyncInbox', N'IsCurrentEmployee'),
    (N'ZkWorkerSyncInbox', N'FirstDiscoveredAtUtc'),
    (N'ZkWorkerSyncInbox', N'LastSeenAtUtc'),
    (N'ZkWorkerSyncInbox', N'SourceRowHash'),
    (N'ZkWorkerSyncInbox', N'ProcessingStatus'),
    (N'ZkWorkerSyncInbox', N'AttemptCount'),
    (N'ZkWorkerSyncInbox', N'LastError'),
    (N'ZkWorkerSyncInbox', N'ProcessingLeaseId'),
    (N'ZkWorkerSyncInbox', N'ProcessingStartedAtUtc'),
    (N'ZkWorkerSyncInbox', N'ProcessedAtUtc'),
    (N'ZkWorkerSyncInbox', N'CreatedAtUtc'),
    (N'ZkWorkerSyncInbox', N'UpdatedAtUtc'),
    (N'ZkWorkerSyncInbox', N'RowVersion'),

    (N'ZkAttendanceSyncInbox', N'InboxId'),
    (N'ZkAttendanceSyncInbox', N'SourceUserId'),
    (N'ZkAttendanceSyncInbox', N'BadgeNumber'),
    (N'ZkAttendanceSyncInbox', N'SourceCheckTimeLocal'),
    (N'ZkAttendanceSyncInbox', N'SourceCheckType'),
    (N'ZkAttendanceSyncInbox', N'VerifyCode'),
    (N'ZkAttendanceSyncInbox', N'SensorId'),
    (N'ZkAttendanceSyncInbox', N'WorkCode'),
    (N'ZkAttendanceSyncInbox', N'SourceKey'),
    (N'ZkAttendanceSyncInbox', N'ProcessingStatus'),
    (N'ZkAttendanceSyncInbox', N'AttemptCount'),
    (N'ZkAttendanceSyncInbox', N'LastError'),
    (N'ZkAttendanceSyncInbox', N'InsertedAtUtc'),
    (N'ZkAttendanceSyncInbox', N'ProcessingLeaseId'),
    (N'ZkAttendanceSyncInbox', N'ProcessingStartedAtUtc'),
    (N'ZkAttendanceSyncInbox', N'ProcessedAtUtc'),
    (N'ZkAttendanceSyncInbox', N'CreatedAtUtc'),
    (N'ZkAttendanceSyncInbox', N'UpdatedAtUtc'),
    (N'ZkAttendanceSyncInbox', N'RowVersion');

IF EXISTS
(
    SELECT 1
    FROM @ExpectedColumns AS Expected
    INNER JOIN sys.tables AS TableObject
        ON TableObject.name = Expected.TableName
       AND SCHEMA_NAME(TableObject.schema_id) = N'dbo'
    LEFT JOIN sys.columns AS ColumnObject
        ON ColumnObject.object_id = TableObject.object_id
       AND ColumnObject.name = Expected.ColumnName
    WHERE ColumnObject.column_id IS NULL
)
BEGIN
    THROW 51312,
          'An existing staging table is missing required columns. No upgrade was applied.',
          1;
END;


/* ============================================================================
   7. Validate existing staging data

   Dynamic SQL is required here because these tables do not exist during a
   fresh installation. SQL Server may otherwise compile table references before
   evaluating OBJECT_ID.
============================================================================ */

IF OBJECT_ID(N'dbo.ZkSyncSchemaVersions', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql
        N'
        IF EXISTS
        (
            SELECT 1
            FROM dbo.ZkSyncSchemaVersions
            WHERE Version > @TargetSchemaVersion
        )
        BEGIN
            THROW 51313,
                  ''The installed staging schema is newer than this installer.'',
                  1;
        END;
        ',
        N'@TargetSchemaVersion int',
        @TargetSchemaVersion = @TargetSchemaVersion;
END;

IF OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql
        N'
        IF EXISTS
        (
            SELECT SourceUserId
            FROM dbo.ZkWorkerSyncInbox
            GROUP BY SourceUserId
            HAVING COUNT(*) > 1
        )
        BEGIN
            THROW 51314,
                  ''Worker staging contains duplicate SourceUserId values; repair is required before adding uniqueness.'',
                  1;
        END;
        ';
END;

IF OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql
        N'
        IF EXISTS
        (
            SELECT
                SourceUserId,
                SourceCheckTimeLocal,
                SourceCheckType
            FROM dbo.ZkAttendanceSyncInbox
            GROUP BY
                SourceUserId,
                SourceCheckTimeLocal,
                SourceCheckType
            HAVING COUNT(*) > 1
        )
        BEGIN
            THROW 51315,
                  ''Attendance staging contains duplicate logical punches; repair is required before adding uniqueness.'',
                  1;
        END;
        ';
END;

IF OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql
        N'
        IF EXISTS
        (
            SELECT SourceKey
            FROM dbo.ZkAttendanceSyncInbox
            GROUP BY SourceKey
            HAVING COUNT(*) > 1
        )
        BEGIN
            THROW 51316,
                  ''Attendance staging contains duplicate source keys; repair is required before adding uniqueness.'',
                  1;
        END;
        ';
END;

IF OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql
        N'
        IF EXISTS
        (
            SELECT 1
            FROM dbo.ZkWorkerSyncInbox
            WHERE ProcessingStatus NOT IN
                  (
                      ''Pending'',
                      ''Processing'',
                      ''Processed'',
                      ''Skipped'',
                      ''Failed''
                  )
               OR AttemptCount < 0
        )
        BEGIN
            THROW 51323,
                  ''Worker staging contains invalid status or attempt values; repair is required before adding constraints.'',
                  1;
        END;
        ';
END;

IF OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql
        N'
        IF EXISTS
        (
            SELECT 1
            FROM dbo.ZkAttendanceSyncInbox
            WHERE ProcessingStatus NOT IN
                  (
                      ''Pending'',
                      ''Processing'',
                      ''Processed'',
                      ''Skipped'',
                      ''Failed''
                  )
               OR AttemptCount < 0
        )
        BEGIN
            THROW 51324,
                  ''Attendance staging contains invalid status or attempt values; repair is required before adding constraints.'',
                  1;
        END;
        ';
END;


/* ============================================================================
   8. Validate access to the ZKTime source
============================================================================ */

IF @SourceServer IS NULL
BEGIN
    IF DB_ID(@SourceDatabase) IS NULL
    BEGIN
        THROW 51317,
              'The configured local ZKTime source database is not visible.',
              1;
    END;

    SET @SourcePrefix =
        QUOTENAME(@SourceDatabase) + N'.dbo.';
END;
ELSE
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.servers
        WHERE name = @SourceServer
          AND is_linked = 1
    )
    BEGIN
        THROW 51318,
              'The configured ZKTime linked server is not visible.',
              1;
    END;

    SET @SourcePrefix =
        QUOTENAME(@SourceServer)
        + N'.'
        + QUOTENAME(@SourceDatabase)
        + N'.dbo.';
END;

BEGIN TRY
    SET @Sql =
        N'
        SELECT TOP (0)
            USERID,
            BADGENUMBER,
            Name,
            DEFAULTDEPTID
        FROM ' + @SourcePrefix + N'USERINFO;

        SELECT TOP (0)
            USERID,
            CHECKTIME,
            CHECKTYPE
        FROM ' + @SourcePrefix + N'CHECKINOUT;

        SELECT TOP (0)
            *
        FROM ' + @SourcePrefix + N'DEPARTMENTS;

        SELECT
            @HasWorkers =
                CONVERT
                (
                    bit,
                    CASE
                        WHEN EXISTS
                        (
                            SELECT 1
                            FROM ' + @SourcePrefix + N'USERINFO
                        )
                        THEN 1
                        ELSE 0
                    END
                ),
            @HasPunches =
                CONVERT
                (
                    bit,
                    CASE
                        WHEN EXISTS
                        (
                            SELECT 1
                            FROM ' + @SourcePrefix + N'CHECKINOUT
                        )
                        THEN 1
                        ELSE 0
                    END
                );
        ';

    EXEC sys.sp_executesql
        @Sql,
        N'@HasWorkers bit OUTPUT, @HasPunches bit OUTPUT',
        @HasWorkers = @SourceHasWorkers OUTPUT,
        @HasPunches = @SourceHasPunches OUTPUT;
END TRY
BEGIN CATCH
    THROW 51319,
          'The deployment principal cannot read USERINFO, CHECKINOUT, and DEPARTMENTS from the configured source.',
          1;
END CATCH;


/* ============================================================================
   9. Validate SQL Server Agent access when requested
============================================================================ */

IF @InstallAgentJob = 1
BEGIN
    IF @EngineEdition = 4
       OR OBJECT_ID(N'msdb.dbo.sp_add_job', N'P') IS NULL
    BEGIN
        THROW 51320,
              'SQL Server Agent is unavailable or inaccessible. Set InstallAgentJob=0 and use an external scheduler.',
              1;
    END;

    DECLARE @CanManageJobs bit =
        CONVERT
        (
            bit,
            CASE
                WHEN IS_SRVROLEMEMBER(N'sysadmin') = 1
                THEN 1
                ELSE 0
            END
        );

    IF @CanManageJobs = 0
       AND EXISTS
       (
           SELECT 1
           FROM msdb.sys.database_principals AS MemberPrincipal
           INNER JOIN msdb.sys.database_role_members AS Membership
               ON Membership.member_principal_id =
                  MemberPrincipal.principal_id
           INNER JOIN msdb.sys.database_principals AS RolePrincipal
               ON RolePrincipal.principal_id =
                  Membership.role_principal_id
           WHERE MemberPrincipal.sid = SUSER_SID()
             AND RolePrincipal.name IN
                 (
                     N'SQLAgentUserRole',
                     N'SQLAgentReaderRole',
                     N'SQLAgentOperatorRole'
                 )
       )
    BEGIN
        SET @CanManageJobs = 1;
    END;

    IF @CanManageJobs = 0
    BEGIN
        THROW 51321,
              'The deployment principal is not permitted to create or update SQL Server Agent jobs.',
              1;
    END;

    DECLARE @JobId uniqueidentifier =
    (
        SELECT job_id
        FROM msdb.dbo.sysjobs
        WHERE name = N'Dayoub - ZKTime Staging Sync'
    );

    IF @JobId IS NOT NULL
       AND EXISTS
       (
           SELECT 1
           FROM msdb.dbo.sysjobsteps
           WHERE job_id = @JobId
             AND step_id BETWEEN 1 AND 5
             AND step_name <>
                 CASE step_id
                     WHEN 1 THEN N'Start run'
                     WHEN 2 THEN N'Stage workers'
                     WHEN 3 THEN N'Stage attendance punches'
                     WHEN 4 THEN N'Complete successful run'
                     WHEN 5 THEN N'Record failed run'
                 END
       )
    BEGIN
        THROW 51322,
              'The existing staging job uses conflicting step identifiers; review it before upgrade.',
              1;
    END;

    IF @JobId IS NOT NULL
       AND EXISTS
       (
           SELECT 1
           FROM msdb.dbo.sysjobsteps
           WHERE job_id = @JobId
             AND step_id NOT BETWEEN 1 AND 5
       )
    BEGIN
        THROW 51325,
              'The existing staging job has unexpected steps; review it before upgrade.',
              1;
    END;

    DECLARE @ExistingScheduleId int =
    (
        SELECT schedule_id
        FROM msdb.dbo.sysschedules
        WHERE name =
              N'Dayoub - ZKTime Staging Sync - Every 5 Minutes'
    );

    IF @ExistingScheduleId IS NOT NULL
       AND EXISTS
       (
           SELECT 1
           FROM msdb.dbo.sysjobschedules AS JobSchedule
           INNER JOIN msdb.dbo.sysjobs AS ScheduledJob
               ON ScheduledJob.job_id =
                  JobSchedule.job_id
           WHERE JobSchedule.schedule_id =
                 @ExistingScheduleId
             AND ScheduledJob.name <>
                 N'Dayoub - ZKTime Staging Sync'
       )
    BEGIN
        THROW 51326,
              'The named staging schedule is shared by another job; review it before upgrade.',
              1;
    END;
END;


/* ============================================================================
   10. Return preflight result
============================================================================ */

SELECT
    @TargetSchemaVersion AS TargetSchemaVersion,
    @TargetDatabase AS TargetDatabase,
    COALESCE
    (
        @SourceServer,
        N'(local)'
    ) AS SourceServerMode,
    @SourceDatabase AS SourceDatabase,
    @SourceHasWorkers AS SourceContainsWorkers,
    @SourceHasPunches AS SourceContainsPunches,
    @InstallAgentJob AS AgentInstallationRequested,
    CONVERT(bit, 1) AS PreflightPassed;

GO
