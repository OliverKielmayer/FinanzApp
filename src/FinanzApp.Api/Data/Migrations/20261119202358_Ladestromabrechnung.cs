using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ladestromabrechnung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChargingStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmploymentId = table.Column<int>(type: "INTEGER", nullable: true),
                    VehicleId = table.Column<int>(type: "INTEGER", nullable: true),
                    FromOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FromMeterKwh = table.Column<long>(type: "INTEGER", nullable: false),
                    ToOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ToMeterKwh = table.Column<long>(type: "INTEGER", nullable: false),
                    Kwh = table.Column<long>(type: "INTEGER", nullable: false),
                    EuroPerKwh = table.Column<long>(type: "INTEGER", nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReimbursementTransactionId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargingStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChargingStatements_Employments_EmploymentId",
                        column: x => x.EmploymentId,
                        principalTable: "Employments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChargingStatements_Transactions_ReimbursementTransactionId",
                        column: x => x.ReimbursementTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChargingStatements_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChargingStatements_EmploymentId",
                table: "ChargingStatements",
                column: "EmploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargingStatements_HouseholdId_EmploymentId_ToOn",
                table: "ChargingStatements",
                columns: new[] { "HouseholdId", "EmploymentId", "ToOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChargingStatements_ReimbursementTransactionId",
                table: "ChargingStatements",
                column: "ReimbursementTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargingStatements_VehicleId",
                table: "ChargingStatements",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChargingStatements");
        }
    }
}
