using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LernendeRegeln : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LearnedAt",
                table: "CategorizationRules",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LearnedAt",
                table: "CategorizationRules");
        }
    }
}
