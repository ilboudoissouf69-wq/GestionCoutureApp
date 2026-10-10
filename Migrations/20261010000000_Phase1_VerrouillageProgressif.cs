using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <summary>
    /// Phase 1 — Sécurité critique : ajout des colonnes de verrouillage progressif
    /// des comptes utilisateurs sur la table Employes.
    ///
    /// Colonnes ajoutées :
    ///   NbEchecConnexion INTEGER NOT NULL DEFAULT 0  — compteur cumulatif d'échecs
    ///   DateVerrouJusqua TEXT NULL                   — UTC, null = non verrouillé
    ///
    /// NOTE ARCHITECTURE : cette migration est un no-op dans Up().
    /// Le DDL réel est appliqué par AppliquerMigrationsManquantes() dans App.cs
    /// (via ALTER TABLE … ADD COLUMN idempotent) pour contourner la limitation
    /// SQLite qui ne supporte pas ADD COLUMN IF NOT EXISTS.
    ///
    /// Voir AuthService.cs pour l'utilisation, et docs/DECISIONS_A_VALIDER.md
    /// DEC-01 / DEC-02 pour les justifications des paliers de verrouillage.
    /// </summary>
    public partial class Phase1_VerrouillageProgressif : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionnellement vide — voir commentaire de classe.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite ne supporte pas DROP COLUMN.
            // Les colonnes sont ignorées par EF Core si elles ne sont plus dans le modèle.
        }
    }
}
