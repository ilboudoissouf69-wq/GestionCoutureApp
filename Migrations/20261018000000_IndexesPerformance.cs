using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <summary>
    /// Étape 9 — Migration non destructive : indexes de performance.
    /// Tous les CREATE INDEX utilisent IF NOT EXISTS (SQLite ≥ 3.3.7)
    /// pour être idempotents en cas de ré-application.
    /// Aucune table ni colonne n'est supprimée ni modifiée.
    /// </summary>
    public partial class IndexesPerformance : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── PiecesCommande ────────────────────────────────────────────
            // DateTerminee : utilisé par CommissionService.CalculerApercu
            //   .Where(p => p.DateTerminee >= debut && p.DateTerminee <= fin)
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_PiecesCommande_DateTerminee " +
                "ON PiecesCommande (DateTerminee);");

            // Statut : filtres fréquents dans AlerteService, CommandeService, CommissionService
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_PiecesCommande_Statut " +
                "ON PiecesCommande (Statut);");

            // IdCouturier : stat couturiers Dashboard, aperçu commissions
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_PiecesCommande_IdCouturier " +
                "ON PiecesCommande (IdCouturier);");

            // IdCommande + Statut : ObtenirPageLightAsync avec filtre statut
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_PiecesCommande_IdCommande_Statut " +
                "ON PiecesCommande (IdCommande, Statut);");

            // ── Commandes ─────────────────────────────────────────────────
            // DateFin : TresorerieService.CalculerBilan, CalculerTotalMateriaux
            //   .Where(c => c.DateFin.Date >= dateDebut && ...)
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Commandes_DateFin " +
                "ON Commandes (DateFin);");

            // Statut : filtres CalculerBilan, VerifierCoherence
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Commandes_Statut " +
                "ON Commandes (Statut);");

            // DateDebut : pagination et filtres période CommandeService
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Commandes_DateDebut " +
                "ON Commandes (DateDebut);");

            // EstSupprimee + DateDebut : requête la plus fréquente (liste principale)
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Commandes_EstSupprimee_DateDebut " +
                "ON Commandes (EstSupprimee, DateDebut DESC);");

            // ── Paiements ─────────────────────────────────────────────────
            // DatePaiement + EstAnnule : TresorerieService.CalculerChiffreAffaires,
            //   Dashboard KPI, graphique 7 jours
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Paiements_DatePaiement_EstAnnule " +
                "ON Paiements (DatePaiement, EstAnnule);");

            // IdCommande : joins fréquents Commande → Paiements
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Paiements_IdCommande " +
                "ON Paiements (IdCommande);");

            // ── Dépenses ──────────────────────────────────────────────────
            // DateDepense + StatutValidation : TresorerieService.CalculerTotalDepenses
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Depenses_DateDepense_Statut " +
                "ON Depenses (DateDepense, StatutValidation);");

            // ── Retours ───────────────────────────────────────────────────
            // NumeroRetour : index UNIQUE filtré (vides exclus) — cohérent avec le
            // modèle EF (IsUnique + HasFilter) et la migration manuelle App.cs.
            // On supprime l'éventuel index non-unique existant avant de créer l'index
            // unique, car SQLite refuse de remplacer un index existant par une définition
            // différente.
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Retours_NumeroRetour;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Retours_NumeroRetour " +
                "ON Retours (NumeroRetour) " +
                "WHERE NumeroRetour != '';");

            // Statut + EstAnnule : FiltrerParStatut, ObtenirPageAsync
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Retours_Statut_EstAnnule " +
                "ON Retours (Statut, EstAnnule);");

            // DateSignalement : tri par défaut dans ObtenirTous, ObtenirPageAsync
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Retours_DateSignalement " +
                "ON Retours (DateSignalement DESC);");

            // IdPieceCommande : AlerteService.ObtenirAlertesRetours, CommissionService
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Retours_IdPieceCommande " +
                "ON Retours (IdPieceCommande);");

            // ── Journal d'audit ───────────────────────────────────────────
            // DateHeureUtc : tri principal, filtre période (JournalAuditWindow, ObtenirParPeriode)
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_JournalAudit_DateHeureUtc " +
                "ON JournalAudit (DateHeureUtc DESC);");

            // IdOperateur : ObtenirParOperateur (carte anti-fraude Dashboard)
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_JournalAudit_IdOperateur " +
                "ON JournalAudit (IdOperateur);");

            // TypeAction : ObtenirParTypeAction
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_JournalAudit_TypeAction " +
                "ON JournalAudit (TypeAction);");

            // ── Commissions ───────────────────────────────────────────────
            // DateCalcul + EstAnnulee : TresorerieService.CalculerTotalCommissions
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Commissions_DateCalcul_EstAnnulee " +
                "ON Commissions (DateCalcul, EstAnnulee);");

            // ── Clients ───────────────────────────────────────────────────
            // Nom + Prenom : recherche texte rapide dans CommandeService, ClientService
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS IX_Clients_Nom_Prenom " +
                "ON Clients (Nom, Prenom);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Suppression propre en cas de rollback
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PiecesCommande_DateTerminee;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PiecesCommande_Statut;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PiecesCommande_IdCouturier;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PiecesCommande_IdCommande_Statut;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Commandes_DateFin;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Commandes_Statut;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Commandes_DateDebut;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Commandes_EstSupprimee_DateDebut;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Paiements_DatePaiement_EstAnnule;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Paiements_IdCommande;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Depenses_DateDepense_Statut;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Retours_NumeroRetour;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Retours_Statut_EstAnnule;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Retours_DateSignalement;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Retours_IdPieceCommande;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_JournalAudit_DateHeureUtc;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_JournalAudit_IdOperateur;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_JournalAudit_TypeAction;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Commissions_DateCalcul_EstAnnulee;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Clients_Nom_Prenom;");
        }
    }
}
