using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ladestrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChargingRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmploymentId = table.Column<int>(type: "INTEGER", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EuroPerKwh = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargingRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChargingRates_Employments_EmploymentId",
                        column: x => x.EmploymentId,
                        principalTable: "Employments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ChargingReadings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmploymentId = table.Column<int>(type: "INTEGER", nullable: true),
                    VehicleId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReadOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    MeterKwh = table.Column<long>(type: "INTEGER", nullable: false),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargingReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChargingReadings_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChargingReadings_Employments_EmploymentId",
                        column: x => x.EmploymentId,
                        principalTable: "Employments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChargingReadings_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChargingRates_EmploymentId",
                table: "ChargingRates",
                column: "EmploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargingRates_HouseholdId_EmploymentId_ValidFrom",
                table: "ChargingRates",
                columns: new[] { "HouseholdId", "EmploymentId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChargingReadings_DocumentId",
                table: "ChargingReadings",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargingReadings_EmploymentId",
                table: "ChargingReadings",
                column: "EmploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargingReadings_HouseholdId_EmploymentId_ReadOn",
                table: "ChargingReadings",
                columns: new[] { "HouseholdId", "EmploymentId", "ReadOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChargingReadings_HouseholdId_ReadOn",
                table: "ChargingReadings",
                columns: new[] { "HouseholdId", "ReadOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ChargingReadings_VehicleId",
                table: "ChargingReadings",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChargingRates");

            migrationBuilder.DropTable(
                name: "ChargingReadings");
        }
    }
}
