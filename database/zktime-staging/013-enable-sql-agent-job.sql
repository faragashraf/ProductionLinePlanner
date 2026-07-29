SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @JobName sysname = N'Dayoub - ZKTime Staging Sync';
IF OBJECT_ID(N'msdb.dbo.sp_update_job', N'P') IS NULL
    THROW 51341, 'SQL Server Agent metadata is unavailable on this instance.', 1;
IF NOT EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName)
    THROW 51342, 'The ZKTime staging SQL Agent job is not installed.', 1;

EXEC msdb.dbo.sp_update_job @job_name = @JobName, @enabled = 1;
SELECT @JobName AS JobName, enabled AS JobEnabled FROM msdb.dbo.sysjobs WHERE name = @JobName;
