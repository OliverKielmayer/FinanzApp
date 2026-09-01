using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Einlage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepositUserId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DepositUserId",
                table: "Transactions",
                column: "DepositUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PropertyId",
                table: "Transactions",
                column: "PropertyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Properties_PropertyId",
                table: "Transactions",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Users_DepositUserId",
                table: "Transactions",
                column: "DepositUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Properties_PropertyId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Users_DepositUserId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_DepositUserId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_PropertyId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DepositUserId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "Transactions");
        }
    }
}
