using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Dublettenerkennung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DuplicateOfDocumentId",
                table: "ScanInbox",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Documents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanInbox_DuplicateOfDocumentId",
                table: "ScanInbox",
                column: "DuplicateOfDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_HouseholdId_ContentHash",
                table: "Documents",
                columns: new[] { "HouseholdId", "ContentHash" });

            migrationBuilder.AddForeignKey(
                name: "FK_ScanInbox_Documents_DuplicateOfDocumentId",
                table: "ScanInbox",
                column: "DuplicateOfDocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScanInbox_Documents_DuplicateOfDocumentId",
                table: "ScanInbox");

            migrationBuilder.DropIndex(
                name: "IX_ScanInbox_DuplicateOfDocumentId",
                table: "ScanInbox");

            migrationBuilder.DropIndex(
                name: "IX_Documents_HouseholdId_ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DuplicateOfDocumentId",
                table: "ScanInbox");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Documents");
        }
    }
}
