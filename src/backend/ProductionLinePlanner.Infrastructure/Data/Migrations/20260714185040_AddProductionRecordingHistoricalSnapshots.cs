using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionRecordingHistoricalSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SnapshotWorkerCode",
                table: "StageProductionWorkerAllocations",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotWorkerName",
                table: "StageProductionWorkerAllocations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotFactoryCode",
                table: "StageProductionRecords",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotFactoryName",
                table: "StageProductionRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotMainStageName",
                table: "StageProductionRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotProductionLineCode",
                table: "StageProductionRecords",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotProductionLineName",
                table: "StageProductionRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Existing records predate these fields. Their financial inputs were
            // already snapshotted; this backfill preserves the best available
            // participant and hierarchy identity without creating new records.
            migrationBuilder.Sql("""
                UPDATE allocation
                SET SnapshotWorkerCode = worker.EmployeeCode,
                    SnapshotWorkerName = worker.FullName
                FROM StageProductionWorkerAllocations allocation
                INNER JOIN Workers worker ON worker.Id = allocation.WorkerId;
                """);

            migrationBuilder.Sql("""
                UPDATE record
                SET SnapshotFactoryCode = factory.Code,
                    SnapshotFactoryName = factory.Name,
                    SnapshotProductionLineCode = COALESCE(line.LineCode, ''),
                    SnapshotProductionLineName = line.Name,
                    SnapshotMainStageName = mainStage.Name
                FROM StageProductionRecords record
                INNER JOIN ProductModelStages modelStage ON modelStage.Id = record.ProductModelStageId
                INNER JOIN SubStages subStage ON subStage.Id = modelStage.SubStageId
                INNER JOIN MainStages mainStage ON mainStage.Id = subStage.MainStageId
                INNER JOIN ProductionLines line ON line.Id = mainStage.ProductionLineId
                INNER JOIN Factories factory ON factory.Id = line.FactoryId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SnapshotWorkerCode",
                table: "StageProductionWorkerAllocations");

            migrationBuilder.DropColumn(
                name: "SnapshotWorkerName",
                table: "StageProductionWorkerAllocations");

            migrationBuilder.DropColumn(
                name: "SnapshotFactoryCode",
                table: "StageProductionRecords");

            migrationBuilder.DropColumn(
                name: "SnapshotFactoryName",
                table: "StageProductionRecords");

            migrationBuilder.DropColumn(
                name: "SnapshotMainStageName",
                table: "StageProductionRecords");

            migrationBuilder.DropColumn(
                name: "SnapshotProductionLineCode",
                table: "StageProductionRecords");

            migrationBuilder.DropColumn(
                name: "SnapshotProductionLineName",
                table: "StageProductionRecords");
        }
    }
}
