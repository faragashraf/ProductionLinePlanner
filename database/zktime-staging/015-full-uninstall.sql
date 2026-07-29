SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TargetDatabase sysname = NULLIF(LTRIM(RTRIM(N'$(TargetDatabase)')), N'');
IF @TargetDatabase IS NULL OR DB_NAME() <> @TargetDatabase
    THROW 51345, 'Full uninstall must run in the explicit TargetDatabase.', 1;
IF N'$(ConfirmFullUninstall)' <> N'DROP-ZKTIME-STAGING'
    THROW 51344, 'Full uninstall was refused. Set ConfirmFullUninstall=DROP-ZKTIME-STAGING only after backup and approval.', 1;

DECLARE @JobName sysname = N'Dayoub - ZKTime Staging Sync';
IF OBJECT_ID(N'msdb.dbo.sp_delete_job', N'P') IS NOT NULL AND EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName)
    EXEC msdb.dbo.sp_delete_job @job_name = @JobName, @delete_unused_schedule = 1;

DROP PROCEDURE IF EXISTS dbo.usp_ZkInboxRequeueFailed;
DROP PROCEDURE IF EXISTS dbo.usp_ZkInboxRequeueSkipped;
DROP PROCEDURE IF EXISTS dbo.usp_ZkAttendanceInboxPendingDates;
DROP PROCEDURE IF EXISTS dbo.usp_ZkAttendanceInboxComplete;
DROP PROCEDURE IF EXISTS dbo.usp_ZkAttendanceInboxClaim;
DROP PROCEDURE IF EXISTS dbo.usp_ZkWorkerInboxComplete;
DROP PROCEDURE IF EXISTS dbo.usp_ZkWorkerInboxReadSnapshot;
DROP PROCEDURE IF EXISTS dbo.usp_ZkSyncExecuteManual;
DROP PROCEDURE IF EXISTS dbo.usp_ZkStageAttendance;
DROP PROCEDURE IF EXISTS dbo.usp_ZkStageWorkers;
DROP PROCEDURE IF EXISTS dbo.usp_ZkSyncDiagnostics;
DROP PROCEDURE IF EXISTS dbo.usp_ZkSyncCleanup;
DROP PROCEDURE IF EXISTS dbo.usp_ZkSyncRunComplete;
DROP PROCEDURE IF EXISTS dbo.usp_ZkSyncRunRecordError;
DROP PROCEDURE IF EXISTS dbo.usp_ZkSyncRunStart;

IF TYPE_ID(N'dbo.ZkInboxProcessingResult') IS NOT NULL DROP TYPE dbo.ZkInboxProcessingResult;
IF TYPE_ID(N'dbo.ZkInboxResolutionResult') IS NOT NULL DROP TYPE dbo.ZkInboxResolutionResult;
DROP TABLE IF EXISTS dbo.ZkAttendanceSyncInbox;
DROP TABLE IF EXISTS dbo.ZkWorkerSyncInbox;
DROP TABLE IF EXISTS dbo.ZkSyncState;
DROP TABLE IF EXISTS dbo.ZkSyncRuns;
DROP TABLE IF EXISTS dbo.ZkSyncSchemaVersions;

SELECT N'ZKTime staging objects and history were removed.' AS Result;
