SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF TYPE_ID(N'dbo.ZkInboxProcessingResult') IS NULL
    BEGIN
        EXEC(N'CREATE TYPE dbo.ZkInboxProcessingResult AS TABLE
        (
            InboxId bigint NOT NULL PRIMARY KEY,
            IsSuccessful bit NOT NULL,
            ErrorMessage nvarchar(1000) NULL
        );');
    END;

    -- Version 2 leaves the original result type in place for a non-destructive upgrade and
    -- introduces a separate, controlled disposition contract for the backend processor.
    IF TYPE_ID(N'dbo.ZkInboxResolutionResult') IS NULL
    BEGIN
        EXEC(N'CREATE TYPE dbo.ZkInboxResolutionResult AS TABLE
        (
            InboxId bigint NOT NULL PRIMARY KEY,
            ResolutionStatus varchar(16) NOT NULL,
            ResolutionCode nvarchar(100) NULL,
            ResolutionDetails nvarchar(1000) NULL
        );');
    END;

    IF OBJECT_ID(N'dbo.ZkSyncSchemaVersions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ZkSyncSchemaVersions
        (
            Version int NOT NULL,
            AppliedAtUtc datetime2(7) NOT NULL,
            Description nvarchar(500) NOT NULL,
            CONSTRAINT PK_ZkSyncSchemaVersions PRIMARY KEY (Version)
        );
    END;

    IF OBJECT_ID(N'dbo.ZkSyncRuns', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ZkSyncRuns
        (
            RunId uniqueidentifier NOT NULL,
            TriggerType nvarchar(30) NOT NULL,
            Status varchar(16) NOT NULL,
            StartedAtUtc datetime2(7) NOT NULL,
            CompletedAtUtc datetime2(7) NULL,
            WorkersDiscovered int NOT NULL CONSTRAINT DF_ZkSyncRuns_WorkersDiscovered DEFAULT (0),
            WorkersInserted int NOT NULL CONSTRAINT DF_ZkSyncRuns_WorkersInserted DEFAULT (0),
            WorkersChanged int NOT NULL CONSTRAINT DF_ZkSyncRuns_WorkersChanged DEFAULT (0),
            PunchesDiscovered int NOT NULL CONSTRAINT DF_ZkSyncRuns_PunchesDiscovered DEFAULT (0),
            PunchesInserted int NOT NULL CONSTRAINT DF_ZkSyncRuns_PunchesInserted DEFAULT (0),
            LastError nvarchar(2000) NULL,
            CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_ZkSyncRuns_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_ZkSyncRuns_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_ZkSyncRuns PRIMARY KEY (RunId),
            CONSTRAINT CK_ZkSyncRuns_Status CHECK (Status IN ('Running', 'Succeeded', 'Failed'))
        );
    END;

    IF OBJECT_ID(N'dbo.ZkSyncState', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ZkSyncState
        (
            StateId tinyint NOT NULL,
            ActiveRunId uniqueidentifier NULL,
            UpdatedAtUtc datetime2(7) NOT NULL,
            CONSTRAINT PK_ZkSyncState PRIMARY KEY (StateId),
            CONSTRAINT CK_ZkSyncState_Singleton CHECK (StateId = 1)
        );
        INSERT dbo.ZkSyncState (StateId, ActiveRunId, UpdatedAtUtc)
        VALUES (1, NULL, SYSUTCDATETIME());
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.ZkSyncState WHERE StateId = 1)
    BEGIN
        INSERT dbo.ZkSyncState (StateId, ActiveRunId, UpdatedAtUtc)
        VALUES (1, NULL, SYSUTCDATETIME());
    END;

    IF OBJECT_ID(N'dbo.ZkWorkerSyncInbox', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ZkWorkerSyncInbox
        (
            InboxId bigint IDENTITY(1,1) NOT NULL,
            SourceUserId int NOT NULL,
            BadgeNumber nvarchar(120) NULL,
            SourceName nvarchar(200) NULL,
            DefaultDepartmentId int NULL,
            IsCurrentEmployee bit NOT NULL CONSTRAINT DF_ZkWorkerSyncInbox_IsCurrentEmployee DEFAULT (1),
            FirstDiscoveredAtUtc datetime2(7) NOT NULL,
            LastSeenAtUtc datetime2(7) NOT NULL,
            SourceRowHash binary(32) NOT NULL,
            ProcessingStatus varchar(16) NOT NULL,
            AttemptCount int NOT NULL CONSTRAINT DF_ZkWorkerSyncInbox_AttemptCount DEFAULT (0),
            LastError nvarchar(1000) NULL,
            ResolutionCode nvarchar(100) NULL,
            ResolutionDetails nvarchar(1000) NULL,
            ResolvedAtUtc datetime2(7) NULL,
            ProcessingLeaseId uniqueidentifier NULL,
            ProcessingStartedAtUtc datetime2(7) NULL,
            ProcessedAtUtc datetime2(7) NULL,
            CreatedAtUtc datetime2(7) NOT NULL,
            UpdatedAtUtc datetime2(7) NOT NULL,
            RowVersion rowversion NOT NULL,
            CONSTRAINT PK_ZkWorkerSyncInbox PRIMARY KEY (InboxId),
            CONSTRAINT UQ_ZkWorkerSyncInbox_SourceUserId UNIQUE (SourceUserId),
            CONSTRAINT CK_ZkWorkerSyncInbox_Status CHECK (ProcessingStatus IN ('Pending', 'Processing', 'Processed', 'Skipped', 'Failed')),
            CONSTRAINT CK_ZkWorkerSyncInbox_AttemptCount CHECK (AttemptCount >= 0)
        );
    END;

    IF COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'ResolutionCode') IS NULL
        ALTER TABLE dbo.ZkWorkerSyncInbox ADD ResolutionCode nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'ResolutionDetails') IS NULL
        ALTER TABLE dbo.ZkWorkerSyncInbox ADD ResolutionDetails nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.ZkWorkerSyncInbox', N'ResolvedAtUtc') IS NULL
        ALTER TABLE dbo.ZkWorkerSyncInbox ADD ResolvedAtUtc datetime2(7) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ZkWorkerSyncInbox') AND name = N'IX_ZkWorkerSyncInbox_Processing')
    BEGIN
        CREATE INDEX IX_ZkWorkerSyncInbox_Processing
            ON dbo.ZkWorkerSyncInbox (ProcessingStatus, AttemptCount, ProcessingStartedAtUtc, InboxId)
            INCLUDE (SourceUserId, BadgeNumber, SourceRowHash);
    END;

    IF OBJECT_ID(N'dbo.ZkAttendanceSyncInbox', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ZkAttendanceSyncInbox
        (
            InboxId bigint IDENTITY(1,1) NOT NULL,
            SourceUserId int NOT NULL,
            BadgeNumber nvarchar(120) NULL,
            SourceCheckTimeLocal datetime2(7) NOT NULL,
            SourceCheckType nvarchar(20) NOT NULL,
            VerifyCode int NULL,
            SensorId nvarchar(120) NULL,
            WorkCode nvarchar(120) NULL,
            SourceKey binary(32) NOT NULL,
            ProcessingStatus varchar(16) NOT NULL,
            AttemptCount int NOT NULL CONSTRAINT DF_ZkAttendanceSyncInbox_AttemptCount DEFAULT (0),
            LastError nvarchar(1000) NULL,
            ResolutionCode nvarchar(100) NULL,
            ResolutionDetails nvarchar(1000) NULL,
            ResolvedAtUtc datetime2(7) NULL,
            InsertedAtUtc datetime2(7) NOT NULL,
            ProcessingLeaseId uniqueidentifier NULL,
            ProcessingStartedAtUtc datetime2(7) NULL,
            ProcessedAtUtc datetime2(7) NULL,
            CreatedAtUtc datetime2(7) NOT NULL,
            UpdatedAtUtc datetime2(7) NOT NULL,
            RowVersion rowversion NOT NULL,
            CONSTRAINT PK_ZkAttendanceSyncInbox PRIMARY KEY (InboxId),
            CONSTRAINT UQ_ZkAttendanceSyncInbox_LogicalPunch UNIQUE (SourceUserId, SourceCheckTimeLocal, SourceCheckType),
            CONSTRAINT UQ_ZkAttendanceSyncInbox_SourceKey UNIQUE (SourceKey),
            CONSTRAINT CK_ZkAttendanceSyncInbox_Status CHECK (ProcessingStatus IN ('Pending', 'Processing', 'Processed', 'Skipped', 'Failed')),
            CONSTRAINT CK_ZkAttendanceSyncInbox_AttemptCount CHECK (AttemptCount >= 0)
        );
    END;

    IF COL_LENGTH(N'dbo.ZkAttendanceSyncInbox', N'ResolutionCode') IS NULL
        ALTER TABLE dbo.ZkAttendanceSyncInbox ADD ResolutionCode nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.ZkAttendanceSyncInbox', N'ResolutionDetails') IS NULL
        ALTER TABLE dbo.ZkAttendanceSyncInbox ADD ResolutionDetails nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.ZkAttendanceSyncInbox', N'ResolvedAtUtc') IS NULL
        ALTER TABLE dbo.ZkAttendanceSyncInbox ADD ResolvedAtUtc datetime2(7) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ZkAttendanceSyncInbox') AND name = N'IX_ZkAttendanceSyncInbox_Processing')
    BEGIN
        CREATE INDEX IX_ZkAttendanceSyncInbox_Processing
            ON dbo.ZkAttendanceSyncInbox (ProcessingStatus, AttemptCount, SourceCheckTimeLocal, ProcessingStartedAtUtc, InboxId)
            INCLUDE (SourceUserId, BadgeNumber, SourceCheckType, SourceKey);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ZkSyncRuns') AND name = N'IX_ZkSyncRuns_StartedAtUtc')
    BEGIN
        CREATE INDEX IX_ZkSyncRuns_StartedAtUtc
            ON dbo.ZkSyncRuns (StartedAtUtc DESC)
            INCLUDE (Status, CompletedAtUtc, LastError);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ZkWorkerSyncInbox') AND name = N'UQ_ZkWorkerSyncInbox_SourceUserId')
        ALTER TABLE dbo.ZkWorkerSyncInbox ADD CONSTRAINT UQ_ZkWorkerSyncInbox_SourceUserId UNIQUE (SourceUserId);
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ZkWorkerSyncInbox') AND name = N'CK_ZkWorkerSyncInbox_Status' AND definition NOT LIKE N'%Skipped%')
        ALTER TABLE dbo.ZkWorkerSyncInbox DROP CONSTRAINT CK_ZkWorkerSyncInbox_Status;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ZkWorkerSyncInbox') AND name = N'CK_ZkWorkerSyncInbox_Status')
        ALTER TABLE dbo.ZkWorkerSyncInbox ADD CONSTRAINT CK_ZkWorkerSyncInbox_Status CHECK (ProcessingStatus IN ('Pending', 'Processing', 'Processed', 'Skipped', 'Failed'));
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ZkWorkerSyncInbox') AND name = N'CK_ZkWorkerSyncInbox_AttemptCount')
        ALTER TABLE dbo.ZkWorkerSyncInbox ADD CONSTRAINT CK_ZkWorkerSyncInbox_AttemptCount CHECK (AttemptCount >= 0);

    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ZkAttendanceSyncInbox') AND name = N'UQ_ZkAttendanceSyncInbox_LogicalPunch')
        ALTER TABLE dbo.ZkAttendanceSyncInbox ADD CONSTRAINT UQ_ZkAttendanceSyncInbox_LogicalPunch UNIQUE (SourceUserId, SourceCheckTimeLocal, SourceCheckType);
    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ZkAttendanceSyncInbox') AND name = N'UQ_ZkAttendanceSyncInbox_SourceKey')
        ALTER TABLE dbo.ZkAttendanceSyncInbox ADD CONSTRAINT UQ_ZkAttendanceSyncInbox_SourceKey UNIQUE (SourceKey);
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ZkAttendanceSyncInbox') AND name = N'CK_ZkAttendanceSyncInbox_Status' AND definition NOT LIKE N'%Skipped%')
        ALTER TABLE dbo.ZkAttendanceSyncInbox DROP CONSTRAINT CK_ZkAttendanceSyncInbox_Status;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ZkAttendanceSyncInbox') AND name = N'CK_ZkAttendanceSyncInbox_Status')
        ALTER TABLE dbo.ZkAttendanceSyncInbox ADD CONSTRAINT CK_ZkAttendanceSyncInbox_Status CHECK (ProcessingStatus IN ('Pending', 'Processing', 'Processed', 'Skipped', 'Failed'));
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ZkAttendanceSyncInbox') AND name = N'CK_ZkAttendanceSyncInbox_AttemptCount')
        ALTER TABLE dbo.ZkAttendanceSyncInbox ADD CONSTRAINT CK_ZkAttendanceSyncInbox_AttemptCount CHECK (AttemptCount >= 0);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
