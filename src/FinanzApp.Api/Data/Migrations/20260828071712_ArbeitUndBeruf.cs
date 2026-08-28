using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArbeitUndBeruf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Employer = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Position = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    HoursPerWeek = table.Column<decimal>(type: "TEXT", nullable: true),
                    GrossMonthly = table.Column<long>(type: "INTEGER", nullable: false),
                    NetMonthly = table.Column<long>(type: "INTEGER", nullable: true),
                    NoticePeriodMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payslips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmploymentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Gross = table.Column<long>(type: "INTEGER", nullable: false),
                    Net = table.Column<long>(type: "INTEGER", nullable: false),
                    Payout = table.Column<long>(type: "INTEGER", nullable: false),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payslips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payslips_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Payslips_Employments_EmploymentId",
                        column: x => x.EmploymentId,
                        principalTable: "Employments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Payslips_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WorkAgreements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmploymentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SignedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkAgreements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkAgreements_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkAgreements_Employments_EmploymentId",
                        column: x => x.EmploymentId,
                        principalTable: "Employments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employments_HouseholdId_IsActive",
                table: "Employments",
                columns: new[] { "HouseholdId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_DocumentId",
                table: "Payslips",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_EmploymentId_Month",
                table: "Payslips",
                columns: new[] { "EmploymentId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_TransactionId",
                table: "Payslips",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkAgreements_DocumentId",
                table: "WorkAgreements",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkAgreements_EmploymentId",
                table: "WorkAgreements",
                column: "EmploymentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payslips");

            migrationBuilder.DropTable(
                name: "WorkAgreements");

            migrationBuilder.DropTable(
                name: "Employments");
        }
    }
}
