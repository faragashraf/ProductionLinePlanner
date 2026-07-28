using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceRealtimeNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_WorkerId_AttendanceTimeUtc",
                table: "AttendanceRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NotificationPolicyRecipientRules_Target",
                table: "NotificationPolicyRecipientRules");

            migrationBuilder.AddColumn<string>(
                name: "CorrelationKey",
                table: "Notifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBrowserEnabled",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSoundEnabled",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsToastEnabled",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "Notifications",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NavigationUrl",
                table: "Notifications",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBrowserEnabled",
                table: "NotificationPolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttendanceNotificationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AttendanceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AttendanceTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceNotificationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceNotificationEvents_AttendanceRecords_AttendanceRecordId",
                        column: x => x.AttendanceRecordId,
                        principalTable: "AttendanceRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_CorrelationKey",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "CorrelationKey" },
                unique: true,
                filter: "[CorrelationKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_WorkerId_AttendanceTimeUtc",
                table: "AttendanceRecords",
                columns: new[] { "WorkerId", "AttendanceTimeUtc" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_NotificationPolicyRecipientRules_Target",
                table: "NotificationPolicyRecipientRules",
                sql: "([RecipientKind] = 0 AND [UserId] IS NOT NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 1 AND [UserId] IS NULL AND [RoleId] IS NOT NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 2 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NOT NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 3 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NOT NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 4 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 5 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 1) OR ([RecipientKind] = 6 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceNotificationEvents_AttendanceRecordId",
                table: "AttendanceNotificationEvents",
                column: "AttendanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceNotificationEvents_IdempotencyKey",
                table: "AttendanceNotificationEvents",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceNotificationEvents_ProcessedAtUtc_CreatedAtUtc",
                table: "AttendanceNotificationEvents",
                columns: new[] { "ProcessedAtUtc", "CreatedAtUtc" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceNotificationEvents");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RecipientUserId_CorrelationKey",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_WorkerId_AttendanceTimeUtc",
                table: "AttendanceRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NotificationPolicyRecipientRules_Target",
                table: "NotificationPolicyRecipientRules");

            migrationBuilder.DropColumn(
                name: "CorrelationKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsBrowserEnabled",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsSoundEnabled",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsToastEnabled",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_WorkerId_AttendanceTimeUtc",
                table: "AttendanceRecords",
                columns: new[] { "WorkerId", "AttendanceTimeUtc" });

            migrationBuilder.DropColumn(
                name: "NavigationUrl",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsBrowserEnabled",
                table: "NotificationPolicies");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NotificationPolicyRecipientRules_Target",
                table: "NotificationPolicyRecipientRules",
                sql: "([RecipientKind] = 0 AND [UserId] IS NOT NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 1 AND [UserId] IS NULL AND [RoleId] IS NOT NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 2 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NOT NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 3 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NOT NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 4 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 5 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 1)");
        }
    }
}
