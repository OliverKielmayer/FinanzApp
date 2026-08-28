using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Quartalsaufstellungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepotStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    DepotId = table.Column<int>(type: "INTEGER", nullable: false),
                    AsOf = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DepotNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Custodian = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepotStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepotStatements_Depots_DepotId",
                        column: x => x.DepotId,
                        principalTable: "Depots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepotStatements_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DepotStatementPositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    StatementId = table.Column<int>(type: "INTEGER", nullable: false),
                    SecurityName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Isin = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Wkn = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    SafeCustody = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Depository = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepotStatementPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepotStatementPositions_DepotStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "DepotStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepotStatementPositions_StatementId",
                table: "DepotStatementPositions",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_DepotStatements_DepotId_AsOf",
                table: "DepotStatements",
                columns: new[] { "DepotId", "AsOf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepotStatements_DocumentId",
                table: "DepotStatements",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepotStatementPositions");

            migrationBuilder.DropTable(
                name: "DepotStatements");
        }
    }
}
