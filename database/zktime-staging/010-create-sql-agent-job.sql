SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @InstallAgentJob bit = TRY_CONVERT(bit, N'$(InstallAgentJob)');
DECLARE @JobName sysname = N'Dayoub - ZKTime Staging Sync';
DECLARE @ScheduleName sysname = N'Dayoub - ZKTime Staging Sync - Every 5 Minutes';
DECLARE @TargetDatabase sysname = NULLIF(LTRIM(RTRIM(N'$(TargetDatabase)')), N'');
DECLARE @SourceServer sysname = NULLIF(LTRIM(RTRIM(N'$(SourceServer)')), N'');
DECLARE @SourceDatabase sysname = NULLIF(LTRIM(RTRIM(N'$(SourceDatabase)')), N'');
DECLARE @JobId uniqueidentifier;
DECLARE @ScheduleId int;
DECLARE @JobWasCreated bit = 0;

IF @InstallAgentJob IS NULL THROW 51330, 'InstallAgentJob must be explicitly set to 0 or 1.', 1;
IF @InstallAgentJob = 0
BEGIN
    PRINT N'Staging objects were installed or upgraded. SQL Agent job installation was explicitly skipped.';
    RETURN;
END;

BEGIN TRY
    IF DB_ID(@TargetDatabase) IS NULL THROW 51331, 'The target application database does not exist.', 1;
    IF OBJECT_ID(N'msdb.dbo.sp_add_job', N'P') IS NULL
        THROW 51332, 'SQL Server Agent is unavailable or inaccessible.', 1;
    IF @SourceServer IS NULL AND DB_ID(@SourceDatabase) IS NULL
        THROW 51333, 'The configured local ZKTime source database does not exist.', 1;
    IF @SourceServer IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.servers WHERE name = @SourceServer AND is_linked = 1)
        THROW 51334, 'The configured ZKTime linked server does not exist.', 1;

    SELECT @JobId = job_id FROM msdb.dbo.sysjobs WHERE name = @JobName;
    IF @JobId IS NULL
    BEGIN
        EXEC msdb.dbo.sp_add_job
            @job_name = @JobName,
            @enabled = 0,
            @description = N'Copies raw ZKTime identities and punches into Dayoub staging only. Domain tables are processed by the backend.',
            @job_id = @JobId OUTPUT;
        SET @JobWasCreated = 1;
    END
    ELSE
    BEGIN
        EXEC msdb.dbo.sp_update_job
            @job_id = @JobId,
            @description = N'Copies raw ZKTime identities and punches into Dayoub staging only. Domain tables are processed by the backend.';
    END;

    DECLARE @StartCommand nvarchar(max) = N'USE ' + QUOTENAME(@TargetDatabase) + N';
DECLARE @RunId uniqueidentifier;
EXEC dbo.usp_ZkSyncRunStart @TriggerType = N''SqlAgent'', @RunId = @RunId OUTPUT;';
    DECLARE @WorkersCommand nvarchar(max) = N'USE ' + QUOTENAME(@TargetDatabase) + N';
EXEC dbo.usp_ZkStageWorkers @SourceServer = ' + COALESCE(N'N''' + REPLACE(@SourceServer, N'''', N'''''') + N'''', N'NULL') + N', @SourceDatabase = N''' + REPLACE(@SourceDatabase, N'''', N'''''') + N''';';
    DECLARE @AttendanceCommand nvarchar(max) = N'USE ' + QUOTENAME(@TargetDatabase) + N';
EXEC dbo.usp_ZkStageAttendance @SourceServer = ' + COALESCE(N'N''' + REPLACE(@SourceServer, N'''', N'''''') + N'''', N'NULL') + N', @SourceDatabase = N''' + REPLACE(@SourceDatabase, N'''', N'''''') + N''', @RollingWindowDays = 3;';
    DECLARE @SuccessCommand nvarchar(max) = N'USE ' + QUOTENAME(@TargetDatabase) + N';
EXEC dbo.usp_ZkSyncCleanup;
EXEC dbo.usp_ZkSyncRunComplete @Succeeded = 1;';
    DECLARE @FailureCommand nvarchar(max) = N'USE ' + QUOTENAME(@TargetDatabase) + N';
DECLARE @RunId uniqueidentifier = (SELECT ActiveRunId FROM dbo.ZkSyncState WHERE StateId = 1);
IF @RunId IS NOT NULL EXEC dbo.usp_ZkSyncRunComplete @Succeeded = 0, @RunId = @RunId, @ErrorMessage = N''SQL Agent staging step failed; inspect job history and ZkSyncRuns.'';';

    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobsteps WHERE job_id = @JobId AND step_id = 1)
        EXEC msdb.dbo.sp_update_jobstep @job_id = @JobId, @step_id = 1, @step_name = N'Start run',
            @subsystem = N'TSQL', @command = @StartCommand, @database_name = N'master',
            @on_success_action = 3, @on_success_step_id = 2, @on_fail_action = 4, @on_fail_step_id = 5;
    ELSE
        EXEC msdb.dbo.sp_add_jobstep @job_id = @JobId, @step_id = 1, @step_name = N'Start run',
            @subsystem = N'TSQL', @command = @StartCommand, @database_name = N'master',
            @on_success_action = 3, @on_success_step_id = 2, @on_fail_action = 4, @on_fail_step_id = 5;

    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobsteps WHERE job_id = @JobId AND step_id = 2)
        EXEC msdb.dbo.sp_update_jobstep @job_id = @JobId, @step_id = 2, @step_name = N'Stage workers',
            @subsystem = N'TSQL', @command = @WorkersCommand, @database_name = N'master',
            @on_success_action = 3, @on_success_step_id = 3, @on_fail_action = 4, @on_fail_step_id = 5;
    ELSE
        EXEC msdb.dbo.sp_add_jobstep @job_id = @JobId, @step_id = 2, @step_name = N'Stage workers',
            @subsystem = N'TSQL', @command = @WorkersCommand, @database_name = N'master',
            @on_success_action = 3, @on_success_step_id = 3, @on_fail_action = 4, @on_fail_step_id = 5;

    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobsteps WHERE job_id = @JobId AND step_id = 3)
        EXEC msdb.dbo.sp_update_jobstep @job_id = @JobId, @step_id = 3, @step_name = N'Stage attendance punches',
            @subsystem = N'TSQL', @command = @AttendanceCommand, @database_name = N'master',
            @on_success_action = 3, @on_success_step_id = 4, @on_fail_action = 4, @on_fail_step_id = 5;
    ELSE
        EXEC msdb.dbo.sp_add_jobstep @job_id = @JobId, @step_id = 3, @step_name = N'Stage attendance punches',
            @subsystem = N'TSQL', @command = @AttendanceCommand, @database_name = N'master',
            @on_success_action = 3, @on_success_step_id = 4, @on_fail_action = 4, @on_fail_step_id = 5;

    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobsteps WHERE job_id = @JobId AND step_id = 4)
        EXEC msdb.dbo.sp_update_jobstep @job_id = @JobId, @step_id = 4, @step_name = N'Complete successful run',
            @subsystem = N'TSQL', @command = @SuccessCommand, @database_name = N'master',
            @on_success_action = 1, @on_fail_action = 4, @on_fail_step_id = 5;
    ELSE
        EXEC msdb.dbo.sp_add_jobstep @job_id = @JobId, @step_id = 4, @step_name = N'Complete successful run',
            @subsystem = N'TSQL', @command = @SuccessCommand, @database_name = N'master',
            @on_success_action = 1, @on_fail_action = 4, @on_fail_step_id = 5;

    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobsteps WHERE job_id = @JobId AND step_id = 5)
        EXEC msdb.dbo.sp_update_jobstep @job_id = @JobId, @step_id = 5, @step_name = N'Record failed run',
            @subsystem = N'TSQL', @command = @FailureCommand, @database_name = N'master',
            @on_success_action = 2, @on_fail_action = 2;
    ELSE
        EXEC msdb.dbo.sp_add_jobstep @job_id = @JobId, @step_id = 5, @step_name = N'Record failed run',
            @subsystem = N'TSQL', @command = @FailureCommand, @database_name = N'master',
            @on_success_action = 2, @on_fail_action = 2;

    EXEC msdb.dbo.sp_update_job @job_id = @JobId, @start_step_id = 1;

    SELECT @ScheduleId = schedule_id FROM msdb.dbo.sysschedules WHERE name = @ScheduleName;
    IF @ScheduleId IS NULL
    BEGIN
        EXEC msdb.dbo.sp_add_schedule
            @schedule_name = @ScheduleName,
            @enabled = 1,
            @freq_type = 4,
            @freq_interval = 1,
            @freq_subday_type = 4,
            @freq_subday_interval = 5,
            @active_start_time = 0,
            @schedule_id = @ScheduleId OUTPUT;
    END
    ELSE
    BEGIN
        EXEC msdb.dbo.sp_update_schedule
            @schedule_id = @ScheduleId,
            @freq_type = 4,
            @freq_interval = 1,
            @freq_subday_type = 4,
            @freq_subday_interval = 5,
            @active_start_time = 0;
    END;

    IF NOT EXISTS (SELECT 1 FROM msdb.dbo.sysjobschedules WHERE job_id = @JobId AND schedule_id = @ScheduleId)
        EXEC msdb.dbo.sp_attach_schedule @job_id = @JobId, @schedule_id = @ScheduleId;
    IF NOT EXISTS (SELECT 1 FROM msdb.dbo.sysjobservers WHERE job_id = @JobId)
        EXEC msdb.dbo.sp_add_jobserver @job_id = @JobId;

    SELECT @JobName AS JobName, @JobWasCreated AS JobWasCreated,
           (SELECT enabled FROM msdb.dbo.sysjobs WHERE job_id = @JobId) AS JobEnabled,
           @ScheduleName AS ScheduleName, @TargetDatabase AS TargetDatabase,
           COALESCE(@SourceServer, N'(local)') AS SourceServerMode, @SourceDatabase AS SourceDatabase;
END TRY
BEGIN CATCH
    PRINT N'Staging schema installation may already have completed. SQL Agent job installation or upgrade requires separate attention; no staging data was rolled back or deleted.';
    THROW;
END CATCH;
GO
