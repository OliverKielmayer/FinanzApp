using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Handlungsbedarf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ActionFromModel",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ActionLine",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "Documents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActionFromModel",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ActionLine",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Documents");
        }
    }
}
