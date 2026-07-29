SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Version int = 2;
DECLARE @Description nvarchar(500) = N'Add explicit skipped resolutions and controlled inbox completion dispositions';

BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.ZkSyncSchemaVersions WITH (UPDLOCK, HOLDLOCK) WHERE Version = @Version)
BEGIN
    INSERT dbo.ZkSyncSchemaVersions (Version, AppliedAtUtc, Description)
    VALUES (@Version, SYSUTCDATETIME(), @Description);
END;
COMMIT TRANSACTION;
GO
