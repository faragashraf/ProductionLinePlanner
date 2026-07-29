using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalReadinessAttendanceSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceSyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OperationalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSuccessfulAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAttemptSucceeded = table.Column<bool>(type: "bit", nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSyncStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSyncStates_SourceName_OperationalDate",
                table: "AttendanceSyncStates",
                columns: new[] { "SourceName", "OperationalDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceSyncStates");
        }
    }
}
