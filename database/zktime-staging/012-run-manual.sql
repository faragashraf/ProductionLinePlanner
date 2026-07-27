SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TargetDatabase sysname = NULLIF(LTRIM(RTRIM(N'$(TargetDatabase)')), N'');
DECLARE @SourceServer sysname = NULLIF(LTRIM(RTRIM(N'$(SourceServer)')), N'');
DECLARE @SourceDatabase sysname = NULLIF(LTRIM(RTRIM(N'$(SourceDatabase)')), N'');

IF @TargetDatabase IS NULL OR DB_NAME() <> @TargetDatabase
    THROW 51350, 'Run the manual collector in the explicit TargetDatabase.', 1;
IF @SourceDatabase IS NULL OR @SourceDatabase LIKE N'%' + NCHAR(36) + N'(%'
    THROW 51351, 'SourceDatabase must be supplied explicitly.', 1;

EXEC dbo.usp_ZkSyncExecuteManual
    @SourceServer = @SourceServer,
    @SourceDatabase = @SourceDatabase,
    @RollingWindowDays = 3;
