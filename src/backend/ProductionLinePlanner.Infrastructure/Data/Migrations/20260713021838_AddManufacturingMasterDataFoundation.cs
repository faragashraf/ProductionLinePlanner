using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingMasterDataFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttendanceDepartmentId",
                table: "Workers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmploymentEndDate",
                table: "Workers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmploymentStatus",
                table: "Workers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastExternalSyncAt",
                table: "Workers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoReference",
                table: "Workers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "SubStages",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                collation: "SQL_Latin1_General_CP1_CI_AS");

            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.Sql("""
                ;WITH MaxOrderByMainStage AS
                (
                    SELECT
                        [MainStageId],
                        MAX([SequenceOrder]) AS [CurrentMaxOrder]
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

                ;WITH SequenceOrderCollisionCheck AS
                (
                    SELECT [MainStageId], [SequenceOrder], COUNT(*) AS [RowsPerOrder]
                    FROM [dbo].[SubStages]
                    GROUP BY [MainStageId], [SequenceOrder]
                    HAVING COUNT(*) > 1
                )
                IF EXISTS (SELECT 1 FROM SequenceOrderCollisionCheck)
                    THROW 51002, 'SubStage SequenceOrder remediation produced duplicate order values within a MainStage.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[SubStages]
                    WHERE [SequenceOrder] <= 0
                )
                    THROW 51001, 'SubStage SequenceOrder remediation could not guarantee positive values.', 1;
                """);

            migrationBuilder.CreateTable(
                name: "ProductModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkerSalaryHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "EGP"),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerSalaryHistories", x => x.Id);
                    table.CheckConstraint("CK_WorkerSalaryHistory_Amount_NonNegative", "[Amount] >= 0");
                    table.CheckConstraint("CK_WorkerSalaryHistory_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_WorkerSalaryHistories_Workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "Workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductModelStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubStageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageOrder = table.Column<int>(type: "int", nullable: false),
                    PiecePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StandardSeconds = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    CompensationMode = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductModelStages", x => x.Id);
                    table.CheckConstraint("CK_ProductModelStage_PiecePrice_NonNegative", "[PiecePrice] >= 0");
                    table.CheckConstraint("CK_ProductModelStage_StageOrder_Positive", "[StageOrder] > 0");
                    table.CheckConstraint("CK_ProductModelStage_StandardSeconds_Positive", "[StandardSeconds] IS NULL OR [StandardSeconds] > 0");
                    table.ForeignKey(
                        name: "FK_ProductModelStages_ProductModels_ProductModelId",
                        column: x => x.ProductModelId,
                        principalTable: "ProductModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductModelStages_SubStages_SubStageId",
                        column: x => x.SubStageId,
                        principalTable: "SubStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubStages_Code",
                table: "SubStages",
                column: "Code",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubStage_DefaultOrder_Positive",
                table: "SubStages",
                sql: "[SequenceOrder] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductModels_Code",
                table: "ProductModels",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductModelStages_ProductModelId_StageOrder",
                table: "ProductModelStages",
                columns: new[] { "ProductModelId", "StageOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductModelStages_ProductModelId_SubStageId",
                table: "ProductModelStages",
                columns: new[] { "ProductModelId", "SubStageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductModelStages_SubStageId",
                table: "ProductModelStages",
                column: "SubStageId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSalaryHistories_Current",
                table: "WorkerSalaryHistories",
                columns: new[] { "WorkerId", "EffectiveTo" },
                unique: true,
                filter: "[EffectiveTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSalaryHistories_WorkerId_EffectiveFrom",
                table: "WorkerSalaryHistories",
                columns: new[] { "WorkerId", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductModelStages");

            migrationBuilder.DropTable(
                name: "WorkerSalaryHistories");

            migrationBuilder.DropTable(
                name: "ProductModels");

            migrationBuilder.DropIndex(
                name: "IX_SubStages_Code",
                table: "SubStages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubStage_DefaultOrder_Positive",
                table: "SubStages");

            migrationBuilder.DropColumn(
                name: "AttendanceDepartmentId",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "EmploymentEndDate",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "EmploymentStatus",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "LastExternalSyncAt",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "PhotoReference",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "SubStages");
        }
    }
}
