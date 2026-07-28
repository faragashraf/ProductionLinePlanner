using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations;

public partial class BackfillConstrainDepartmentOwnedStagesAndLineAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.MainStages', N'DepartmentId') IS NULL
               OR COL_LENGTH(N'dbo.SubStages', N'DepartmentId') IS NULL
               OR COL_LENGTH(N'dbo.ProductModelStages', N'ProductionLineId') IS NULL
               OR COL_LENGTH(N'dbo.WorkerDefaultAssignments', N'ProductionLineId') IS NULL
                THROW 51109, 'Stage migration aborted: the additive migration is incomplete.', 1;

            DECLARE @mainHasLegacyLine bit = IIF(COL_LENGTH(N'dbo.MainStages', N'ProductionLineId') IS NULL, 0, 1);
            DECLARE @subHasLegacyLine bit = IIF(COL_LENGTH(N'dbo.SubStages', N'ProductionLineId') IS NULL, 0, 1);

            IF @mainHasLegacyLine <> @subHasLegacyLine
                THROW 51110, 'Stage migration aborted: only one legacy stage table still has ProductionLineId.', 1;

            IF @mainHasLegacyLine = 1
            BEGIN
                -- Dynamic SQL is intentional: SQL Server otherwise resolves legacy
                -- column names when compiling the whole batch, including resume paths
                -- where those columns were already removed.
                EXEC(N'
                    IF EXISTS (
                        SELECT 1
                        FROM [MainStages] AS [m]
                        LEFT JOIN [ProductionLines] AS [l] ON [l].[Id] = [m].[ProductionLineId]
                        WHERE [l].[Id] IS NULL OR [l].[DepartmentId] IS NULL)
                        THROW 51100, ''Stage migration aborted: every MainStage line must have a department.'', 1;

                    IF EXISTS (
                        SELECT 1
                        FROM [SubStages] AS [s]
                        LEFT JOIN [MainStages] AS [m] ON [m].[Id] = [s].[MainStageId]
                        WHERE [m].[Id] IS NULL OR [s].[ProductionLineId] <> [m].[ProductionLineId])
                        THROW 51101, ''Stage migration aborted: a SubStage line differs from its MainStage line.'', 1;

                    IF EXISTS (
                        SELECT [l].[DepartmentId], [m].[Name]
                        FROM [MainStages] AS [m]
                        INNER JOIN [ProductionLines] AS [l] ON [l].[Id] = [m].[ProductionLineId]
                        GROUP BY [l].[DepartmentId], [m].[Name]
                        HAVING COUNT_BIG(*) > 1)
                        THROW 51102, ''Stage migration aborted: duplicate MainStage names would exist in one department.'', 1;

                    IF EXISTS (
                        SELECT [l].[DepartmentId], [s].[Code]
                        FROM [SubStages] AS [s]
                        INNER JOIN [ProductionLines] AS [l] ON [l].[Id] = [s].[ProductionLineId]
                        GROUP BY [l].[DepartmentId], [s].[Code]
                        HAVING COUNT_BIG(*) > 1)
                        THROW 51103, ''Stage migration aborted: duplicate SubStage codes would exist in one department.'', 1;

                    IF EXISTS (
                        SELECT 1
                        FROM [ProductModelStages] AS [pms]
                        LEFT JOIN [SubStages] AS [s] ON [s].[Id] = [pms].[SubStageId]
                        LEFT JOIN [ProductionLines] AS [l] ON [l].[Id] = [s].[ProductionLineId]
                        WHERE [s].[Id] IS NULL OR [l].[Id] IS NULL)
                        THROW 51104, ''Stage migration aborted: a ProductModelStage has no valid legacy stage line.'', 1;

                    IF EXISTS (
                        SELECT 1
                        FROM [WorkerDefaultAssignments] AS [wda]
                        LEFT JOIN [SubStages] AS [s] ON [s].[Id] = [wda].[SubStageId]
                        LEFT JOIN [ProductionLines] AS [l] ON [l].[Id] = [s].[ProductionLineId]
                        WHERE [s].[Id] IS NULL OR [l].[Id] IS NULL)
                        THROW 51105, ''Stage migration aborted: a worker default assignment has no valid legacy stage line.'', 1;

                    INSERT INTO [StageOwnershipMigrationRollbackMap] ([StageKind], [StageId], [ProductionLineId])
                    SELECT 1, [m].[Id], [m].[ProductionLineId]
                    FROM [MainStages] AS [m]
                    WHERE NOT EXISTS (
                        SELECT 1 FROM [StageOwnershipMigrationRollbackMap] AS [map]
                        WHERE [map].[StageKind] = 1 AND [map].[StageId] = [m].[Id]);

                    INSERT INTO [StageOwnershipMigrationRollbackMap] ([StageKind], [StageId], [ProductionLineId])
                    SELECT 2, [s].[Id], [s].[ProductionLineId]
                    FROM [SubStages] AS [s]
                    WHERE NOT EXISTS (
                        SELECT 1 FROM [StageOwnershipMigrationRollbackMap] AS [map]
                        WHERE [map].[StageKind] = 2 AND [map].[StageId] = [s].[Id]);

                    UPDATE [m]
                    SET [DepartmentId] = [l].[DepartmentId]
                    FROM [MainStages] AS [m]
                    INNER JOIN [ProductionLines] AS [l] ON [l].[Id] = [m].[ProductionLineId];

                    UPDATE [s]
                    SET [DepartmentId] = [m].[DepartmentId]
                    FROM [SubStages] AS [s]
                    INNER JOIN [MainStages] AS [m] ON [m].[Id] = [s].[MainStageId];

                    UPDATE [pms]
                    SET [ProductionLineId] = [s].[ProductionLineId]
                    FROM [ProductModelStages] AS [pms]
                    INNER JOIN [SubStages] AS [s] ON [s].[Id] = [pms].[SubStageId];

                    UPDATE [wda]
                    SET [ProductionLineId] = [s].[ProductionLineId]
                    FROM [WorkerDefaultAssignments] AS [wda]
                    INNER JOIN [SubStages] AS [s] ON [s].[Id] = [wda].[SubStageId];');
            END
            ELSE
            BEGIN
                -- Resume only a deterministic partially converted database. This
                -- path never guesses between two lines in the same department.
                IF EXISTS (
                    SELECT 1
                    FROM [MainStages] AS [m]
                    LEFT JOIN [Departments] AS [d] ON [d].[Id] = [m].[DepartmentId]
                    WHERE [d].[Id] IS NULL)
                   OR EXISTS (
                    SELECT 1
                    FROM [SubStages] AS [s]
                    LEFT JOIN [MainStages] AS [m]
                      ON [m].[Id] = [s].[MainStageId] AND [m].[DepartmentId] = [s].[DepartmentId]
                    WHERE [m].[Id] IS NULL)
                    THROW 51111, 'Stage migration aborted: partially converted stage ownership is inconsistent.', 1;

                IF EXISTS (
                    SELECT [m].[DepartmentId]
                    FROM [MainStages] AS [m]
                    LEFT JOIN [ProductionLines] AS [l] ON [l].[DepartmentId] = [m].[DepartmentId]
                    GROUP BY [m].[DepartmentId]
                    HAVING COUNT([l].[Id]) <> 1)
                    THROW 51112, 'Stage migration aborted: legacy line ownership cannot be reconstructed uniquely.', 1;

                INSERT INTO [StageOwnershipMigrationRollbackMap] ([StageKind], [StageId], [ProductionLineId])
                SELECT 1, [m].[Id], MIN([l].[Id])
                FROM [MainStages] AS [m]
                INNER JOIN [ProductionLines] AS [l] ON [l].[DepartmentId] = [m].[DepartmentId]
                WHERE NOT EXISTS (
                    SELECT 1 FROM [StageOwnershipMigrationRollbackMap] AS [map]
                    WHERE [map].[StageKind] = 1 AND [map].[StageId] = [m].[Id])
                GROUP BY [m].[Id];

                INSERT INTO [StageOwnershipMigrationRollbackMap] ([StageKind], [StageId], [ProductionLineId])
                SELECT 2, [s].[Id], MIN([l].[Id])
                FROM [SubStages] AS [s]
                INNER JOIN [ProductionLines] AS [l] ON [l].[DepartmentId] = [s].[DepartmentId]
                WHERE NOT EXISTS (
                    SELECT 1 FROM [StageOwnershipMigrationRollbackMap] AS [map]
                    WHERE [map].[StageKind] = 2 AND [map].[StageId] = [s].[Id])
                GROUP BY [s].[Id];

                UPDATE [pms]
                SET [ProductionLineId] = [candidate].[ProductionLineId]
                FROM [ProductModelStages] AS [pms]
                INNER JOIN [SubStages] AS [s] ON [s].[Id] = [pms].[SubStageId]
                CROSS APPLY (
                    SELECT MIN([l].[Id]) AS [ProductionLineId], COUNT_BIG(*) AS [LineCount]
                    FROM [ProductionLines] AS [l]
                    WHERE [l].[DepartmentId] = [s].[DepartmentId]) AS [candidate]
                WHERE [pms].[ProductionLineId] IS NULL AND [candidate].[LineCount] = 1;

                UPDATE [wda]
                SET [ProductionLineId] = [candidate].[ProductionLineId]
                FROM [WorkerDefaultAssignments] AS [wda]
                INNER JOIN [SubStages] AS [s] ON [s].[Id] = [wda].[SubStageId]
                CROSS APPLY (
                    SELECT MIN([l].[Id]) AS [ProductionLineId], COUNT_BIG(*) AS [LineCount]
                    FROM [ProductionLines] AS [l]
                    WHERE [l].[DepartmentId] = [s].[DepartmentId]) AS [candidate]
                WHERE [wda].[ProductionLineId] IS NULL AND [candidate].[LineCount] = 1;
            END;

            IF EXISTS (SELECT 1 FROM [MainStages] WHERE [DepartmentId] IS NULL)
               OR EXISTS (SELECT 1 FROM [SubStages] WHERE [DepartmentId] IS NULL)
               OR EXISTS (SELECT 1 FROM [ProductModelStages] WHERE [ProductionLineId] IS NULL)
               OR EXISTS (SELECT 1 FROM [WorkerDefaultAssignments] WHERE [ProductionLineId] IS NULL)
                THROW 51113, 'Stage migration aborted: additive keys remain null after backfill.', 1;

            IF EXISTS (
                SELECT 1
                FROM [ProductModelStages] AS [pms]
                LEFT JOIN [ProductionLines] AS [l] ON [l].[Id] = [pms].[ProductionLineId]
                LEFT JOIN [SubStages] AS [s] ON [s].[Id] = [pms].[SubStageId]
                WHERE [l].[Id] IS NULL
                   OR [s].[Id] IS NULL
                   OR [l].[DepartmentId] IS NULL
                   OR [s].[DepartmentId] IS NULL
                   OR [l].[DepartmentId] <> [s].[DepartmentId])
                THROW 51106, 'Stage migration aborted: ProductModelStage line and stage departments are inconsistent.', 1;

            IF EXISTS (
                SELECT [ProductModelId], [ProductionLineId], [SubStageId]
                FROM [ProductModelStages]
                GROUP BY [ProductModelId], [ProductionLineId], [SubStageId]
                HAVING COUNT_BIG(*) > 1)
                THROW 51107, 'Stage migration aborted: duplicate model + line + stage assignments exist.', 1;

            IF EXISTS (
                SELECT [ProductModelId], [ProductionLineId], [StageOrder]
                FROM [ProductModelStages]
                GROUP BY [ProductModelId], [ProductionLineId], [StageOrder]
                HAVING COUNT_BIG(*) > 1)
                THROW 51108, 'Stage migration aborted: duplicate model + line stage orders exist.', 1;

            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_MainStages_ProductionLines_ProductionLineId')
                ALTER TABLE [MainStages] DROP CONSTRAINT [FK_MainStages_ProductionLines_ProductionLineId];
            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_SubStages_MainStages_MainStageId')
                ALTER TABLE [SubStages] DROP CONSTRAINT [FK_SubStages_MainStages_MainStageId];
            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_SubStages_ProductionLines_ProductionLineId')
                ALTER TABLE [SubStages] DROP CONSTRAINT [FK_SubStages_ProductionLines_ProductionLineId];

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[WorkerDefaultAssignments]') AND [name] = N'IX_WorkerDefaultAssignments_WorkerId_SubStageId')
                DROP INDEX [IX_WorkerDefaultAssignments_WorkerId_SubStageId] ON [WorkerDefaultAssignments];
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[SubStages]') AND [name] = N'IX_SubStages_Code')
                DROP INDEX [IX_SubStages_Code] ON [SubStages];
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[SubStages]') AND [name] = N'IX_SubStages_ProductionLineId')
                DROP INDEX [IX_SubStages_ProductionLineId] ON [SubStages];
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[ProductModelStages]') AND [name] = N'IX_ProductModelStages_ProductModelId_StageOrder')
                DROP INDEX [IX_ProductModelStages_ProductModelId_StageOrder] ON [ProductModelStages];
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[ProductModelStages]') AND [name] = N'IX_ProductModelStages_ProductModelId_SubStageId')
                DROP INDEX [IX_ProductModelStages_ProductModelId_SubStageId] ON [ProductModelStages];
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[MainStages]') AND [name] = N'IX_MainStages_ProductionLineId_SequenceOrder')
                DROP INDEX [IX_MainStages_ProductionLineId_SequenceOrder] ON [MainStages];

            IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[MainStages]') AND [name] = N'DepartmentId' AND [is_nullable] = 1)
                ALTER TABLE [MainStages] ALTER COLUMN [DepartmentId] uniqueidentifier NOT NULL;
            IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[SubStages]') AND [name] = N'DepartmentId' AND [is_nullable] = 1)
                ALTER TABLE [SubStages] ALTER COLUMN [DepartmentId] uniqueidentifier NOT NULL;
            IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[ProductModelStages]') AND [name] = N'ProductionLineId' AND [is_nullable] = 1)
                ALTER TABLE [ProductModelStages] ALTER COLUMN [ProductionLineId] uniqueidentifier NOT NULL;
            IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[WorkerDefaultAssignments]') AND [name] = N'ProductionLineId' AND [is_nullable] = 1)
                ALTER TABLE [WorkerDefaultAssignments] ALTER COLUMN [ProductionLineId] uniqueidentifier NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE [name] = N'AK_MainStages_Id_DepartmentId')
                ALTER TABLE [MainStages] ADD CONSTRAINT [AK_MainStages_Id_DepartmentId] UNIQUE ([Id], [DepartmentId]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[WorkerDefaultAssignments]') AND [name] = N'IX_WorkerDefaultAssignments_ProductionLineId_SubStageId')
                CREATE INDEX [IX_WorkerDefaultAssignments_ProductionLineId_SubStageId] ON [WorkerDefaultAssignments] ([ProductionLineId], [SubStageId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[WorkerDefaultAssignments]') AND [name] = N'IX_WorkerDefaultAssignments_WorkerId_ProductionLineId_SubStageId')
                CREATE UNIQUE INDEX [IX_WorkerDefaultAssignments_WorkerId_ProductionLineId_SubStageId] ON [WorkerDefaultAssignments] ([WorkerId], [ProductionLineId], [SubStageId]) WHERE [IsActive] = 1;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[SubStages]') AND [name] = N'IX_SubStages_DepartmentId_Code')
                CREATE UNIQUE INDEX [IX_SubStages_DepartmentId_Code] ON [SubStages] ([DepartmentId], [Code]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[SubStages]') AND [name] = N'IX_SubStages_MainStageId_DepartmentId')
                CREATE INDEX [IX_SubStages_MainStageId_DepartmentId] ON [SubStages] ([MainStageId], [DepartmentId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[ProductModelStages]') AND [name] = N'IX_ProductModelStages_ProductionLineId')
                CREATE INDEX [IX_ProductModelStages_ProductionLineId] ON [ProductModelStages] ([ProductionLineId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[ProductModelStages]') AND [name] = N'IX_ProductModelStages_ProductModelId_ProductionLineId_StageOrder')
                CREATE UNIQUE INDEX [IX_ProductModelStages_ProductModelId_ProductionLineId_StageOrder] ON [ProductModelStages] ([ProductModelId], [ProductionLineId], [StageOrder]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[ProductModelStages]') AND [name] = N'IX_ProductModelStages_ProductModelId_ProductionLineId_SubStageId')
                CREATE UNIQUE INDEX [IX_ProductModelStages_ProductModelId_ProductionLineId_SubStageId] ON [ProductModelStages] ([ProductModelId], [ProductionLineId], [SubStageId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[MainStages]') AND [name] = N'IX_MainStages_DepartmentId_Name')
                CREATE UNIQUE INDEX [IX_MainStages_DepartmentId_Name] ON [MainStages] ([DepartmentId], [Name]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[MainStages]') AND [name] = N'IX_MainStages_DepartmentId_SequenceOrder')
                CREATE INDEX [IX_MainStages_DepartmentId_SequenceOrder] ON [MainStages] ([DepartmentId], [SequenceOrder]);

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_MainStages_Departments_DepartmentId')
                ALTER TABLE [MainStages] WITH CHECK ADD CONSTRAINT [FK_MainStages_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]);
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ProductModelStages_ProductionLines_ProductionLineId')
                ALTER TABLE [ProductModelStages] WITH CHECK ADD CONSTRAINT [FK_ProductModelStages_ProductionLines_ProductionLineId] FOREIGN KEY ([ProductionLineId]) REFERENCES [ProductionLines] ([Id]);
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_SubStages_MainStages_MainStageId_DepartmentId')
                ALTER TABLE [SubStages] WITH CHECK ADD CONSTRAINT [FK_SubStages_MainStages_MainStageId_DepartmentId] FOREIGN KEY ([MainStageId], [DepartmentId]) REFERENCES [MainStages] ([Id], [DepartmentId]);
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_WorkerDefaultAssignments_ProductionLines_ProductionLineId')
                ALTER TABLE [WorkerDefaultAssignments] WITH CHECK ADD CONSTRAINT [FK_WorkerDefaultAssignments_ProductionLines_ProductionLineId] FOREIGN KEY ([ProductionLineId]) REFERENCES [ProductionLines] ([Id]);

            IF @subHasLegacyLine = 1
                EXEC(N'ALTER TABLE [SubStages] DROP COLUMN [ProductionLineId];');
            IF @mainHasLegacyLine = 1
                EXEC(N'ALTER TABLE [MainStages] DROP COLUMN [ProductionLineId];');

            EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_ProductModelStages_DepartmentGuard]
            ON [dbo].[ProductModelStages]
            AFTER INSERT, UPDATE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1
                    FROM inserted AS i
                    LEFT JOIN [dbo].[ProductionLines] AS l ON l.Id = i.ProductionLineId
                    LEFT JOIN [dbo].[SubStages] AS s ON s.Id = i.SubStageId
                    WHERE l.Id IS NULL OR s.Id IS NULL OR l.DepartmentId IS NULL OR l.DepartmentId <> s.DepartmentId)
                    THROW 51120, ''ProductModelStage line and SubStage must belong to the same department.'', 1;
            END');

            EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_ProductionLines_ProductModelStageDepartmentGuard]
            ON [dbo].[ProductionLines]
            AFTER UPDATE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1
                    FROM inserted AS i
                    INNER JOIN [dbo].[ProductModelStages] AS pms ON pms.ProductionLineId = i.Id
                    INNER JOIN [dbo].[SubStages] AS s ON s.Id = pms.SubStageId
                    WHERE i.DepartmentId IS NULL OR i.DepartmentId <> s.DepartmentId)
                    THROW 51121, ''A line with model-stage assignments cannot move to another department.'', 1;
            END');

            EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_SubStages_ProductModelStageDepartmentGuard]
            ON [dbo].[SubStages]
            AFTER UPDATE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1
                    FROM inserted AS i
                    INNER JOIN [dbo].[ProductModelStages] AS pms ON pms.SubStageId = i.Id
                    INNER JOIN [dbo].[ProductionLines] AS l ON l.Id = pms.ProductionLineId
                    WHERE l.DepartmentId IS NULL OR l.DepartmentId <> i.DepartmentId)
                    THROW 51122, ''An assigned SubStage cannot move to a different department than its line.'', 1;
            END');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS [dbo].[TR_ProductModelStages_DepartmentGuard];
            DROP TRIGGER IF EXISTS [dbo].[TR_ProductionLines_ProductModelStageDepartmentGuard];
            DROP TRIGGER IF EXISTS [dbo].[TR_SubStages_ProductModelStageDepartmentGuard];

            IF EXISTS (
                SELECT [ProductModelId], [SubStageId]
                FROM [ProductModelStages]
                GROUP BY [ProductModelId], [SubStageId]
                HAVING COUNT_BIG(*) > 1)
                THROW 51130, 'Rollback aborted: line-specific model assignments cannot fit the legacy model + stage key.', 1;

            IF EXISTS (
                SELECT [ProductModelId], [StageOrder]
                FROM [ProductModelStages]
                GROUP BY [ProductModelId], [StageOrder]
                HAVING COUNT_BIG(*) > 1)
                THROW 51133, 'Rollback aborted: line-specific stage orders cannot fit the legacy model order key.', 1;

            IF EXISTS (
                SELECT [WorkerId], [SubStageId]
                FROM [WorkerDefaultAssignments]
                WHERE [IsActive] = 1
                GROUP BY [WorkerId], [SubStageId]
                HAVING COUNT_BIG(*) > 1)
                THROW 51134, 'Rollback aborted: line-specific worker defaults cannot fit the legacy active assignment key.', 1;

            IF EXISTS (SELECT [Code] FROM [SubStages] GROUP BY [Code] HAVING COUNT_BIG(*) > 1)
                THROW 51135, 'Rollback aborted: department-scoped SubStage codes cannot fit the legacy global code key.', 1;

            IF EXISTS (
                SELECT [map].[ProductionLineId], [m].[SequenceOrder]
                FROM [MainStages] AS [m]
                INNER JOIN [StageOwnershipMigrationRollbackMap] AS [map] ON [map].[StageKind] = 1 AND [map].[StageId] = [m].[Id]
                GROUP BY [map].[ProductionLineId], [m].[SequenceOrder]
                HAVING COUNT_BIG(*) > 1)
                THROW 51136, 'Rollback aborted: department stage ordering cannot fit the legacy line order key.', 1;

            IF EXISTS (
                SELECT 1
                FROM [SubStages] AS [s]
                INNER JOIN [StageOwnershipMigrationRollbackMap] AS [subMap] ON [subMap].[StageKind] = 2 AND [subMap].[StageId] = [s].[Id]
                INNER JOIN [StageOwnershipMigrationRollbackMap] AS [mainMap] ON [mainMap].[StageKind] = 1 AND [mainMap].[StageId] = [s].[MainStageId]
                WHERE [subMap].[ProductionLineId] <> [mainMap].[ProductionLineId])
                THROW 51137, 'Rollback aborted: a SubStage moved between legacy line-owned MainStages.', 1;

            IF EXISTS (SELECT 1 FROM [MainStages] AS [m] LEFT JOIN [StageOwnershipMigrationRollbackMap] AS [map] ON [map].[StageKind] = 1 AND [map].[StageId] = [m].[Id] WHERE [map].[StageId] IS NULL)
               OR EXISTS (SELECT 1 FROM [SubStages] AS [s] LEFT JOIN [StageOwnershipMigrationRollbackMap] AS [map] ON [map].[StageKind] = 2 AND [map].[StageId] = [s].[Id] WHERE [map].[StageId] IS NULL)
                THROW 51131, 'Rollback aborted: stages created after migration have no legacy line owner.', 1;
            """);

        migrationBuilder.DropForeignKey(name: "FK_MainStages_Departments_DepartmentId", table: "MainStages");
        migrationBuilder.DropForeignKey(name: "FK_ProductModelStages_ProductionLines_ProductionLineId", table: "ProductModelStages");
        migrationBuilder.DropForeignKey(name: "FK_SubStages_MainStages_MainStageId_DepartmentId", table: "SubStages");
        migrationBuilder.DropForeignKey(name: "FK_WorkerDefaultAssignments_ProductionLines_ProductionLineId", table: "WorkerDefaultAssignments");
        migrationBuilder.DropIndex(name: "IX_WorkerDefaultAssignments_ProductionLineId_SubStageId", table: "WorkerDefaultAssignments");
        migrationBuilder.DropIndex(name: "IX_WorkerDefaultAssignments_WorkerId_ProductionLineId_SubStageId", table: "WorkerDefaultAssignments");
        migrationBuilder.DropIndex(name: "IX_SubStages_DepartmentId_Code", table: "SubStages");
        migrationBuilder.DropIndex(name: "IX_SubStages_MainStageId_DepartmentId", table: "SubStages");
        migrationBuilder.DropIndex(name: "IX_ProductModelStages_ProductionLineId", table: "ProductModelStages");
        migrationBuilder.DropIndex(name: "IX_ProductModelStages_ProductModelId_ProductionLineId_StageOrder", table: "ProductModelStages");
        migrationBuilder.DropIndex(name: "IX_ProductModelStages_ProductModelId_ProductionLineId_SubStageId", table: "ProductModelStages");
        migrationBuilder.DropUniqueConstraint(name: "AK_MainStages_Id_DepartmentId", table: "MainStages");
        migrationBuilder.DropIndex(name: "IX_MainStages_DepartmentId_Name", table: "MainStages");
        migrationBuilder.DropIndex(name: "IX_MainStages_DepartmentId_SequenceOrder", table: "MainStages");

        migrationBuilder.Sql("""
            ALTER TABLE [MainStages] ADD [ProductionLineId] uniqueidentifier NULL;
            ALTER TABLE [SubStages] ADD [ProductionLineId] uniqueidentifier NULL;

            -- Compile all references to the newly added legacy columns only after
            -- ALTER TABLE has executed. This keeps generated Down SQL batch-safe.
            EXEC(N'
                UPDATE [m]
                SET [ProductionLineId] = [map].[ProductionLineId]
                FROM [MainStages] AS [m]
                INNER JOIN [StageOwnershipMigrationRollbackMap] AS [map] ON [map].[StageKind] = 1 AND [map].[StageId] = [m].[Id];

                UPDATE [s]
                SET [ProductionLineId] = [map].[ProductionLineId]
                FROM [SubStages] AS [s]
                INNER JOIN [StageOwnershipMigrationRollbackMap] AS [map] ON [map].[StageKind] = 2 AND [map].[StageId] = [s].[Id];

                IF EXISTS (SELECT 1 FROM [MainStages] WHERE [ProductionLineId] IS NULL)
                   OR EXISTS (SELECT 1 FROM [SubStages] WHERE [ProductionLineId] IS NULL)
                    THROW 51132, ''Rollback aborted: a legacy line owner could not be restored.'', 1;

                ALTER TABLE [MainStages] ALTER COLUMN [ProductionLineId] uniqueidentifier NOT NULL;
                ALTER TABLE [SubStages] ALTER COLUMN [ProductionLineId] uniqueidentifier NOT NULL;

                CREATE UNIQUE INDEX [IX_MainStages_ProductionLineId_SequenceOrder]
                    ON [MainStages] ([ProductionLineId], [SequenceOrder]);
                CREATE INDEX [IX_SubStages_ProductionLineId]
                    ON [SubStages] ([ProductionLineId]);

                ALTER TABLE [MainStages] ADD CONSTRAINT [FK_MainStages_ProductionLines_ProductionLineId]
                    FOREIGN KEY ([ProductionLineId]) REFERENCES [ProductionLines] ([Id]) ON DELETE NO ACTION;
                ALTER TABLE [SubStages] ADD CONSTRAINT [FK_SubStages_ProductionLines_ProductionLineId]
                    FOREIGN KEY ([ProductionLineId]) REFERENCES [ProductionLines] ([Id]) ON DELETE NO ACTION;');
            """);

        migrationBuilder.CreateIndex(name: "IX_WorkerDefaultAssignments_WorkerId_SubStageId", table: "WorkerDefaultAssignments", columns: new[] { "WorkerId", "SubStageId" }, unique: true, filter: "[IsActive] = 1");
        migrationBuilder.CreateIndex(name: "IX_SubStages_Code", table: "SubStages", column: "Code", unique: true);
        migrationBuilder.CreateIndex(name: "IX_ProductModelStages_ProductModelId_StageOrder", table: "ProductModelStages", columns: new[] { "ProductModelId", "StageOrder" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_ProductModelStages_ProductModelId_SubStageId", table: "ProductModelStages", columns: new[] { "ProductModelId", "SubStageId" }, unique: true);
        migrationBuilder.AddForeignKey(name: "FK_SubStages_MainStages_MainStageId", table: "SubStages", column: "MainStageId", principalTable: "MainStages", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }
}
