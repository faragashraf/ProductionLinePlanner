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

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709123947_AddAssignmentTimeline'
)
BEGIN
    CREATE TABLE [AssignmentTimelineEntries] (
        [Id] uniqueidentifier NOT NULL,
        [WorkerId] uniqueidentifier NOT NULL,
        [FromSubStageId] uniqueidentifier NULL,
        [ToSubStageId] uniqueidentifier NULL,
        [AssignmentType] nvarchar(40) NOT NULL,
        [ActionType] nvarchar(80) NOT NULL,
        [Reason] nvarchar(500) NULL,
        [StartAtUtc] datetime2 NOT NULL,
        [EndAtUtc] datetime2 NULL,
        [PerformedByUserId] uniqueidentifier NOT NULL,
        [IsAutomatic] bit NOT NULL DEFAULT CAST(0 AS bit),
        [RelatedTemporaryAssignmentId] uniqueidentifier NULL,
        [ReplacementForWorkerId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AssignmentTimelineEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AssignmentTimelineEntries_AppUsers_PerformedByUserId] FOREIGN KEY ([PerformedByUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AssignmentTimelineEntries_SubStages_FromSubStageId] FOREIGN KEY ([FromSubStageId]) REFERENCES [SubStages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AssignmentTimelineEntries_SubStages_ToSubStageId] FOREIGN KEY ([ToSubStageId]) REFERENCES [SubStages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AssignmentTimelineEntries_Workers_WorkerId] FOREIGN KEY ([WorkerId]) REFERENCES [Workers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709123947_AddAssignmentTimeline'
)
BEGIN
    CREATE INDEX [IX_AssignmentTimelineEntries_FromSubStageId] ON [AssignmentTimelineEntries] ([FromSubStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709123947_AddAssignmentTimeline'
)
BEGIN
    CREATE INDEX [IX_AssignmentTimelineEntries_PerformedByUserId] ON [AssignmentTimelineEntries] ([PerformedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709123947_AddAssignmentTimeline'
)
BEGIN
    CREATE INDEX [IX_AssignmentTimelineEntries_StartAtUtc] ON [AssignmentTimelineEntries] ([StartAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709123947_AddAssignmentTimeline'
)
BEGIN
    CREATE INDEX [IX_AssignmentTimelineEntries_ToSubStageId] ON [AssignmentTimelineEntries] ([ToSubStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709123947_AddAssignmentTimeline'
)
BEGIN
    CREATE INDEX [IX_AssignmentTimelineEntries_WorkerId] ON [AssignmentTimelineEntries] ([WorkerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709123947_AddAssignmentTimeline'
)
BEGIN
    CREATE INDEX [IX_AssignmentTimelineEntries_WorkerId_StartAtUtc] ON [AssignmentTimelineEntries] ([WorkerId], [StartAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709123947_AddAssignmentTimeline'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709123947_AddAssignmentTimeline', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712213444_IamPermissionsFoundation'
)
BEGIN
    ALTER TABLE [AppRoles] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712213444_IamPermissionsFoundation'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Capability] nvarchar(120) NOT NULL,
        [DescriptionAr] nvarchar(500) NULL,
        [DescriptionEn] nvarchar(500) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712213444_IamPermissionsFoundation'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [AppRoleId] uniqueidentifier NOT NULL,
        [PermissionId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([AppRoleId], [PermissionId]),
        CONSTRAINT [FK_RolePermissions_AppRoles_AppRoleId] FOREIGN KEY ([AppRoleId]) REFERENCES [AppRoles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712213444_IamPermissionsFoundation'
)
BEGIN
    CREATE TABLE [UserPermissionOverrides] (
        [AppUserId] uniqueidentifier NOT NULL,
        [PermissionId] uniqueidentifier NOT NULL,
        [Effect] nvarchar(20) NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_UserPermissionOverrides] PRIMARY KEY ([AppUserId], [PermissionId]),
        CONSTRAINT [FK_UserPermissionOverrides_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserPermissionOverrides_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712213444_IamPermissionsFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Name] ON [Permissions] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712213444_IamPermissionsFoundation'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712213444_IamPermissionsFoundation'
)
BEGIN
    CREATE INDEX [IX_UserPermissionOverrides_Effect] ON [UserPermissionOverrides] ([Effect]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712213444_IamPermissionsFoundation'
)
BEGIN
    CREATE INDEX [IX_UserPermissionOverrides_PermissionId] ON [UserPermissionOverrides] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712213444_IamPermissionsFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260712213444_IamPermissionsFoundation', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712230006_EnableCustomRoles'
)
BEGIN
    IF EXISTS (
        SELECT [Name]
        FROM [AppRoles]
        GROUP BY [Name]
        HAVING COUNT(*) > 1
    )
    BEGIN
        THROW 51001, 'Cannot apply EnableCustomRoles because duplicate AppRoles.Name values exist. Correct duplicate role names before rerunning the migration.', 1;
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712230006_EnableCustomRoles'
)
BEGIN
    DROP INDEX [IX_AppRoles_Role] ON [AppRoles];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712230006_EnableCustomRoles'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AppRoles]') AND [c].[name] = N'Role');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [AppRoles] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [AppRoles] ALTER COLUMN [Role] nvarchar(60) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712230006_EnableCustomRoles'
)
BEGIN
    UPDATE [AppRoles] SET [IsSystemRole] = CAST(1 AS bit) WHERE [Role] IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712230006_EnableCustomRoles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppRoles_Name] ON [AppRoles] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712230006_EnableCustomRoles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AppRoles_Role] ON [AppRoles] ([Role]) WHERE [Role] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712230006_EnableCustomRoles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260712230006_EnableCustomRoles', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    ALTER TABLE [Workers] ADD [AttendanceDepartmentId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    ALTER TABLE [Workers] ADD [EmploymentEndDate] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    ALTER TABLE [Workers] ADD [EmploymentStatus] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    ALTER TABLE [Workers] ADD [LastExternalSyncAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    ALTER TABLE [Workers] ADD [PhotoReference] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    ALTER TABLE [SubStages] ADD [Code] nvarchar(120) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    ;WITH MissingCodes AS
    (
        SELECT [Id], CONCAT(N'STG-', UPPER(REPLACE(CONVERT(nvarchar(36), [Id]), N'-', N''))) AS [Code]
        FROM [dbo].[SubStages]
        WHERE NULLIF(LTRIM(RTRIM([Code])), N'') IS NULL
    )
    UPDATE [SubStages]
    SET [Code] = MissingCodes.[Code]
    FROM [dbo].[SubStages]
    INNER JOIN MissingCodes ON MissingCodes.[Id] = [SubStages].[Id];

    IF EXISTS
    (
        SELECT [Code] COLLATE SQL_Latin1_General_CP1_CI_AS
        FROM [dbo].[SubStages]
        GROUP BY [Code] COLLATE SQL_Latin1_General_CP1_CI_AS
        HAVING COUNT(*) > 1 OR MAX(LEN([Code])) > 120 OR SUM(CASE WHEN LEN(LTRIM(RTRIM([Code]))) = 0 THEN 1 ELSE 0 END) > 0
    )
        THROW 51000, 'SubStage Code remediation could not guarantee unique non-empty values.', 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    ;WITH MaxOrderByMainStage AS
    (
        SELECT
            [MainStageId],
            MAX(CASE WHEN [SequenceOrder] > 0 THEN [SequenceOrder] ELSE 0 END) AS [CurrentMaxOrder]
        FROM [dbo].[SubStages]
        GROUP BY [MainStageId]
    ),
    InvalidOrders AS
    (
        SELECT
            s.[Id],
            s.[MainStageId],
            COALESCE(m.[CurrentMaxOrder], 0)
                + ROW_NUMBER() OVER (PARTITION BY s.[MainStageId] ORDER BY s.[Id]) AS [ReplacementOrder]
        FROM [dbo].[SubStages] s
        LEFT JOIN MaxOrderByMainStage m
            ON m.[MainStageId] = s.[MainStageId]
        WHERE s.[SequenceOrder] <= 0
    )
    UPDATE s
    SET [SequenceOrder] = i.[ReplacementOrder]
    FROM [dbo].[SubStages] s
    INNER JOIN InvalidOrders i ON i.[Id] = s.[Id];

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            SELECT [MainStageId], [SequenceOrder]
            FROM [dbo].[SubStages]
            GROUP BY [MainStageId], [SequenceOrder]
            HAVING COUNT(*) > 1
        ) AS [SequenceOrderCollisionCheck]
    )
        THROW 51002, 'SubStage SequenceOrder remediation produced duplicate order values within a MainStage.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM [dbo].[SubStages]
        WHERE [SequenceOrder] <= 0
    )
        THROW 51001, 'SubStage SequenceOrder remediation could not guarantee positive values.', 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    CREATE TABLE [ProductModels] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(120) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductModels] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    CREATE TABLE [WorkerSalaryHistories] (
        [Id] uniqueidentifier NOT NULL,
        [WorkerId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,4) NOT NULL,
        [CurrencyCode] nvarchar(3) NOT NULL DEFAULT N'EGP',
        [EffectiveFrom] datetime2 NOT NULL,
        [EffectiveTo] datetime2 NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedBy] uniqueidentifier NULL,
        [UpdatedBy] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkerSalaryHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_WorkerSalaryHistory_Amount_NonNegative] CHECK ([Amount] >= 0),
        CONSTRAINT [CK_WorkerSalaryHistory_EffectiveRange] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]),
        CONSTRAINT [FK_WorkerSalaryHistories_Workers_WorkerId] FOREIGN KEY ([WorkerId]) REFERENCES [Workers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    CREATE TABLE [ProductModelStages] (
        [Id] uniqueidentifier NOT NULL,
        [ProductModelId] uniqueidentifier NOT NULL,
        [SubStageId] uniqueidentifier NOT NULL,
        [StageOrder] int NOT NULL,
        [PiecePrice] decimal(18,2) NOT NULL,
        [StandardSeconds] decimal(18,4) NULL,
        [CompensationMode] int NOT NULL,
        [IsRequired] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [EffectiveFrom] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductModelStages] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductModelStage_PiecePrice_NonNegative] CHECK ([PiecePrice] >= 0),
        CONSTRAINT [CK_ProductModelStage_StageOrder_Positive] CHECK ([StageOrder] > 0),
        CONSTRAINT [CK_ProductModelStage_StandardSeconds_Positive] CHECK ([StandardSeconds] IS NULL OR [StandardSeconds] > 0),
        CONSTRAINT [FK_ProductModelStages_ProductModels_ProductModelId] FOREIGN KEY ([ProductModelId]) REFERENCES [ProductModels] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProductModelStages_SubStages_SubStageId] FOREIGN KEY ([SubStageId]) REFERENCES [SubStages] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubStages_Code] ON [SubStages] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    EXEC(N'ALTER TABLE [SubStages] ADD CONSTRAINT [CK_SubStage_DefaultOrder_Positive] CHECK ([SequenceOrder] > 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductModels_Code] ON [ProductModels] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductModelStages_ProductModelId_StageOrder] ON [ProductModelStages] ([ProductModelId], [StageOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductModelStages_ProductModelId_SubStageId] ON [ProductModelStages] ([ProductModelId], [SubStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    CREATE INDEX [IX_ProductModelStages_SubStageId] ON [ProductModelStages] ([SubStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WorkerSalaryHistories_Current] ON [WorkerSalaryHistories] ([WorkerId], [EffectiveTo]) WHERE [EffectiveTo] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    CREATE INDEX [IX_WorkerSalaryHistories_WorkerId_EffectiveFrom] ON [WorkerSalaryHistories] ([WorkerId], [EffectiveFrom]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713021838_AddManufacturingMasterDataFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260713021838_AddManufacturingMasterDataFoundation', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713024755_EnforceUniqueCurrentWorkerSalary'
)
BEGIN
    DROP INDEX [IX_WorkerSalaryHistories_Current] ON [WorkerSalaryHistories];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713024755_EnforceUniqueCurrentWorkerSalary'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WorkerSalaryHistories_Current] ON [WorkerSalaryHistories] ([WorkerId], [EffectiveTo]) WHERE [EffectiveTo] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713024755_EnforceUniqueCurrentWorkerSalary'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260713024755_EnforceUniqueCurrentWorkerSalary', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE TABLE [ProductionOrders] (
        [Id] uniqueidentifier NOT NULL,
        [OrderNumber] nvarchar(80) NOT NULL,
        [ProductModelId] uniqueidentifier NOT NULL,
        [ProductionLineId] uniqueidentifier NULL,
        [ProductionDate] date NOT NULL,
        [PlannedQuantity] decimal(18,3) NOT NULL,
        [Status] int NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [UpdatedBy] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_ProductionOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductionOrders_ProductModels_ProductModelId] FOREIGN KEY ([ProductModelId]) REFERENCES [ProductModels] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductionOrders_ProductionLines_ProductionLineId] FOREIGN KEY ([ProductionLineId]) REFERENCES [ProductionLines] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE TABLE [StageProductionRecords] (
        [Id] uniqueidentifier NOT NULL,
        [ProductionOrderId] uniqueidentifier NOT NULL,
        [ProductModelStageId] uniqueidentifier NOT NULL,
        [ProductionDate] date NOT NULL,
        [ProducedQuantity] decimal(18,3) NOT NULL,
        [AcceptedQuantity] decimal(18,3) NOT NULL,
        [RejectedQuantity] decimal(18,3) NOT NULL,
        [Status] int NOT NULL,
        [SnapshotStageCode] nvarchar(80) NOT NULL,
        [SnapshotStageName] nvarchar(200) NOT NULL,
        [SnapshotPiecePrice] decimal(18,4) NOT NULL,
        [SnapshotStandardSeconds] decimal(18,2) NULL,
        [SnapshotCompensationMode] int NOT NULL,
        [TotalWorkerEarnings] decimal(18,4) NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ApprovedBy] uniqueidentifier NULL,
        [ApprovedAtUtc] datetime2 NULL,
        [CancelledBy] uniqueidentifier NULL,
        [CancelledAtUtc] datetime2 NULL,
        CONSTRAINT [PK_StageProductionRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StageProductionRecords_ProductModelStages_ProductModelStageId] FOREIGN KEY ([ProductModelStageId]) REFERENCES [ProductModelStages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StageProductionRecords_ProductionOrders_ProductionOrderId] FOREIGN KEY ([ProductionOrderId]) REFERENCES [ProductionOrders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE TABLE [StageProductionWorkerAllocations] (
        [Id] uniqueidentifier NOT NULL,
        [StageProductionRecordId] uniqueidentifier NOT NULL,
        [WorkerId] uniqueidentifier NOT NULL,
        [Percentage] decimal(9,4) NULL,
        [FixedAmount] decimal(18,4) NULL,
        [EquivalentQuantity] decimal(18,3) NOT NULL,
        [CalculatedEarning] decimal(18,4) NOT NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_StageProductionWorkerAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StageProductionWorkerAllocations_StageProductionRecords_StageProductionRecordId] FOREIGN KEY ([StageProductionRecordId]) REFERENCES [StageProductionRecords] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StageProductionWorkerAllocations_Workers_WorkerId] FOREIGN KEY ([WorkerId]) REFERENCES [Workers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductionOrders_OrderNumber] ON [ProductionOrders] ([OrderNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE INDEX [IX_ProductionOrders_ProductionDate_Status] ON [ProductionOrders] ([ProductionDate], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE INDEX [IX_ProductionOrders_ProductionLineId] ON [ProductionOrders] ([ProductionLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE INDEX [IX_ProductionOrders_ProductModelId] ON [ProductionOrders] ([ProductModelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE INDEX [IX_StageProductionRecords_ProductionDate_Status] ON [StageProductionRecords] ([ProductionDate], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE INDEX [IX_StageProductionRecords_ProductionOrderId_ProductModelStageId] ON [StageProductionRecords] ([ProductionOrderId], [ProductModelStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE INDEX [IX_StageProductionRecords_ProductModelStageId] ON [StageProductionRecords] ([ProductModelStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StageProductionWorkerAllocations_StageProductionRecordId_WorkerId] ON [StageProductionWorkerAllocations] ([StageProductionRecordId], [WorkerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    CREATE INDEX [IX_StageProductionWorkerAllocations_WorkerId] ON [StageProductionWorkerAllocations] ([WorkerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713125851_AddProductionCostRecordingV1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260713125851_AddProductionCostRecordingV1', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713154002_StrengthenProductionCostRecordingV1'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [ClientRequestId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713154002_StrengthenProductionCostRecordingV1'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [ConcurrencyToken] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713154002_StrengthenProductionCostRecordingV1'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [SnapshotProductModelCode] nvarchar(80) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713154002_StrengthenProductionCostRecordingV1'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [SnapshotProductModelName] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713154002_StrengthenProductionCostRecordingV1'
)
BEGIN
    ALTER TABLE [ProductionOrders] ADD [ConcurrencyToken] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713154002_StrengthenProductionCostRecordingV1'
)
BEGIN
    UPDATE ProductionOrders SET ConcurrencyToken = NEWID();
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713154002_StrengthenProductionCostRecordingV1'
)
BEGIN
    UPDATE spr SET ClientRequestId = NEWID(), ConcurrencyToken = NEWID(), SnapshotProductModelCode = pm.Code, SnapshotProductModelName = pm.Name FROM StageProductionRecords spr INNER JOIN ProductionOrders po ON po.Id = spr.ProductionOrderId INNER JOIN ProductModels pm ON pm.Id = po.ProductModelId;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713154002_StrengthenProductionCostRecordingV1'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StageProductionRecords_ProductionOrderId_ClientRequestId] ON [StageProductionRecords] ([ProductionOrderId], [ClientRequestId]) WHERE [ClientRequestId] <> ''00000000-0000-0000-0000-000000000000''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713154002_StrengthenProductionCostRecordingV1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260713154002_StrengthenProductionCostRecordingV1', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    ALTER TABLE [StageProductionWorkerAllocations] ADD [SnapshotWorkerCode] nvarchar(80) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    ALTER TABLE [StageProductionWorkerAllocations] ADD [SnapshotWorkerName] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [SnapshotFactoryCode] nvarchar(80) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [SnapshotFactoryName] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [SnapshotMainStageName] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [SnapshotProductionLineCode] nvarchar(80) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [SnapshotProductionLineName] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    UPDATE allocation
    SET SnapshotWorkerCode = worker.EmployeeCode,
        SnapshotWorkerName = worker.FullName
    FROM StageProductionWorkerAllocations allocation
    INNER JOIN Workers worker ON worker.Id = allocation.WorkerId;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    UPDATE record
    SET SnapshotFactoryCode = factory.Code,
        SnapshotFactoryName = factory.Name,
        SnapshotProductionLineCode = COALESCE(line.LineCode, ''),
        SnapshotProductionLineName = line.Name,
        SnapshotMainStageName = mainStage.Name
    FROM StageProductionRecords record
    INNER JOIN ProductModelStages modelStage ON modelStage.Id = record.ProductModelStageId
    INNER JOIN SubStages subStage ON subStage.Id = modelStage.SubStageId
    INNER JOIN MainStages mainStage ON mainStage.Id = subStage.MainStageId
    INNER JOIN ProductionLines line ON line.Id = mainStage.ProductionLineId
    INNER JOIN Factories factory ON factory.Id = line.FactoryId;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714185040_AddProductionRecordingHistoricalSnapshots'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714185040_AddProductionRecordingHistoricalSnapshots', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714204000_AddProductionParticipantOverrideReason'
)
BEGIN
    ALTER TABLE [StageProductionWorkerAllocations] ADD [ManualOverrideReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714204000_AddProductionParticipantOverrideReason'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714204000_AddProductionParticipantOverrideReason', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714204438_SupportConcurrentTemporaryAssignmentValidation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714204438_SupportConcurrentTemporaryAssignmentValidation', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714204459_AddTemporaryAssignmentOverlapLookupIndex'
)
BEGIN
    CREATE INDEX [IX_WorkerTemporaryAssignments_WorkerId_Status_StartAtUtc_EndAtUtc] ON [WorkerTemporaryAssignments] ([WorkerId], [Status], [StartAtUtc], [EndAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714204459_AddTemporaryAssignmentOverlapLookupIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714204459_AddTemporaryAssignmentOverlapLookupIndex', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    ALTER TABLE [Workers] ADD [LocalDepartmentName] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    ALTER TABLE [StageProductionWorkerAllocations] ADD [InputQuantity] decimal(18,3) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    ALTER TABLE [ProductionOrders] ADD [ApprovedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    ALTER TABLE [ProductionOrders] ADD [ApprovedBy] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    ALTER TABLE [ProductionOrders] ADD [RecordedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    UPDATE [ProductionOrders] SET [RecordedAtUtc] = [CreatedAtUtc] WHERE [RecordedAtUtc] IS NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductionOrders]') AND [c].[name] = N'RecordedAtUtc');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [ProductionOrders] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [ProductionOrders] ALTER COLUMN [RecordedAtUtc] datetime2 NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    ALTER TABLE [ProductionOrders] ADD [SourceImportBatchId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    ALTER TABLE [ProductionOrders] ADD [SourceReference] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    IF EXISTS (
        SELECT 1
        FROM [ProductionOrders]
        WHERE [ProductionLineId] IS NOT NULL
        GROUP BY [ProductionDate], [ProductionLineId], [ProductModelId]
        HAVING COUNT(*) > 1)
    THROW 51000, 'Cannot add the manual-production line/day/product uniqueness rule while duplicate aggregates exist.', 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductionOrders_ProductionDate_ProductionLineId_ProductModelId] ON [ProductionOrders] ([ProductionDate], [ProductionLineId], [ProductModelId]) WHERE [ProductionLineId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715000000_AddPilotManualProductionReadiness'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260715000000_AddPilotManualProductionReadiness', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    DROP INDEX [IX_StageProductionRecords_ProductionOrderId_ProductModelStageId] ON [StageProductionRecords];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    CREATE TABLE [ImportBatches] (
        [Id] uniqueidentifier NOT NULL,
        [IdempotencyKey] nvarchar(128) NOT NULL,
        [SourceReference] nvarchar(500) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [AppliedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ImportBatches] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    CREATE TABLE [ProductionDayStageResolutions] (
        [Id] uniqueidentifier NOT NULL,
        [ProductionOrderId] uniqueidentifier NOT NULL,
        [ProductModelStageId] uniqueidentifier NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [ResolvedBy] uniqueidentifier NOT NULL,
        [ResolvedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductionDayStageResolutions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductionDayStageResolutions_ProductModelStages_ProductModelStageId] FOREIGN KEY ([ProductModelStageId]) REFERENCES [ProductModelStages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductionDayStageResolutions_ProductionOrders_ProductionOrderId] FOREIGN KEY ([ProductionOrderId]) REFERENCES [ProductionOrders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    CREATE INDEX [IX_ProductionOrders_SourceImportBatchId] ON [ProductionOrders] ([SourceImportBatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    CREATE INDEX [IX_StageProductionRecords_ProductionOrderId_ProductModelStageId] ON [StageProductionRecords] ([ProductionOrderId], [ProductModelStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ImportBatches_IdempotencyKey] ON [ImportBatches] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductionDayStageResolutions_ProductionOrderId_ProductModelStageId] ON [ProductionDayStageResolutions] ([ProductionOrderId], [ProductModelStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    CREATE INDEX [IX_ProductionDayStageResolutions_ProductModelStageId] ON [ProductionDayStageResolutions] ([ProductModelStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    ALTER TABLE [ProductionOrders] ADD CONSTRAINT [FK_ProductionOrders_ImportBatches_SourceImportBatchId] FOREIGN KEY ([SourceImportBatchId]) REFERENCES [ImportBatches] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715012831_AddControlledRealDataIntake'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260715012831_AddControlledRealDataIntake', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715193031_AddProductionApprovalCancellationReason'
)
BEGIN
    ALTER TABLE [StageProductionRecords] ADD [ApprovalCancellationReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715193031_AddProductionApprovalCancellationReason'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260715193031_AddProductionApprovalCancellationReason', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716015121_AllowWorkerMultiStageParticipation'
)
BEGIN
    DROP INDEX [IX_WorkerDefaultAssignments_WorkerId] ON [WorkerDefaultAssignments];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716015121_AllowWorkerMultiStageParticipation'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WorkerTemporaryAssignments]') AND [c].[name] = N'FromSubStageId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [WorkerTemporaryAssignments] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [WorkerTemporaryAssignments] ALTER COLUMN [FromSubStageId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716015121_AllowWorkerMultiStageParticipation'
)
BEGIN
    ALTER TABLE [WorkerTemporaryAssignments] ADD [ParticipationMode] nvarchar(40) NOT NULL DEFAULT N'TemporaryMove';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716015121_AllowWorkerMultiStageParticipation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WorkerDefaultAssignments_WorkerId_SubStageId] ON [WorkerDefaultAssignments] ([WorkerId], [SubStageId]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716015121_AllowWorkerMultiStageParticipation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260716015121_AllowWorkerMultiStageParticipation', N'9.0.0');
END;

COMMIT;
GO
