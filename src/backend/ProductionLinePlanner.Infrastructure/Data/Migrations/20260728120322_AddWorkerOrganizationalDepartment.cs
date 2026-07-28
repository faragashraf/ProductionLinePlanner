using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerOrganizationalDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationalDepartmentConcurrencyToken",
                table: "Workers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWSEQUENTIALID()");

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationalDepartmentId",
                table: "Workers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workers_OrganizationalDepartmentId",
                table: "Workers",
                column: "OrganizationalDepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Workers_Departments_OrganizationalDepartmentId",
                table: "Workers",
                column: "OrganizationalDepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Workers_Departments_OrganizationalDepartmentId",
                table: "Workers");

            migrationBuilder.DropIndex(
                name: "IX_Workers_OrganizationalDepartmentId",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "OrganizationalDepartmentConcurrencyToken",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "OrganizationalDepartmentId",
                table: "Workers");
        }
    }
}
