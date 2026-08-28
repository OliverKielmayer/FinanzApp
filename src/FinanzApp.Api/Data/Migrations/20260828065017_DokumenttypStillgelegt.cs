using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DokumenttypStillgelegt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentTypes_HouseholdId_Name",
                table: "DocumentTypes");

            migrationBuilder.AddColumn<bool>(
                name: "IsRetired",
                table: "DocumentTypes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_HouseholdId_Name",
                table: "DocumentTypes",
                columns: new[] { "HouseholdId", "Name" },
                unique: true,
                filter: "\"IsRetired\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentTypes_HouseholdId_Name",
                table: "DocumentTypes");

            migrationBuilder.DropColumn(
                name: "IsRetired",
                table: "DocumentTypes");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_HouseholdId_Name",
                table: "DocumentTypes",
                columns: new[] { "HouseholdId", "Name" },
                unique: true);
        }
    }
}
