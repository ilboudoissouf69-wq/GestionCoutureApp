using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <summary>
    /// CORRECTIF AUDIT #11 : Ajout du champ DerniereModificationMotDePasse
    /// pour permettre un rappel périodique de changement de mot de passe (90 jours).
    /// </summary>
    public partial class AjoutDerniereModificationMotDePasse : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DerniereModificationMotDePasse",
                table: "Employes",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DerniereModificationMotDePasse",
                table: "Employes");
        }
    }
}
