using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class AjoutMaterielsSupplements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaterielsSupplements",
                columns: table => new
                {
                    IdMateriel = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdPieceCommande = table.Column<int>(type: "INTEGER", nullable: true),
                    IdCommande = table.Column<int>(type: "INTEGER", nullable: false),
                    Designation = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Quantite = table.Column<int>(type: "INTEGER", nullable: false),
                    PrixUnitaire = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterielsSupplements", x => x.IdMateriel);
                    table.ForeignKey(
                        name: "FK_MaterielsSupplements_Commandes_IdCommande",
                        column: x => x.IdCommande,
                        principalTable: "Commandes",
                        principalColumn: "IdCommande",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterielsSupplements_PiecesCommande_IdPieceCommande",
                        column: x => x.IdPieceCommande,
                        principalTable: "PiecesCommande",
                        principalColumn: "IdPieceCommande",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterielsSupplements_IdCommande",
                table: "MaterielsSupplements",
                column: "IdCommande");

            migrationBuilder.CreateIndex(
                name: "IX_MaterielsSupplements_IdPieceCommande",
                table: "MaterielsSupplements",
                column: "IdPieceCommande");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterielsSupplements");
        }
    }
}
