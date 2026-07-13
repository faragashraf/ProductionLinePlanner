using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueCurrentWorkerSalary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkerSalaryHistories_Current",
                table: "WorkerSalaryHistories");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSalaryHistories_Current",
                table: "WorkerSalaryHistories",
                columns: new[] { "WorkerId", "EffectiveTo" },
                unique: true,
                filter: "[EffectiveTo] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkerSalaryHistories_Current",
                table: "WorkerSalaryHistories");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSalaryHistories_Current",
                table: "WorkerSalaryHistories",
                columns: new[] { "WorkerId", "EffectiveTo" },
                filter: "[EffectiveTo] IS NULL");
        }
    }
}
