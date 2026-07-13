using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnableCustomRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT [Name]
                    FROM [AppRoles]
                    GROUP BY [Name]
                    HAVING COUNT(*) > 1
                )
                BEGIN
                    THROW 51001, 'Cannot apply EnableCustomRoles because duplicate AppRoles.Name values exist. Correct duplicate role names before rerunning the migration.', 1;
                END
                """);

            migrationBuilder.DropIndex(
                name: "IX_AppRoles_Role",
                table: "AppRoles");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "AppRoles",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(60)",
                oldMaxLength: 60);

            migrationBuilder.Sql("UPDATE [AppRoles] SET [IsSystemRole] = CAST(1 AS bit) WHERE [Role] IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_AppRoles_Name",
                table: "AppRoles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRoles_Role",
                table: "AppRoles",
                column: "Role",
                unique: true,
                filter: "[Role] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [AppRoles] WHERE [Role] IS NULL)
                BEGIN
                    THROW 51000, 'Cannot roll back EnableCustomRoles while custom roles exist. Remove or convert custom roles first.', 1;
                END
                """);

            migrationBuilder.DropIndex(
                name: "IX_AppRoles_Name",
                table: "AppRoles");

            migrationBuilder.DropIndex(
                name: "IX_AppRoles_Role",
                table: "AppRoles");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "AppRoles",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(60)",
                oldMaxLength: 60,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRoles_Role",
                table: "AppRoles",
                column: "Role",
                unique: true);
        }
    }
}
