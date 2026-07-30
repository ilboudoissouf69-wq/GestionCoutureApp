using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class AjoutRetours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Retours",
                columns: table => new
                {
                    IdRetour = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdCommande = table.Column<int>(type: "INTEGER", nullable: false),
                    IdPieceCommande = table.Column<int>(type: "INTEGER", nullable: false),
                    IdCouturier = table.Column<int>(type: "INTEGER", nullable: false),
                    DescriptionProbleme = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Statut = table.Column<string>(type: "TEXT", nullable: false),
                    DateSignalement = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateResolution = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IdOperateurEnregistrement = table.Column<int>(type: "INTEGER", nullable: false),
                    NomOperateurEnregistrement = table.Column<string>(type: "TEXT", nullable: false),
                    IdOperateurResolution = table.Column<int>(type: "INTEGER", nullable: true),
                    NomOperateurResolution = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Retours", x => x.IdRetour);
                    table.ForeignKey(
                        name: "FK_Retours_Commandes_IdCommande",
                        column: x => x.IdCommande,
                        principalTable: "Commandes",
                        principalColumn: "IdCommande",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Retours_Employes_IdCouturier",
                        column: x => x.IdCouturier,
                        principalTable: "Employes",
                        principalColumn: "IdEmploye",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Retours_PiecesCommande_IdPieceCommande",
                        column: x => x.IdPieceCommande,
                        principalTable: "PiecesCommande",
                        principalColumn: "IdPieceCommande",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Retours_IdCommande",
                table: "Retours",
                column: "IdCommande");

            migrationBuilder.CreateIndex(
                name: "IX_Retours_IdCouturier",
                table: "Retours",
                column: "IdCouturier");

            migrationBuilder.CreateIndex(
                name: "IX_Retours_IdPieceCommande",
                table: "Retours",
                column: "IdPieceCommande");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Retours");
        }
    }
}
