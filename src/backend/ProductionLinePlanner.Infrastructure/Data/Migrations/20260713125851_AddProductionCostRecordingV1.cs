using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    public partial class AddProductionCostRecordingV1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ProductModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrders", x => x.Id);
                    table.ForeignKey(name: "FK_ProductionOrders_ProductModels_ProductModelId", column: x => x.ProductModelId, principalTable: "ProductModels", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ProductionOrders_ProductionLines_ProductionLineId", column: x => x.ProductionLineId, principalTable: "ProductionLines", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StageProductionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductModelStageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProducedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    AcceptedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SnapshotStageCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SnapshotStageName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SnapshotPiecePrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SnapshotStandardSeconds = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    SnapshotCompensationMode = table.Column<int>(type: "int", nullable: false),
                    TotalWorkerEarnings = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageProductionRecords", x => x.Id);
                    table.ForeignKey(name: "FK_StageProductionRecords_ProductModelStages_ProductModelStageId", column: x => x.ProductModelStageId, principalTable: "ProductModelStages", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_StageProductionRecords_ProductionOrders_ProductionOrderId", column: x => x.ProductionOrderId, principalTable: "ProductionOrders", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StageProductionWorkerAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageProductionRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    FixedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    EquivalentQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CalculatedEarning = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageProductionWorkerAllocations", x => x.Id);
                    table.ForeignKey(name: "FK_StageProductionWorkerAllocations_StageProductionRecords_StageProductionRecordId", column: x => x.StageProductionRecordId, principalTable: "StageProductionRecords", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_StageProductionWorkerAllocations_Workers_WorkerId", column: x => x.WorkerId, principalTable: "Workers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_ProductionOrders_OrderNumber", table: "ProductionOrders", column: "OrderNumber", unique: true);
            migrationBuilder.CreateIndex(name: "IX_ProductionOrders_ProductionDate_Status", table: "ProductionOrders", columns: new[] { "ProductionDate", "Status" });
            migrationBuilder.CreateIndex(name: "IX_ProductionOrders_ProductionLineId", table: "ProductionOrders", column: "ProductionLineId");
            migrationBuilder.CreateIndex(name: "IX_ProductionOrders_ProductModelId", table: "ProductionOrders", column: "ProductModelId");
            migrationBuilder.CreateIndex(name: "IX_StageProductionRecords_ProductionDate_Status", table: "StageProductionRecords", columns: new[] { "ProductionDate", "Status" });
            migrationBuilder.CreateIndex(name: "IX_StageProductionRecords_ProductionOrderId_ProductModelStageId", table: "StageProductionRecords", columns: new[] { "ProductionOrderId", "ProductModelStageId" });
            migrationBuilder.CreateIndex(name: "IX_StageProductionRecords_ProductModelStageId", table: "StageProductionRecords", column: "ProductModelStageId");
            migrationBuilder.CreateIndex(name: "IX_StageProductionWorkerAllocations_StageProductionRecordId_WorkerId", table: "StageProductionWorkerAllocations", columns: new[] { "StageProductionRecordId", "WorkerId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_StageProductionWorkerAllocations_WorkerId", table: "StageProductionWorkerAllocations", column: "WorkerId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StageProductionWorkerAllocations");
            migrationBuilder.DropTable(name: "StageProductionRecords");
            migrationBuilder.DropTable(name: "ProductionOrders");
        }
    }
}
