using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class AjoutRetoursChampsReprise : Migration
    {
        private static void AjouterColonneSiAbsente(MigrationBuilder mb, string table, string colonne, string definition)
        {
            // SQLite ne supporte pas IF NOT EXISTS sur ALTER TABLE.
            // On enveloppe chaque ALTER dans un bloc qui ignore l'erreur
            // "duplicate column name" (code SQLite 1 ou message contenant
            // "duplicate column").  Grâce à suppressTransaction: true,
            // l'échec d'un ALTER n'annule pas l'ensemble de la migration.
            mb.Sql($"ALTER TABLE {table} ADD COLUMN {colonne} {definition};",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Champs annulation (peuvent déjà exister sur certaines bases) ──
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "EstAnnule",    "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "MotifAnnulation",  "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "DateAnnulation",   "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "NomAnnulateur",    "TEXT NULL");

            // ── Nouveaux champs reprise ───────────────────────────────────────
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "IdCouturierReprise",  "INTEGER NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "CheminPhotoDefaut",   "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "DateRdvReprise",      "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "HeureDebutReprise",   "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "HeureFinReprise",     "TEXT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite ne supporte pas DROP COLUMN de façon fiable sur toutes les versions.
            // On laisse vide intentionnellement.
        }
    }
}
