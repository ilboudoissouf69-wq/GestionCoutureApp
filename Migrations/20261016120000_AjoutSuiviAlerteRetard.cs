using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class AjoutSuiviAlerteRetard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SuivisAlerteRetard",
                columns: table => new
                {
                    IdSuivi = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TypeCible = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IdCible = table.Column<int>(type: "INTEGER", nullable: false),
                    IdClient = table.Column<int>(type: "INTEGER", nullable: false),
                    DateRdvTraite = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NbPreventions = table.Column<int>(type: "INTEGER", nullable: false),
                    NbReports = table.Column<int>(type: "INTEGER", nullable: false),
                    DateDernierMessage = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IdOperateurDernierMessage = table.Column<int>(type: "INTEGER", nullable: true),
                    NomOperateurDernierMessage = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AncienneDateDernierReport = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NouvelleDateDernierReport = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DateDernierReport = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuivisAlerteRetard", x => x.IdSuivi);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SuivisAlerteRetard_TypeCible_IdCible",
                table: "SuivisAlerteRetard",
                columns: new[] { "TypeCible", "IdCible" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SuivisAlerteRetard");
        }
    }
}
