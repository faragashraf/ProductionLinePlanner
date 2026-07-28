using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProductionLinePlanner.Infrastructure.Data;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260728172938_AddDepartmentStageAndLineAssignmentKeys")]
public sealed class AddDepartmentStageAndLineAssignmentKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep this first migration additive and safe to resume. Some development
        // databases were partially prepared while the original single migration
        // was being reviewed, but do not have an EF history row for that work.
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[MainStages]', N'DepartmentId') IS NULL
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[MainStages] ADD [DepartmentId] uniqueidentifier NULL;';
            """);

        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[SubStages]', N'DepartmentId') IS NULL
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[SubStages] ADD [DepartmentId] uniqueidentifier NULL;';
            """);

        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[ProductModelStages]', N'ProductionLineId') IS NULL
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[ProductModelStages] ADD [ProductionLineId] uniqueidentifier NULL;';
            """);

        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[WorkerDefaultAssignments]', N'ProductionLineId') IS NULL
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[WorkerDefaultAssignments] ADD [ProductionLineId] uniqueidentifier NULL;';
            """);

        // Retained until rollback of the pair so Down() can restore the exact
        // legacy line owner rather than guessing when a department has many lines.
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[StageOwnershipMigrationRollbackMap]', N'U') IS NULL
                EXEC sys.sp_executesql N'
                    CREATE TABLE [dbo].[StageOwnershipMigrationRollbackMap]
                    (
                        [StageKind] tinyint NOT NULL,
                        [StageId] uniqueidentifier NOT NULL,
                        [ProductionLineId] uniqueidentifier NOT NULL,
                        CONSTRAINT [PK_StageOwnershipMigrationRollbackMap]
                            PRIMARY KEY ([StageKind], [StageId])
                    );';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[StageOwnershipMigrationRollbackMap]', N'U') IS NOT NULL
                DROP TABLE [dbo].[StageOwnershipMigrationRollbackMap];
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[WorkerDefaultAssignments]', N'ProductionLineId') IS NOT NULL
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[WorkerDefaultAssignments] DROP COLUMN [ProductionLineId];';
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[ProductModelStages]', N'ProductionLineId') IS NOT NULL
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[ProductModelStages] DROP COLUMN [ProductionLineId];';
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[SubStages]', N'DepartmentId') IS NOT NULL
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[SubStages] DROP COLUMN [DepartmentId];';
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[MainStages]', N'DepartmentId') IS NOT NULL
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[MainStages] DROP COLUMN [DepartmentId];';
            """);
    }
}
