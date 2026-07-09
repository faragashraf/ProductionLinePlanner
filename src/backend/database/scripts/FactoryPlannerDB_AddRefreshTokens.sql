IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [AppRoles] (
        [Id] uniqueidentifier NOT NULL,
        [Role] nvarchar(60) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IsSystemRole] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AppRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [AppUsers] (
        [Id] uniqueidentifier NOT NULL,
        [FullName] nvarchar(200) NOT NULL,
        [Email] nvarchar(200) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [PreferredLanguage] nvarchar(10) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AppUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [Factories] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(100) NOT NULL,
        [Location] nvarchar(300) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Factories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [StageReadinessSnapshots] (
        [Id] uniqueidentifier NOT NULL,
        [ScopeType] nvarchar(60) NOT NULL,
        [ScopeEntityId] uniqueidentifier NOT NULL,
        [CalculatedAtUtc] datetime2 NOT NULL,
        [RequiredWorkers] int NOT NULL,
        [PresentWorkers] int NOT NULL,
        [LateWorkers] int NOT NULL,
        [AbsentWorkers] int NOT NULL,
        [UnassignedWorkers] int NOT NULL,
        [ReadinessPercent] decimal(5,2) NOT NULL,
        [ReadinessStatus] nvarchar(40) NOT NULL DEFAULT N'Unknown',
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_StageReadinessSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_StageReadinessSnapshot_AbsentNonNegative] CHECK ([AbsentWorkers] >= 0),
        CONSTRAINT [CK_StageReadinessSnapshot_LateNonNegative] CHECK ([LateWorkers] >= 0),
        CONSTRAINT [CK_StageReadinessSnapshot_PresentNonNegative] CHECK ([PresentWorkers] >= 0),
        CONSTRAINT [CK_StageReadinessSnapshot_RequiredNonNegative] CHECK ([RequiredWorkers] >= 0),
        CONSTRAINT [CK_StageReadinessSnapshot_UnassignedNonNegative] CHECK ([UnassignedWorkers] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [Workers] (
        [Id] uniqueidentifier NOT NULL,
        [EmployeeCode] nvarchar(80) NOT NULL,
        [FullName] nvarchar(200) NOT NULL,
        [AttendanceUserId] nvarchar(120) NULL,
        [BadgeNumber] nvarchar(120) NULL,
        [Phone] nvarchar(40) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Workers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [ActorUserId] uniqueidentifier NOT NULL,
        [ActionType] nvarchar(40) NOT NULL,
        [EntityType] nvarchar(200) NOT NULL,
        [EntityId] nvarchar(100) NOT NULL,
        [EntityBeforeJson] nvarchar(4000) NULL,
        [EntityAfterJson] nvarchar(4000) NULL,
        [RequestMeta] nvarchar(4000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuditLogs_AppUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [RecipientUserId] uniqueidentifier NOT NULL,
        [SenderUserId] uniqueidentifier NULL,
        [Title] nvarchar(200) NOT NULL,
        [Message] nvarchar(2000) NOT NULL,
        [Status] int NOT NULL,
        [RelatedWorkerId] uniqueidentifier NULL,
        [RelatedEntityType] nvarchar(max) NULL,
        [RelatedEntityId] uniqueidentifier NULL,
        [IsRead] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ReadAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_AppUsers_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Notifications_AppUsers_SenderUserId] FOREIGN KEY ([SenderUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [AppUserId] uniqueidentifier NOT NULL,
        [AppRoleId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([AppUserId], [AppRoleId]),
        CONSTRAINT [FK_UserRoles_AppRoles_AppRoleId] FOREIGN KEY ([AppRoleId]) REFERENCES [AppRoles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserRoles_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductionLines] (
        [Id] uniqueidentifier NOT NULL,
        [FactoryId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [LineCode] nvarchar(80) NULL,
        [SequenceOrder] int NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductionLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductionLines_Factories_FactoryId] FOREIGN KEY ([FactoryId]) REFERENCES [Factories] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [AttendanceRecords] (
        [Id] uniqueidentifier NOT NULL,
        [WorkerId] uniqueidentifier NOT NULL,
        [AttendanceTimeUtc] datetime2 NOT NULL,
        [AttendanceStatus] nvarchar(30) NOT NULL DEFAULT N'Unassigned',
        [Source] nvarchar(60) NULL,
        [SourceRawId] nvarchar(120) NULL,
        [AttendanceUserId] nvarchar(120) NULL,
        [BadgeNumber] nvarchar(120) NULL,
        [SourcePayload] nvarchar(4000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AttendanceRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AttendanceRecords_Workers_WorkerId] FOREIGN KEY ([WorkerId]) REFERENCES [Workers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [MainStages] (
        [Id] uniqueidentifier NOT NULL,
        [ProductionLineId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [SequenceOrder] int NOT NULL,
        [IsCritical] bit NOT NULL DEFAULT CAST(0 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_MainStages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MainStages_ProductionLines_ProductionLineId] FOREIGN KEY ([ProductionLineId]) REFERENCES [ProductionLines] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [SubStages] (
        [Id] uniqueidentifier NOT NULL,
        [MainStageId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Capacity] int NOT NULL,
        [SequenceOrder] int NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SubStages] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SubStage_Capacity_NonNegative] CHECK ([Capacity] >= 0),
        CONSTRAINT [FK_SubStages_MainStages_MainStageId] FOREIGN KEY ([MainStageId]) REFERENCES [MainStages] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [WorkerDefaultAssignments] (
        [Id] uniqueidentifier NOT NULL,
        [WorkerId] uniqueidentifier NOT NULL,
        [SubStageId] uniqueidentifier NOT NULL,
        [AssignedAt] datetime2 NOT NULL,
        [AssignedByUserId] uniqueidentifier NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [Reason] nvarchar(250) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkerDefaultAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkerDefaultAssignments_SubStages_SubStageId] FOREIGN KEY ([SubStageId]) REFERENCES [SubStages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkerDefaultAssignments_Workers_WorkerId] FOREIGN KEY ([WorkerId]) REFERENCES [Workers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE TABLE [WorkerTemporaryAssignments] (
        [Id] uniqueidentifier NOT NULL,
        [WorkerId] uniqueidentifier NOT NULL,
        [FromSubStageId] uniqueidentifier NOT NULL,
        [ToSubStageId] uniqueidentifier NOT NULL,
        [StartAtUtc] datetime2 NOT NULL,
        [EndAtUtc] datetime2 NOT NULL,
        [AssignedByUserId] uniqueidentifier NOT NULL,
        [ReplacementForWorkerId] uniqueidentifier NULL,
        [Reason] nvarchar(300) NOT NULL,
        [Status] nvarchar(50) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkerTemporaryAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkerTemporaryAssignments_SubStages_FromSubStageId] FOREIGN KEY ([FromSubStageId]) REFERENCES [SubStages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkerTemporaryAssignments_SubStages_ToSubStageId] FOREIGN KEY ([ToSubStageId]) REFERENCES [SubStages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkerTemporaryAssignments_Workers_WorkerId] FOREIGN KEY ([WorkerId]) REFERENCES [Workers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppRoles_Role] ON [AppRoles] ([Role]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppUsers_Email] ON [AppUsers] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AttendanceRecords_WorkerId_AttendanceTimeUtc] ON [AttendanceRecords] ([WorkerId], [AttendanceTimeUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_ActorUserId] ON [AuditLogs] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Factories_Code] ON [Factories] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MainStages_ProductionLineId_SequenceOrder] ON [MainStages] ([ProductionLineId], [SequenceOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_RecipientUserId] ON [Notifications] ([RecipientUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_RelatedEntityId] ON [Notifications] ([RelatedEntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_SenderUserId] ON [Notifications] ([SenderUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductionLines_FactoryId_LineCode] ON [ProductionLines] ([FactoryId], [LineCode]) WHERE [LineCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StageReadinessSnapshots_ScopeType_ScopeEntityId_CalculatedAtUtc] ON [StageReadinessSnapshots] ([ScopeType], [ScopeEntityId], [CalculatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubStages_MainStageId_SequenceOrder] ON [SubStages] ([MainStageId], [SequenceOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_AppRoleId] ON [UserRoles] ([AppRoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_AppUserId] ON [UserRoles] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkerDefaultAssignments_SubStageId] ON [WorkerDefaultAssignments] ([SubStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WorkerDefaultAssignments_WorkerId] ON [WorkerDefaultAssignments] ([WorkerId]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Workers_EmployeeCode] ON [Workers] ([EmployeeCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkerTemporaryAssignments_FromSubStageId_ToSubStageId] ON [WorkerTemporaryAssignments] ([FromSubStageId], [ToSubStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkerTemporaryAssignments_ToSubStageId] ON [WorkerTemporaryAssignments] ([ToSubStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkerTemporaryAssignments_WorkerId] ON [WorkerTemporaryAssignments] ([WorkerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709103703_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709103703_InitialCreate', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709115325_AddRefreshTokens'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(128) NOT NULL,
        [IsRevoked] bit NOT NULL DEFAULT CAST(0 AS bit),
        [RevokedAtUtc] datetime2 NULL,
        [RevokedReason] nvarchar(200) NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [LastUsedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709115325_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_AppUserId_IsRevoked_ExpiresAtUtc] ON [RefreshTokens] ([AppUserId], [IsRevoked], [ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709115325_AddRefreshTokens'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709115325_AddRefreshTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709115325_AddRefreshTokens', N'9.0.0');
END;

COMMIT;
GO

