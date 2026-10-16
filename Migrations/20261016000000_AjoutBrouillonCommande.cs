using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class AjoutBrouillonCommande : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrouillonsCommande",
                columns: table => new
                {
                    IdBrouillon = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdEmploye = table.Column<int>(type: "INTEGER", nullable: false),
                    ContenuJson = table.Column<string>(type: "TEXT", nullable: false),
                    DateMiseAJour = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrouillonsCommande", x => x.IdBrouillon);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrouillonsCommande_IdEmploye",
                table: "BrouillonsCommande",
                column: "IdEmploye",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrouillonsCommande");
        }
    }
}
