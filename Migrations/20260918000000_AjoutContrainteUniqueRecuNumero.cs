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
            migrationBuilder.CreateIndex(
                name: "IX_Paiements_RecuNumero",
                table: "Paiements",
                column: "RecuNumero",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Paiements_RecuNumero",
                table: "Paiements");
        }
    }
}
