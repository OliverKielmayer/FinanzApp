using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Vertragskennzahlen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractFacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContractId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: true),
                    Text = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractFacts_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractFacts_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractFacts_ContractId",
                table: "ContractFacts",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractFacts_DocumentId",
                table: "ContractFacts",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractFacts_HouseholdId_ContractId_Key_ValidFrom",
                table: "ContractFacts",
                columns: new[] { "HouseholdId", "ContractId", "Key", "ValidFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractFacts");
        }
    }
}
