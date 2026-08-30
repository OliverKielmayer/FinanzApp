using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Vertragswerte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AccruedBonus",
                table: "Policies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BaseValue",
                table: "Policies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PolicyReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    PolicyId = table.Column<int>(type: "INTEGER", nullable: false),
                    AsOf = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyReports_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyReports_HouseholdId_PolicyId_AsOf",
                table: "PolicyReports",
                columns: new[] { "HouseholdId", "PolicyId", "AsOf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyReports_PolicyId",
                table: "PolicyReports",
                column: "PolicyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyReports");

            migrationBuilder.DropColumn(
                name: "AccruedBonus",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "BaseValue",
                table: "Policies");
        }
    }
}
