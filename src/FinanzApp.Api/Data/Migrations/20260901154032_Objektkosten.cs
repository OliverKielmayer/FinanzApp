using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Objektkosten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LivingArea",
                table: "Properties",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyReserve",
                table: "Properties",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LivingArea",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "MonthlyReserve",
                table: "Properties");
        }
    }
}
