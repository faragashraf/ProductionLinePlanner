using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddControlledRealDataIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StageProductionRecords_ProductionOrderId_ProductModelStageId",
                table: "StageProductionRecords");

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionDayStageResolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductModelStageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ResolvedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionDayStageResolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionDayStageResolutions_ProductModelStages_ProductModelStageId",
                        column: x => x.ProductModelStageId,
                        principalTable: "ProductModelStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionDayStageResolutions_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_SourceImportBatchId",
                table: "ProductionOrders",
                column: "SourceImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StageProductionRecords_ProductionOrderId_ProductModelStageId",
                table: "StageProductionRecords",
                columns: new[] { "ProductionOrderId", "ProductModelStageId" },
                unique: false);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_IdempotencyKey",
                table: "ImportBatches",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDayStageResolutions_ProductionOrderId_ProductModelStageId",
                table: "ProductionDayStageResolutions",
                columns: new[] { "ProductionOrderId", "ProductModelStageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDayStageResolutions_ProductModelStageId",
                table: "ProductionDayStageResolutions",
                column: "ProductModelStageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrders_ImportBatches_SourceImportBatchId",
                table: "ProductionOrders",
                column: "SourceImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrders_ImportBatches_SourceImportBatchId",
                table: "ProductionOrders");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "ProductionDayStageResolutions");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_SourceImportBatchId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_StageProductionRecords_ProductionOrderId_ProductModelStageId",
                table: "StageProductionRecords");

            migrationBuilder.CreateIndex(
                name: "IX_StageProductionRecords_ProductionOrderId_ProductModelStageId",
                table: "StageProductionRecords",
                columns: new[] { "ProductionOrderId", "ProductModelStageId" });

        }
    }
}
