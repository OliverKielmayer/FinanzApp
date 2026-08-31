using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Berichtswerte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AccruedBonus",
                table: "PolicyReports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BaseValue",
                table: "PolicyReports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentId",
                table: "PolicyReports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyReports_DocumentId",
                table: "PolicyReports",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyReports_Documents_DocumentId",
                table: "PolicyReports",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Der erreichte Wert kommt ab jetzt aus dem neuesten Bericht. Verträge, deren Wert
            // vor dieser Reihe gepflegt wurde, haben keinen — sie stünden ohne Wert da. Also
            // bekommt jeder von ihnen seinen bisherigen Stand als ersten Bericht, mit seinem
            // eigenen Stichtag und der Quelle „erfasst".
            migrationBuilder.Sql(
                """
                INSERT INTO PolicyReports
                    (HouseholdId, PolicyId, AsOf, Value, BaseValue, AccruedBonus, Source, CreatedAt)
                SELECT p.HouseholdId, p.Id, p.ValuationDate, p.CurrentValue,
                       p.BaseValue, p.AccruedBonus, 'erfasst', p.ValuationDate || ' 00:00:00'
                FROM Policies p
                WHERE p.IsCapitalForming = 1
                  AND p.CurrentValue IS NOT NULL
                  AND p.ValuationDate IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM PolicyReports r
                      WHERE r.PolicyId = p.Id AND r.AsOf = p.ValuationDate);
                """);

            // Und die Bestandteile an die vorhandenen Berichte, soweit sie zum Stichtag passen:
            // ein eingelesener Statusreport hat sie geliefert, gespeichert waren sie bisher nur
            // am Vertrag.
            migrationBuilder.Sql(
                """
                UPDATE PolicyReports
                SET BaseValue = (SELECT p.BaseValue FROM Policies p WHERE p.Id = PolicyReports.PolicyId),
                    AccruedBonus = (SELECT p.AccruedBonus FROM Policies p WHERE p.Id = PolicyReports.PolicyId)
                WHERE BaseValue IS NULL
                  AND AsOf = (SELECT p.ValuationDate FROM Policies p WHERE p.Id = PolicyReports.PolicyId);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PolicyReports_Documents_DocumentId",
                table: "PolicyReports");

            migrationBuilder.DropIndex(
                name: "IX_PolicyReports_DocumentId",
                table: "PolicyReports");

            migrationBuilder.DropColumn(
                name: "AccruedBonus",
                table: "PolicyReports");

            migrationBuilder.DropColumn(
                name: "BaseValue",
                table: "PolicyReports");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "PolicyReports");
        }
    }
}
