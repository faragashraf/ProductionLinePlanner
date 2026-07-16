using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryAssignmentOverlapLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WorkerTemporaryAssignments_WorkerId_Status_StartAtUtc_EndAtUtc",
                table: "WorkerTemporaryAssignments",
                columns: new[] { "WorkerId", "Status", "StartAtUtc", "EndAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkerTemporaryAssignments_WorkerId_Status_StartAtUtc_EndAtUtc",
                table: "WorkerTemporaryAssignments");
        }
    }
}
