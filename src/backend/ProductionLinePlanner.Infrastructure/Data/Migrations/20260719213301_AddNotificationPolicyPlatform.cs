using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationPolicyPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventKey",
                table: "Notifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Severity",
                table: "Notifications",
                type: "int",
                nullable: true,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NotificationPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    IsToastEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsInboxEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsSoundEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SoundKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TitleTemplateAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MessageTemplateAr = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPolicies", x => x.Id);
                    table.CheckConstraint("CK_NotificationPolicies_SoundKey", "([IsSoundEnabled] = 0 AND [SoundKey] IS NULL) OR ([IsSoundEnabled] = 1 AND [SoundKey] = 'default')");
                    table.ForeignKey(
                        name: "FK_NotificationPolicies_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationPolicies_AppUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPolicyRecipientRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientKind = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PermissionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CapabilityKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsExcludeActor = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPolicyRecipientRules", x => x.Id);
                    table.CheckConstraint("CK_NotificationPolicyRecipientRules_Target", "([RecipientKind] = 0 AND [UserId] IS NOT NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 1 AND [UserId] IS NULL AND [RoleId] IS NOT NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 2 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NOT NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 3 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NOT NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 4 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 0) OR ([RecipientKind] = 5 AND [UserId] IS NULL AND [RoleId] IS NULL AND [PermissionKey] IS NULL AND [CapabilityKey] IS NULL AND [IsExcludeActor] = 1)");
                    table.ForeignKey(
                        name: "FK_NotificationPolicyRecipientRules_AppRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AppRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationPolicyRecipientRules_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationPolicyRecipientRules_NotificationPolicies_NotificationPolicyId",
                        column: x => x.NotificationPolicyId,
                        principalTable: "NotificationPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EventKey",
                table: "Notifications",
                column: "EventKey");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicies_CreatedByUserId",
                table: "NotificationPolicies",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicies_EventKey",
                table: "NotificationPolicies",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicies_UpdatedAtUtc",
                table: "NotificationPolicies",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicies_UpdatedByUserId",
                table: "NotificationPolicies",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicyRecipientRules_NotificationPolicyId_SortOrder",
                table: "NotificationPolicyRecipientRules",
                columns: new[] { "NotificationPolicyId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicyRecipientRules_RoleId",
                table: "NotificationPolicyRecipientRules",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicyRecipientRules_UserId",
                table: "NotificationPolicyRecipientRules",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationPolicyRecipientRules");

            migrationBuilder.DropTable(
                name: "NotificationPolicies");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_EventKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EventKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "Notifications");
        }
    }
}
