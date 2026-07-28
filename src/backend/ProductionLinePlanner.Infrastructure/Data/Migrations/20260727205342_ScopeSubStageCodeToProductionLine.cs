using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeSubStageCodeToProductionLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubStages_Code",
                table: "SubStages");

            migrationBuilder.CreateIndex(
                name: "IX_SubStages_ProductionLineId_Code",
                table: "SubStages",
                columns: new[] { "ProductionLineId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubStages_ProductionLineId_Code",
                table: "SubStages");

            migrationBuilder.CreateIndex(
                name: "IX_SubStages_Code",
                table: "SubStages",
                column: "Code",
                unique: true);
        }
    }
}
