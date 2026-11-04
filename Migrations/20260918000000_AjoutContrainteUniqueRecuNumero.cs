using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <summary>
    /// CORRECTIF AUDIT #5 : Ajout d'une contrainte UNIQUE sur Paiements.RecuNumero
    /// pour empêcher les doublons en cas de race condition (deux paiements simultanés).
    /// </summary>
    public partial class AjoutContrainteUniqueRecuNumero : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Certaines anciennes bases contiennent des numéros réutilisés.
            // Garder le numéro d'origine sur le plus ancien paiement et donner
            // un suffixe traçable aux suivants avant de poser l'index unique.
            migrationBuilder.Sql(@"
                UPDATE ""Paiements""
                SET ""RecuNumero"" = ""RecuNumero"" || '-DUP-' || ""IdPaiement""
                WHERE ""RecuNumero"" != ''
                  AND ""IdPaiement"" NOT IN (
                      SELECT MIN(""IdPaiement"")
                      FROM ""Paiements""
                      WHERE ""RecuNumero"" != ''
                      GROUP BY ""RecuNumero""
                  );");

            migrationBuilder.CreateIndex(
                name: "IX_Paiements_RecuNumero",
                table: "Paiements",
                column: "RecuNumero",
                unique: true,
                filter: "\"RecuNumero\" != ''");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Paiements_RecuNumero",
                table: "Paiements");
        }
    }
}
