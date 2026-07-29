SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @JobName sysname = N'Dayoub - ZKTime Staging Sync';
IF OBJECT_ID(N'msdb.dbo.sp_update_job', N'P') IS NULL
    THROW 51340, 'SQL Server Agent metadata is unavailable on this instance.', 1;
IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName)
    EXEC msdb.dbo.sp_update_job @job_name = @JobName, @enabled = 0;

SELECT @JobName AS JobName,
       COALESCE((SELECT enabled FROM msdb.dbo.sysjobs WHERE name = @JobName), 0) AS JobEnabled,
       CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName) THEN 1 ELSE 0 END) AS JobExists;
