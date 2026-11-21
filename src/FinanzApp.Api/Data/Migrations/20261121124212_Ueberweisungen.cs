using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ueberweisungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceiverName = table.Column<string>(type: "TEXT", maxLength: 70, nullable: false),
                    ReceiverIban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: false),
                    ReceiverBic = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 140, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    BankMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransfers_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_AccountId",
                table: "BankTransfers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_HouseholdId_AccountId_CreatedAt",
                table: "BankTransfers",
                columns: new[] { "HouseholdId", "AccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_InvoiceId",
                table: "BankTransfers",
                column: "InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankTransfers");
        }
    }
}
