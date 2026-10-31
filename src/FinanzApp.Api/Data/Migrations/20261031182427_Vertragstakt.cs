using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Vertragstakt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Umbenannt, nicht neu angelegt: In der Spalte stehen die gepflegten Betraege.
            // Das Geruest hatte sie fuer die Zahlungsweise gehalten und daneben einen leeren
            // Betrag gesetzt - danach stuende in jedem Vertrag ein Cent-Betrag als Takt und
            // ein Abschlag von null.
            migrationBuilder.RenameColumn(
                name: "MonthlyAmount",
                table: "Contracts",
                newName: "Amount");

            // Vorgabe 0 ist Monthly - und genau das waren die bisherigen Betraege.
            migrationBuilder.AddColumn<int>(
                name: "PaymentInterval",
                table: "Contracts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Zurueck geht nur der Takt verloren. Wer ihn gesetzt hatte, haelt danach einen
            // Vierteljahresbetrag in einem Feld, das monatlich heisst - deshalb steht das hier
            // und nicht nur in der Versionsgeschichte.
            migrationBuilder.DropColumn(
                name: "PaymentInterval",
                table: "Contracts");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "Contracts",
                newName: "MonthlyAmount");
        }
    }
}
