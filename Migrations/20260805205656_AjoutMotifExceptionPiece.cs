using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class AjoutMotifExceptionPiece : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateAnnulation",
                table: "Retours",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EstAnnule",
                table: "Retours",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MotifAnnulation",
                table: "Retours",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomAnnulateur",
                table: "Retours",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotifAjoutApresEncaissement",
                table: "PiecesCommande",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAnnulation",
                table: "Depenses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EstAnnulee",
                table: "Depenses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MotifAnnulation",
                table: "Depenses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomAnnulateur",
                table: "Depenses",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateAnnulation",
                table: "Retours");

            migrationBuilder.DropColumn(
                name: "EstAnnule",
                table: "Retours");

            migrationBuilder.DropColumn(
                name: "MotifAnnulation",
                table: "Retours");

            migrationBuilder.DropColumn(
                name: "NomAnnulateur",
                table: "Retours");

            migrationBuilder.DropColumn(
                name: "MotifAjoutApresEncaissement",
                table: "PiecesCommande");

            migrationBuilder.DropColumn(
                name: "DateAnnulation",
                table: "Depenses");

            migrationBuilder.DropColumn(
                name: "EstAnnulee",
                table: "Depenses");

            migrationBuilder.DropColumn(
                name: "MotifAnnulation",
                table: "Depenses");

            migrationBuilder.DropColumn(
                name: "NomAnnulateur",
                table: "Depenses");
        }
    }
}
