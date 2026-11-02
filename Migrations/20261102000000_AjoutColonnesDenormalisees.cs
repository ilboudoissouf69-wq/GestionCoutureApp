using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <summary>
    /// ÉTAPE 1 PERF — Colonnes dénormalisées DateRdvEffective et CommandeActive
    /// sur PiecesCommande, avec backfill SQL et index composites.
    ///
    /// DateRdvEffective = RendezVousException ?? DateFin.Date + (HeureFin ?? 17:00)
    /// CommandeActive = NOT EstSupprimee AND NOT EstAnnulee
    ///
    /// Ces colonnes permettent de filtrer les pièces SANS jointure sur Commandes
    /// et SANS calcul de date en mémoire (indexable directement).
    /// </summary>
    public partial class AjoutColonnesDenormalisees : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Ajouter les colonnes (avec valeur par défaut pour les lignes existantes) ──
            // DateRdvEffective : DateTime non-null — valeur temporaire, backfillée juste après
            migrationBuilder.Sql(
                "ALTER TABLE PiecesCommande ADD COLUMN DateRdvEffective TEXT NOT NULL DEFAULT '1900-01-01 00:00:00';");

            // CommandeActive : bool — true par défaut (la plupart des commandes sont actives)
            migrationBuilder.Sql(
                "ALTER TABLE PiecesCommande ADD COLUMN CommandeActive INTEGER NOT NULL DEFAULT 1;");

            // ── 2. Backfill : calculer DateRdvEffective pour toutes les pièces existantes ──
            // RendezVousException si présent, sinon DateFin.Date + (HeureFin ?? 17:00)
            // SQLite ne supporte pas UPDATE avec JOIN → on utilise une sous-requête corrélée.
            migrationBuilder.Sql(@"
                UPDATE PiecesCommande
                SET DateRdvEffective = CASE
                    WHEN RendezVousException IS NOT NULL THEN RendezVousException
                    ELSE (
                        SELECT date(c.DateFin) || ' ' || COALESCE(c.HeureFin, '17:00:00')
                        FROM Commandes c
                        WHERE c.IdCommande = PiecesCommande.IdCommande
                    )
                END
                WHERE EXISTS (SELECT 1 FROM Commandes WHERE IdCommande = PiecesCommande.IdCommande);");

            // ── 3. Backfill : CommandeActive depuis la commande parente ──
            migrationBuilder.Sql(@"
                UPDATE PiecesCommande
                SET CommandeActive = (
                    SELECT CASE WHEN COALESCE(c.EstSupprimee, 0) = 0 AND COALESCE(c.EstAnnulee, 0) = 0
                         THEN 1 ELSE 0 END
                    FROM Commandes c
                    WHERE c.IdCommande = PiecesCommande.IdCommande
                )
                WHERE EXISTS (SELECT 1 FROM Commandes WHERE IdCommande = PiecesCommande.IdCommande);");

            // ── 4. Index composites ────────────────────────────────────────────
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_PiecesCommande_Statut_DateRdvEffective " +
                "ON PiecesCommande (Statut, DateRdvEffective);");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_PiecesCommande_CommandeActive_Statut_DateRdv " +
                "ON PiecesCommande (CommandeActive, Statut, DateRdvEffective);");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_PiecesCommande_IdCouturier_Statut " +
                "ON PiecesCommande (IdCouturier, Statut);");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Commandes_EstSupprimee_EstAnnulee_DateFin " +
                "ON Commandes (EstSupprimee, EstAnnulee, DateFin);");

            // ── 5. ANALYZE pour que le query planner utilise les nouveaux index ──
            migrationBuilder.Sql("ANALYZE;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PiecesCommande_Statut_DateRdvEffective;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PiecesCommande_CommandeActive_Statut_DateRdv;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PiecesCommande_IdCouturier_Statut;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Commandes_EstSupprimee_EstAnnulee_DateFin;");

            // SQLite ne supporte pas DROP COLUMN avant 3.35.0 ; on recrée sans les colonnes
            // En pratique, le Down n'est presque jamais appelé en production.
            migrationBuilder.Sql("ALTER TABLE PiecesCommande DROP COLUMN DateRdvEffective;");
            migrationBuilder.Sql("ALTER TABLE PiecesCommande DROP COLUMN CommandeActive;");
        }
    }
}
