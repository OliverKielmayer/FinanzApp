using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Investitionen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_PropertyId",
                table: "Purchases",
                column: "PropertyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Properties_PropertyId",
                table: "Purchases",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Properties_PropertyId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_PropertyId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "Purchases");
        }
    }
}
