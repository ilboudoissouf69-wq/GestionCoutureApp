using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <summary>
    /// Migration finale de consolidation.
    ///
    /// Remplace les 3 migrations conflictuelles supprimées :
    ///   20260923000000_AjoutJournalAuditEtSecuriteFinanciere
    ///   20260923000100_TracabiliteCreationEtUniciteClient
    ///   20260924173906_SyncSnapshot
    ///
    /// ARCHITECTURE DE MIGRATION CHOISIE (2026-09-26) :
    ///
    /// Cette migration est intentionnellement un no-op dans son Up(). Toute la
    /// logique DDL vit dans App.cs → AppliquerMigrationsManquantes(), pour la
    /// raison suivante :
    ///
    /// SQLite ne supporte pas ADD COLUMN IF NOT EXISTS. EF Core propose
    /// suppressTransaction:true comme contournement supposé, mais c'est trompeur :
    /// suppressTransaction sort la commande de la transaction EF Core ambiante,
    /// il ne supprime PAS les erreurs SQLite. Si la migration s'exécute une
    /// deuxième fois (base partiellement migrée, crash au milieu), chaque ALTER
    /// TABLE ADD COLUMN qui porte sur une colonne déjà présente plantera avec
    /// "SQLite Error 1: duplicate column name: X", rendant la migration
    /// complètement non-rejoué.
    ///
    /// La seule façon vraiment robuste en SQLite pur est :
    ///   - Vérifier pragma_table_info() avant chaque ALTER TABLE via ADO.NET
    ///   - Chaque ALTER est dans son propre try/catch individuel
    ///   - L'enregistrement dans __EFMigrationsHistory n'a lieu qu'APRÈS le
    ///     succès de toutes les opérations
    ///
    /// C'est exactement ce que fait AppliquerMigrationsManquantes() dans App.cs.
    ///
    /// COLONNES GÉRÉES PAR CETTE MIGRATION (via AppliquerMigrationsManquantes) :
    ///   Commissions   : PrimeQualite
    ///   Paiements     : IdOperateur NOT NULL (recréation de table)
    ///   Employes      : DerniereModificationMotDePasse
    ///   Depenses      : Categorie, StatutValidation, IdOperateur
    ///   MaterielsSupplements : IdOperateur, NomOperateur
    ///   Commandes     : EstSupprimee, MotifSuppression, DateSuppression,
    ///                   IdOperateurSuppression, NomOperateurSuppression,
    ///                   IdOperateurCreation, NomOperateurCreation, DateCreation
    ///   Retours       : CheminPhotoDefaut, DateRdvReprise, HeureDebutReprise,
    ///                   HeureFinReprise, IdCouturierReprise
    ///   Tables        : JournalAudit (CREATE TABLE IF NOT EXISTS)
    ///   Index         : IX_Commandes_EstSupprimee, IX_Commandes_IdOperateurCreation,
    ///                   IX_Clients_Telephone_Unique, IX_Retours_IdCouturierReprise,
    ///                   IX_JournalAudit_*
    ///
    /// POUR UN UTILISATEUR EN PRODUCTION dont cette migration est marquée
    /// "non appliquée" malgré des colonnes partiellement présentes :
    ///
    ///   1. Vérifiez l'état de la base :
    ///        SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId;
    ///        PRAGMA table_info('Commandes');   -- chercher EstSupprimee
    ///
    ///   2. Si EstSupprimee existe sur Commandes mais que la migration est absente
    ///      de __EFMigrationsHistory, la migration a été partiellement appliquée.
    ///      Enregistrez-la manuellement :
    ///        INSERT OR IGNORE INTO __EFMigrationsHistory
    ///          VALUES ('20260925000000_ConsolidationFinale', '8.0.11');
    ///
    ///   3. Relancez l'application — AppliquerMigrationsManquantes() ajoutera les
    ///      colonnes manquantes restantes en toute sécurité, puis le check de
    ///      schéma confirmera la cohérence avant d'ouvrir l'écran de connexion.
    /// </summary>
    public partial class ConsolidationFinale : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── NO-OP INTENTIONNEL ────────────────────────────────────────────
            //
            // Voir le commentaire de classe ci-dessus pour la justification complète.
            //
            // Toute la logique DDL est dans App.cs → AppliquerMigrationsManquantes().
            // Cette méthode est appelée après context.Database.Migrate() à chaque
            // démarrage de l'application, avec vérification pragma_table_info et
            // try/catch individuel par colonne.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down() est laissé vide intentionnellement :
            //   - Les colonnes ajoutées à des tables existantes ne peuvent pas être
            //     retirées proprement en SQLite sans recréer la table entière.
            //   - La table JournalAudit contient des données d'audit immuables :
            //     la supprimer serait contraire au principe d'intégrité du journal.
            //   - En pratique, Down() n'est jamais appelé en production sur ce projet.
        }
    }
}
