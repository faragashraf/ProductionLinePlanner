using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssignmentTimelineEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromSubStageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToSubStageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignmentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsAutomatic = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RelatedTemporaryAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplacementForWorkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentTimelineEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentTimelineEntries_AppUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentTimelineEntries_SubStages_FromSubStageId",
                        column: x => x.FromSubStageId,
                        principalTable: "SubStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentTimelineEntries_SubStages_ToSubStageId",
                        column: x => x.ToSubStageId,
                        principalTable: "SubStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentTimelineEntries_Workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "Workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTimelineEntries_FromSubStageId",
                table: "AssignmentTimelineEntries",
                column: "FromSubStageId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTimelineEntries_PerformedByUserId",
                table: "AssignmentTimelineEntries",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTimelineEntries_StartAtUtc",
                table: "AssignmentTimelineEntries",
                column: "StartAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTimelineEntries_ToSubStageId",
                table: "AssignmentTimelineEntries",
                column: "ToSubStageId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTimelineEntries_WorkerId",
                table: "AssignmentTimelineEntries",
                column: "WorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTimelineEntries_WorkerId_StartAtUtc",
                table: "AssignmentTimelineEntries",
                columns: new[] { "WorkerId", "StartAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentTimelineEntries");
        }
    }
}
