using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class AjoutPieceCommande : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Table PiecesCommande — entité centrale du Point 1 (commandes multi-pièces)
            migrationBuilder.CreateTable(
                name: "PiecesCommande",
                columns: table => new
                {
                    IdPieceCommande = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdCommande = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeVetement = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionPrecision = table.Column<string>(type: "TEXT", nullable: false),
                    CheminPhoto = table.Column<string>(type: "TEXT", nullable: false),
                    IdCouturier = table.Column<int>(type: "INTEGER", nullable: true),
                    MontantCouture = table.Column<decimal>(type: "TEXT", nullable: false),
                    Statut = table.Column<string>(type: "TEXT", nullable: false),
                    RendezVousException = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IdCommission = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PiecesCommande", x => x.IdPieceCommande);
                    table.ForeignKey(
                        name: "FK_PiecesCommande_Commandes_IdCommande",
                        column: x => x.IdCommande,
                        principalTable: "Commandes",
                        principalColumn: "IdCommande",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PiecesCommande_Employes_IdCouturier",
                        column: x => x.IdCouturier,
                        principalTable: "Employes",
                        principalColumn: "IdEmploye");
                    table.ForeignKey(
                        name: "FK_PiecesCommande_Commissions_IdCommission",
                        column: x => x.IdCommission,
                        principalTable: "Commissions",
                        principalColumn: "IdCommission",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PiecesCommande_IdCommande",
                table: "PiecesCommande",
                column: "IdCommande");

            migrationBuilder.CreateIndex(
                name: "IX_PiecesCommande_IdCouturier",
                table: "PiecesCommande",
                column: "IdCouturier");

            migrationBuilder.CreateIndex(
                name: "IX_PiecesCommande_IdCommission",
                table: "PiecesCommande",
                column: "IdCommission");

            // Colonne IdPieceCommande ajoutée à Mesures (nullable — Étape 1a)
            migrationBuilder.AddColumn<int>(
                name: "IdPieceCommande",
                table: "Mesures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mesures_IdPieceCommande",
                table: "Mesures",
                column: "IdPieceCommande");

            migrationBuilder.AddForeignKey(
                name: "FK_Mesures_PiecesCommande_IdPieceCommande",
                table: "Mesures",
                column: "IdPieceCommande",
                principalTable: "PiecesCommande",
                principalColumn: "IdPieceCommande",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Mesures_PiecesCommande_IdPieceCommande",
                table: "Mesures");

            migrationBuilder.DropIndex(
                name: "IX_Mesures_IdPieceCommande",
                table: "Mesures");

            migrationBuilder.DropColumn(
                name: "IdPieceCommande",
                table: "Mesures");

            migrationBuilder.DropTable(
                name: "PiecesCommande");
        }
    }
}
