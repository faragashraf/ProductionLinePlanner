using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CorrectDepartmentLineStageHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FactoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SequenceOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Departments_Factories_FactoryId",
                        column: x => x.FactoryId,
                        principalTable: "Factories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Legacy lines are deliberately left unassigned.  There is no default,
            // name matching, or inference from attendance departments.
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "ProductionLines",
                type: "uniqueidentifier",
                nullable: true);

            // Add nullable first so existing operational records can be validated
            // and deterministically backfilled without a zero-GUID default.
            migrationBuilder.AddColumn<Guid>(
                name: "ProductionLineId",
                table: "SubStages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM [SubStages] AS [s]
    LEFT JOIN [MainStages] AS [m] ON [m].[Id] = [s].[MainStageId]
    LEFT JOIN [ProductionLines] AS [l] ON [l].[Id] = [m].[ProductionLineId]
    WHERE [m].[Id] IS NULL OR [l].[Id] IS NULL)
BEGIN
    THROW 51001, 'CorrectDepartmentLineStageHierarchy aborted: every SubStage must reference a valid MainStage and ProductionLine before backfill.', 1;
END;

UPDATE [s]
SET [ProductionLineId] = [m].[ProductionLineId]
FROM [SubStages] AS [s]
INNER JOIN [MainStages] AS [m] ON [m].[Id] = [s].[MainStageId];

IF EXISTS (
    SELECT 1
    FROM [SubStages] AS [s]
    INNER JOIN [MainStages] AS [m] ON [m].[Id] = [s].[MainStageId]
    WHERE [s].[ProductionLineId] IS NULL OR [s].[ProductionLineId] <> [m].[ProductionLineId])
BEGIN
    THROW 51002, 'CorrectDepartmentLineStageHierarchy aborted: SubStage production-line backfill is inconsistent with MainStage.', 1;
END;

IF EXISTS (SELECT 1 FROM [SubStages] WHERE [ProductionLineId] IS NULL)
BEGIN
    THROW 51003, 'CorrectDepartmentLineStageHierarchy aborted: a SubStage remains without ProductionLineId after backfill.', 1;
END;
");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductionLineId",
                table: "SubStages",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubStages_ProductionLineId",
                table: "SubStages",
                column: "ProductionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLines_DepartmentId",
                table: "ProductionLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_FactoryId_Code",
                table: "Departments",
                columns: new[] { "FactoryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_FactoryId_SequenceOrder",
                table: "Departments",
                columns: new[] { "FactoryId", "SequenceOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionLines_Departments_DepartmentId",
                table: "ProductionLines",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // STG numbers are allocated only for new operational stages. Existing
            // codes are never modified. The predicate accepts exactly STG<number>.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StageCodeSequence]', N'SO') IS NULL
BEGIN
    DECLARE @nextValue bigint = ISNULL(
        (
            SELECT MAX(TRY_CONVERT(bigint, SUBSTRING([Code], 4, 8000)))
            FROM [SubStages]
            WHERE LEN([Code]) > 3
              AND UPPER(LEFT([Code], 3)) = N'STG'
              AND SUBSTRING([Code], 4, 8000) NOT LIKE N'%[^0-9]%'
        ), 0) + 1;
    DECLARE @sequenceSql nvarchar(max) =
        N'CREATE SEQUENCE [StageCodeSequence] AS bigint START WITH ' + CONVERT(nvarchar(30), @nextValue) + N' INCREMENT BY 1';
    EXEC sp_executesql @sequenceSql;
END;
");

            migrationBuilder.AddForeignKey(
                name: "FK_SubStages_ProductionLines_ProductionLineId",
                table: "SubStages",
                column: "ProductionLineId",
                principalTable: "ProductionLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A rollback must not silently erase operational department mapping.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Departments])
   OR EXISTS (SELECT 1 FROM [ProductionLines] WHERE [DepartmentId] IS NOT NULL)
BEGIN
    THROW 51004, 'CorrectDepartmentLineStageHierarchy cannot be rolled back while local departments or manual line mappings exist.', 1;
END;
");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StageCodeSequence]', N'SO') IS NOT NULL
    DROP SEQUENCE [StageCodeSequence];
");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionLines_Departments_DepartmentId",
                table: "ProductionLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SubStages_ProductionLines_ProductionLineId",
                table: "SubStages");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_SubStages_ProductionLineId",
                table: "SubStages");

            migrationBuilder.DropIndex(
                name: "IX_ProductionLines_DepartmentId",
                table: "ProductionLines");

            migrationBuilder.DropColumn(
                name: "ProductionLineId",
                table: "SubStages");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "ProductionLines");
        }
    }
}
