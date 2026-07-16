using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations;

/// <summary>
/// Minimal, backward-safe schema support for the first manual production pilot.
/// It intentionally contains no import-batch or workbook-import tables.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260715000000_AddPilotManualProductionReadiness")]
public partial class AddPilotManualProductionReadiness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LocalDepartmentName",
            table: "Workers",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "InputQuantity",
            table: "StageProductionWorkerAllocations",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ApprovedAtUtc",
            table: "ProductionOrders",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ApprovedBy",
            table: "ProductionOrders",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "RecordedAtUtc",
            table: "ProductionOrders",
            type: "datetime2",
            nullable: true);

        migrationBuilder.Sql("UPDATE [ProductionOrders] SET [RecordedAtUtc] = [CreatedAtUtc] WHERE [RecordedAtUtc] IS NULL;");

        migrationBuilder.AlterColumn<DateTime>(
            name: "RecordedAtUtc",
            table: "ProductionOrders",
            type: "datetime2",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        // Nullable provenance fields keep the current manual-production model runnable.
        // The deferred generic-import migration adds its batch table and FK later.
        migrationBuilder.AddColumn<Guid>(
            name: "SourceImportBatchId",
            table: "ProductionOrders",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SourceReference",
            table: "ProductionOrders",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1
                FROM [ProductionOrders]
                WHERE [ProductionLineId] IS NOT NULL
                GROUP BY [ProductionDate], [ProductionLineId], [ProductModelId]
                HAVING COUNT(*) > 1)
            THROW 51000, 'Cannot add the manual-production line/day/product uniqueness rule while duplicate aggregates exist.', 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_ProductionOrders_ProductionDate_ProductionLineId_ProductModelId",
            table: "ProductionOrders",
            columns: new[] { "ProductionDate", "ProductionLineId", "ProductModelId" },
            unique: true,
            filter: "[ProductionLineId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ProductionOrders_ProductionDate_ProductionLineId_ProductModelId",
            table: "ProductionOrders");

        migrationBuilder.DropColumn(name: "LocalDepartmentName", table: "Workers");
        migrationBuilder.DropColumn(name: "InputQuantity", table: "StageProductionWorkerAllocations");
        migrationBuilder.DropColumn(name: "ApprovedAtUtc", table: "ProductionOrders");
        migrationBuilder.DropColumn(name: "ApprovedBy", table: "ProductionOrders");
        migrationBuilder.DropColumn(name: "RecordedAtUtc", table: "ProductionOrders");
        migrationBuilder.DropColumn(name: "SourceImportBatchId", table: "ProductionOrders");
        migrationBuilder.DropColumn(name: "SourceReference", table: "ProductionOrders");
    }
}
