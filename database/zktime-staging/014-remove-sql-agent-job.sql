SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @JobName sysname = N'Dayoub - ZKTime Staging Sync';
IF OBJECT_ID(N'msdb.dbo.sp_delete_job', N'P') IS NULL
    THROW 51343, 'SQL Server Agent metadata is unavailable on this instance.', 1;
IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName)
    EXEC msdb.dbo.sp_delete_job @job_name = @JobName, @delete_unused_schedule = 1;

SELECT @JobName AS JobName, CONVERT(bit, 0) AS JobExists;
