using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnlegeFelder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Properties",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "Depots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Broker",
                table: "Depots",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepotKind",
                table: "Depots",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "Depots",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuoteSource",
                table: "Depots",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StatedValue",
                table: "Depots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValuationDate",
                table: "Depots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Period",
                table: "Budgets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValidFrom",
                table: "Budgets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarnThresholdPercent",
                table: "Budgets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Depots_AccountId",
                table: "Depots",
                column: "AccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Depots_Accounts_AccountId",
                table: "Depots",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Depots_Accounts_AccountId",
                table: "Depots");

            migrationBuilder.DropIndex(
                name: "IX_Depots_AccountId",
                table: "Depots");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Depots");

            migrationBuilder.DropColumn(
                name: "Broker",
                table: "Depots");

            migrationBuilder.DropColumn(
                name: "DepotKind",
                table: "Depots");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Depots");

            migrationBuilder.DropColumn(
                name: "QuoteSource",
                table: "Depots");

            migrationBuilder.DropColumn(
                name: "StatedValue",
                table: "Depots");

            migrationBuilder.DropColumn(
                name: "ValuationDate",
                table: "Depots");

            migrationBuilder.DropColumn(
                name: "Period",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "ValidFrom",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "WarnThresholdPercent",
                table: "Budgets");
        }
    }
}
