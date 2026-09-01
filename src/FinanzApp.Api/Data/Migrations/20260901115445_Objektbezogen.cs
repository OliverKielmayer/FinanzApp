using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Objektbezogen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PropertyRelated",
                table: "Contracts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PropertyRelated",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Ein Vertrag am Objekt war bisher genau das, was „objektbezogen“ meint — die
            // vorhandene Zuordnung behält also ihre Bedeutung. Ausnahmen wie der Internetanschluss
            // werden danach von Hand abgewählt.
            migrationBuilder.Sql(
                "UPDATE Contracts SET PropertyRelated = 1 WHERE PropertyId IS NOT NULL;");

            // Kategorien bekommen keinen Nachtrag: welche zum Objekt gehören, steht nicht im
            // Namen. Geraten wäre schlimmer als leer — leer ist sichtbar und wird gepflegt.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PropertyRelated",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PropertyRelated",
                table: "Categories");
        }
    }
}
