using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DepotAusfuehrungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepotTrades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    DepotId = table.Column<int>(type: "INTEGER", nullable: false),
                    SecurityName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Isin = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Wkn = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderType = table.Column<int>(type: "INTEGER", nullable: false),
                    LimitPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    Fee = table.Column<long>(type: "INTEGER", nullable: false),
                    ImportReference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepotTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepotTrades_Depots_DepotId",
                        column: x => x.DepotId,
                        principalTable: "Depots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepotTrades_DepotId",
                table: "DepotTrades",
                column: "DepotId");

            migrationBuilder.CreateIndex(
                name: "IX_DepotTrades_HouseholdId_ImportReference",
                table: "DepotTrades",
                columns: new[] { "HouseholdId", "ImportReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepotTrades");
        }
    }
}
