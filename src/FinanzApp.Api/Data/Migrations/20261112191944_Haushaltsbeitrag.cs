using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Haushaltsbeitrag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "Loans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContributorUserId",
                table: "Categories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loans_AccountId",
                table: "Loans",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ContributorUserId",
                table: "Categories",
                column: "ContributorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Users_ContributorUserId",
                table: "Categories",
                column: "ContributorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Loans_Accounts_AccountId",
                table: "Loans",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Users_ContributorUserId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_Loans_Accounts_AccountId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Loans_AccountId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Categories_ContributorUserId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ContributorUserId",
                table: "Categories");
        }
    }
}
