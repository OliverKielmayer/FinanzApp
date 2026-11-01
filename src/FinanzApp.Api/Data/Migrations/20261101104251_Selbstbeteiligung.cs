using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Selbstbeteiligung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DentalOwnSharePercent",
                table: "Policies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OwnShareAnnualCap",
                table: "Policies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OwnSharePercent",
                table: "Policies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDental",
                table: "MedicalBills",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DentalOwnSharePercent",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "OwnShareAnnualCap",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "OwnSharePercent",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "IsDental",
                table: "MedicalBills");
        }
    }
}
