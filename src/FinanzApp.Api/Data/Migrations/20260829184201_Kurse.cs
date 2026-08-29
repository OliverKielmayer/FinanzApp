using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Kurse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuoteRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Requested = table.Column<int>(type: "INTEGER", nullable: false),
                    Stored = table.Column<int>(type: "INTEGER", nullable: false),
                    Failed = table.Column<int>(type: "INTEGER", nullable: false),
                    Problem = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Manual = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Isin = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Close = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRuns_HouseholdId_FinishedAt",
                table: "QuoteRuns",
                columns: new[] { "HouseholdId", "FinishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_HouseholdId_Isin_Date",
                table: "Quotes",
                columns: new[] { "HouseholdId", "Isin", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteRuns");

            migrationBuilder.DropTable(
                name: "Quotes");
        }
    }
}
