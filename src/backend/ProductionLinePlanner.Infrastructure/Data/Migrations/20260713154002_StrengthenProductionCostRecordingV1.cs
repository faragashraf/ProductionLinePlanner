using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StrengthenProductionCostRecordingV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "StageProductionRecords",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "StageProductionRecords",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "SnapshotProductModelCode",
                table: "StageProductionRecords",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotProductModelName",
                table: "StageProductionRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("UPDATE ProductionOrders SET ConcurrencyToken = NEWID();");
            migrationBuilder.Sql("UPDATE spr SET ClientRequestId = NEWID(), ConcurrencyToken = NEWID(), SnapshotProductModelCode = pm.Code, SnapshotProductModelName = pm.Name FROM StageProductionRecords spr INNER JOIN ProductionOrders po ON po.Id = spr.ProductionOrderId INNER JOIN ProductModels pm ON pm.Id = po.ProductModelId;");

            migrationBuilder.CreateIndex(
                name: "IX_StageProductionRecords_ProductionOrderId_ClientRequestId",
                table: "StageProductionRecords",
                columns: new[] { "ProductionOrderId", "ClientRequestId" },
                unique: true,
                filter: "[ClientRequestId] <> '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StageProductionRecords_ProductionOrderId_ClientRequestId",
                table: "StageProductionRecords");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "StageProductionRecords");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "StageProductionRecords");

            migrationBuilder.DropColumn(
                name: "SnapshotProductModelCode",
                table: "StageProductionRecords");

            migrationBuilder.DropColumn(
                name: "SnapshotProductModelName",
                table: "StageProductionRecords");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "ProductionOrders");
        }
    }
}
