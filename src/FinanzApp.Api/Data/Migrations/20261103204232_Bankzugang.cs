using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Bankzugang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Blz",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinTsCustomerId",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinTsTanMethod",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinTsUrl",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinTsUserId",
                table: "Accounts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Blz",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "FinTsCustomerId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "FinTsTanMethod",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "FinTsUrl",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "FinTsUserId",
                table: "Accounts");
        }
    }
}
