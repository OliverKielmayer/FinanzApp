using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FahrzeugeUndScaneingang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScanInbox",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sender = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Recognised = table.Column<bool>(type: "INTEGER", nullable: false),
                    FiledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanInbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanInbox_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Plate = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Usage = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    FirstRegistration = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Mileage = table.Column<int>(type: "INTEGER", nullable: true),
                    PolicyId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanInbox_DocumentId",
                table: "ScanInbox",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanInbox_FiledAt",
                table: "ScanInbox",
                column: "FiledAt");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_PolicyId",
                table: "Vehicles",
                column: "PolicyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScanInbox");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
