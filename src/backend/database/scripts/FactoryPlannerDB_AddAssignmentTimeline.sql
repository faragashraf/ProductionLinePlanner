BEGIN TRANSACTION;
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

CREATE INDEX [IX_AssignmentTimelineEntries_FromSubStageId] ON [AssignmentTimelineEntries] ([FromSubStageId]);

CREATE INDEX [IX_AssignmentTimelineEntries_PerformedByUserId] ON [AssignmentTimelineEntries] ([PerformedByUserId]);

CREATE INDEX [IX_AssignmentTimelineEntries_StartAtUtc] ON [AssignmentTimelineEntries] ([StartAtUtc]);

CREATE INDEX [IX_AssignmentTimelineEntries_ToSubStageId] ON [AssignmentTimelineEntries] ([ToSubStageId]);

CREATE INDEX [IX_AssignmentTimelineEntries_WorkerId] ON [AssignmentTimelineEntries] ([WorkerId]);

CREATE INDEX [IX_AssignmentTimelineEntries_WorkerId_StartAtUtc] ON [AssignmentTimelineEntries] ([WorkerId], [StartAtUtc]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260709123947_AddAssignmentTimeline', N'9.0.0');

COMMIT;
GO

