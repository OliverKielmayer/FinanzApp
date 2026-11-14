using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnschaffungAusBuchung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TransactionId",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_TransactionId",
                table: "Purchases",
                column: "TransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Transactions_TransactionId",
                table: "Purchases",
                column: "TransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Transactions_TransactionId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_TransactionId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "TransactionId",
                table: "Purchases");
        }
    }
}
