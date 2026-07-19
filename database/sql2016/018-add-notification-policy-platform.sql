BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EventKey] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    ALTER TABLE [Notifications] ADD [Severity] int NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE TABLE [NotificationPolicies] (
        [Id] uniqueidentifier NOT NULL,
        [EventKey] nvarchar(100) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [Severity] int NOT NULL,
        [IsToastEnabled] bit NOT NULL,
        [IsInboxEnabled] bit NOT NULL,
        [IsSoundEnabled] bit NOT NULL,
        [SoundKey] nvarchar(50) NULL,
        [TitleTemplateAr] nvarchar(200) NOT NULL,
        [MessageTemplateAr] nvarchar(2000) NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_NotificationPolicies] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_NotificationPolicies_SoundKey] CHECK (([IsSoundEnabled] = 0 AND [SoundKey] IS NULL) OR ([IsSoundEnabled] = 1 AND [SoundKey] = 'default')),
        CONSTRAINT [FK_NotificationPolicies_AppUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_NotificationPolicies_AppUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE TABLE [NotificationPolicyRecipientRules] (
        [Id] uniqueidentifier NOT NULL,
        [NotificationPolicyId] uniqueidentifier NOT NULL,
        [RecipientKind] int NOT NULL,
        [UserId] uniqueidentifier NULL,
        [RoleId] uniqueidentifier NULL,
        [PermissionKey] nvarchar(100) NULL,
        [CapabilityKey] nvarchar(100) NULL,
        [IsExcludeActor] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_NotificationPolicyRecipientRules] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_NotificationPolicyRecipientRules_Target] CHECK (([RecipientKind] = 0 AND [UserId] IS NOT NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 1 AND [UserId] IS NULL AND [RoleId] IS NOT NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 2 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NOT NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 3 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NOT NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 4 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 5 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 1)),
        CONSTRAINT [FK_NotificationPolicyRecipientRules_AppRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AppRoles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_NotificationPolicyRecipientRules_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_NotificationPolicyRecipientRules_NotificationPolicies_NotificationPolicyId] FOREIGN KEY ([NotificationPolicyId]) REFERENCES [NotificationPolicies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE INDEX [IX_Notifications_EventKey] ON [Notifications] ([EventKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE INDEX [IX_NotificationPolicies_CreatedByUserId] ON [NotificationPolicies] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotificationPolicies_EventKey] ON [NotificationPolicies] ([EventKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE INDEX [IX_NotificationPolicies_UpdatedAtUtc] ON [NotificationPolicies] ([UpdatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE INDEX [IX_NotificationPolicies_UpdatedByUserId] ON [NotificationPolicies] ([UpdatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotificationPolicyRecipientRules_NotificationPolicyId_SortOrder] ON [NotificationPolicyRecipientRules] ([NotificationPolicyId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE INDEX [IX_NotificationPolicyRecipientRules_RoleId] ON [NotificationPolicyRecipientRules] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    CREATE INDEX [IX_NotificationPolicyRecipientRules_UserId] ON [NotificationPolicyRecipientRules] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719213301_AddNotificationPolicyPlatform'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719213301_AddNotificationPolicyPlatform', N'9.0.0');
END;

COMMIT;
GO
