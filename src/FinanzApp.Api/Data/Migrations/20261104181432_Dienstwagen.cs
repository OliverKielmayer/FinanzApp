using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Dienstwagen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmploymentId",
                table: "Vehicles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GrossListPrice",
                table: "Vehicles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxableSharePercent",
                table: "Vehicles",
                type: "TEXT",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_EmploymentId",
                table: "Vehicles",
                column: "EmploymentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_Employments_EmploymentId",
                table: "Vehicles",
                column: "EmploymentId",
                principalTable: "Employments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_Employments_EmploymentId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_EmploymentId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "EmploymentId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "GrossListPrice",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TaxableSharePercent",
                table: "Vehicles");
        }
    }
}
