using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowWorkerMultiStageParticipation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkerDefaultAssignments_WorkerId",
                table: "WorkerDefaultAssignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "FromSubStageId",
                table: "WorkerTemporaryAssignments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "ParticipationMode",
                table: "WorkerTemporaryAssignments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                // Existing temporary rows were all created as moves under the
                // original model; retain that behavior during conversion.
                defaultValue: "TemporaryMove");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerDefaultAssignments_WorkerId_SubStageId",
                table: "WorkerDefaultAssignments",
                columns: new[] { "WorkerId", "SubStageId" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkerDefaultAssignments_WorkerId_SubStageId",
                table: "WorkerDefaultAssignments");

            migrationBuilder.DropColumn(
                name: "ParticipationMode",
                table: "WorkerTemporaryAssignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "FromSubStageId",
                table: "WorkerTemporaryAssignments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkerDefaultAssignments_WorkerId",
                table: "WorkerDefaultAssignments",
                column: "WorkerId",
                unique: true,
                filter: "[IsActive] = 1");
        }
    }
}
