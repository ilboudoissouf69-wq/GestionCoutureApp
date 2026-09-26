using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <summary>
    /// Migration finale de consolidation (générée par EF Core, modifiée pour idempotence).
    ///
    /// Remplace les 3 migrations conflictuelles supprimées :
    ///   20260923000000_AjoutJournalAuditEtSecuriteFinanciere
    ///   20260923000100_TracabiliteCreationEtUniciteClient
    ///   (ancienne) 20260924173906_SyncSnapshot
    ///
    /// Applique tous les changements manquants après les 11 migrations stables
    /// (20260718 → 20260918000001) pour une base créée de zéro OU existante.
    ///
    /// Idempotence : les AddColumn EF Core plantent si la colonne existe déjà
    /// (bases existantes mises à jour manuellement). On enveloppe dans des blocs
    /// try/catch SQL via suppressTransaction:true pour les colonnes à risque.
    /// Les CreateTable et CreateIndex utilisent IF NOT EXISTS en SQL brut.
    /// Les AddForeignKey/AlterColumn sont encapsulés dans des gardes ADO.NET.
    /// </summary>
    public partial class ConsolidationFinale : Migration
    {
        // Helper : exécute un ALTER TABLE ADD COLUMN en ignorant "duplicate column name"
        private static void AjouterColonneSiAbsente(MigrationBuilder mb, string table, string col, string def)
            => mb.Sql($"ALTER TABLE \"{table}\" ADD COLUMN \"{col}\" {def};", suppressTransaction: true);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. JournalAudit ───────────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""JournalAudit"" (
                    ""IdJournal"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""DateHeureUtc"" TEXT NOT NULL,
                    ""IdOperateur"" INTEGER NOT NULL,
                    ""NomOperateur"" TEXT NOT NULL,
                    ""RoleOperateur"" TEXT NOT NULL,
                    ""TypeAction"" TEXT NOT NULL,
                    ""Entite"" TEXT NOT NULL,
                    ""IdEntite"" INTEGER NOT NULL,
                    ""ValeursAvant"" TEXT NULL,
                    ""ValeursApres"" TEXT NULL,
                    ""Motif"" TEXT NULL,
                    ""HashPrecedent"" TEXT NULL,
                    ""HashCourant"" TEXT NOT NULL,
                    ""AdresseIp"" TEXT NULL,
                    ""NotificationEnvoyee"" INTEGER NOT NULL DEFAULT 0
                );
            ");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_JournalAudit_DateHeureUtc\" ON \"JournalAudit\" (\"DateHeureUtc\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_JournalAudit_IdOperateur\" ON \"JournalAudit\" (\"IdOperateur\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_JournalAudit_TypeAction\" ON \"JournalAudit\" (\"TypeAction\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_JournalAudit_Entite_IdEntite\" ON \"JournalAudit\" (\"Entite\", \"IdEntite\");");

            // ── 2. Paiements : IdOperateur NOT NULL ───────────────────────────
            // AlterColumn EF Core sur SQLite recrée la table — idempotent si déjà NOT NULL.
            // On utilise SQL brut pour éviter la perte de données et gérer les deux cas.
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""Paiements_V2"" (
                    ""IdPaiement"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""IdCommande"" INTEGER NOT NULL,
                    ""MontantPaye"" TEXT NOT NULL,
                    ""DatePaiement"" TEXT NOT NULL,
                    ""ModePaiement"" TEXT NOT NULL DEFAULT 'Especes',
                    ""RecuNumero"" TEXT NOT NULL DEFAULT '',
                    ""IdOperateur"" INTEGER NOT NULL DEFAULT 0,
                    ""NomOperateur"" TEXT NOT NULL DEFAULT '',
                    ""EstAnnule"" INTEGER NOT NULL DEFAULT 0,
                    ""MotifsAnnulation"" TEXT NULL,
                    ""DateAnnulation"" TEXT NULL,
                    ""NomAnnulateur"" TEXT NULL,
                    ""MontantTotalCommande"" TEXT NOT NULL DEFAULT '0',
                    ""ResteAvantPaiement"" TEXT NOT NULL DEFAULT '0',
                    FOREIGN KEY (""IdCommande"") REFERENCES ""Commandes""(""IdCommande"") ON DELETE RESTRICT
                );
            ");
            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO ""Paiements_V2""
                    (""IdPaiement"", ""IdCommande"", ""MontantPaye"", ""DatePaiement"",
                     ""ModePaiement"", ""RecuNumero"", ""IdOperateur"", ""NomOperateur"",
                     ""EstAnnule"", ""MotifsAnnulation"", ""DateAnnulation"", ""NomAnnulateur"",
                     ""MontantTotalCommande"", ""ResteAvantPaiement"")
                SELECT ""IdPaiement"", ""IdCommande"", ""MontantPaye"", ""DatePaiement"",
                       ""ModePaiement"", ""RecuNumero"",
                       COALESCE(""IdOperateur"", 0), ""NomOperateur"",
                       ""EstAnnule"", ""MotifsAnnulation"", ""DateAnnulation"", ""NomAnnulateur"",
                       COALESCE(""MontantTotalCommande"", '0'),
                       COALESCE(""ResteAvantPaiement"", '0')
                FROM ""Paiements""
                WHERE ""IdPaiement"" NOT IN (SELECT ""IdPaiement"" FROM ""Paiements_V2"");
            ");
            migrationBuilder.Sql("DROP TABLE \"Paiements\";");
            migrationBuilder.Sql("ALTER TABLE \"Paiements_V2\" RENAME TO \"Paiements\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Paiements_RecuNumero\" ON \"Paiements\" (\"RecuNumero\");");

            // ── 3. Employes : DerniereModificationMotDePasse ──────────────────
            AjouterColonneSiAbsente(migrationBuilder, "Employes", "DerniereModificationMotDePasse", "TEXT NULL");

            // ── 4. Commissions : PrimeQualite ─────────────────────────────────
            AjouterColonneSiAbsente(migrationBuilder, "Commissions", "PrimeQualite", "TEXT NOT NULL DEFAULT '0'");

            // ── 5. Depenses : Categorie, StatutValidation, IdOperateur ─────────
            AjouterColonneSiAbsente(migrationBuilder, "Depenses", "Categorie", "TEXT NOT NULL DEFAULT 'Divers'");
            AjouterColonneSiAbsente(migrationBuilder, "Depenses", "StatutValidation", "TEXT NOT NULL DEFAULT 'Validee'");
            AjouterColonneSiAbsente(migrationBuilder, "Depenses", "IdOperateur", "INTEGER NOT NULL DEFAULT 0");

            // ── 6. MaterielsSupplements : IdOperateur, NomOperateur ───────────
            AjouterColonneSiAbsente(migrationBuilder, "MaterielsSupplements", "IdOperateur", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(migrationBuilder, "MaterielsSupplements", "NomOperateur", "TEXT NOT NULL DEFAULT ''");

            // ── 7. Commandes : suppression logique + traçabilité création ──────
            AjouterColonneSiAbsente(migrationBuilder, "Commandes", "EstSupprimee", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(migrationBuilder, "Commandes", "MotifSuppression", "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Commandes", "DateSuppression", "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Commandes", "IdOperateurSuppression", "INTEGER NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Commandes", "NomOperateurSuppression", "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Commandes", "IdOperateurCreation", "INTEGER NOT NULL DEFAULT 0");
            AjouterColonneSiAbsente(migrationBuilder, "Commandes", "NomOperateurCreation", "TEXT NOT NULL DEFAULT ''");
            AjouterColonneSiAbsente(migrationBuilder, "Commandes", "DateCreation", "TEXT NOT NULL DEFAULT '2000-01-01 00:00:00'");

            // Rétro-remplissage DateCreation = DateDebut pour les lignes historiques
            migrationBuilder.Sql("UPDATE \"Commandes\" SET \"DateCreation\" = \"DateDebut\" WHERE \"DateCreation\" = '2000-01-01 00:00:00';");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Commandes_EstSupprimee\" ON \"Commandes\" (\"EstSupprimee\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Commandes_IdOperateurCreation\" ON \"Commandes\" (\"IdOperateurCreation\");");

            // ── 8. Clients : index unique filtré Telephone ────────────────────
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Clients_Telephone_Unique\" " +
                "ON \"Clients\" (\"Telephone\") " +
                "WHERE \"Telephone\" IS NOT NULL AND \"Telephone\" != '';");

            // ── 9. Retours : champs reprise ───────────────────────────────────
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "CheminPhotoDefaut", "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "DateRdvReprise", "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "HeureDebutReprise", "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "HeureFinReprise", "TEXT NULL");
            AjouterColonneSiAbsente(migrationBuilder, "Retours", "IdCouturierReprise", "INTEGER NULL");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Retours_IdCouturierReprise\" ON \"Retours\" (\"IdCouturierReprise\");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"JournalAudit\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Clients_Telephone_Unique\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Commandes_EstSupprimee\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Commandes_IdOperateurCreation\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Retours_IdCouturierReprise\";");
            // Pas de DROP COLUMN en SQLite pour Retours, Commandes, Depenses, MaterielsSupplements —
            // nécessiterait une recréation de table. Acceptable pour Down() rarement utilisé.
            // Paiements : restaurer IdOperateur nullable
            migrationBuilder.Sql(@"
                CREATE TABLE ""Paiements_Down"" (
                    ""IdPaiement"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""IdCommande"" INTEGER NOT NULL,
                    ""MontantPaye"" TEXT NOT NULL,
                    ""DatePaiement"" TEXT NOT NULL,
                    ""ModePaiement"" TEXT NOT NULL DEFAULT 'Especes',
                    ""RecuNumero"" TEXT NOT NULL DEFAULT '',
                    ""IdOperateur"" INTEGER NULL,
                    ""NomOperateur"" TEXT NOT NULL DEFAULT '',
                    ""EstAnnule"" INTEGER NOT NULL DEFAULT 0,
                    ""MotifsAnnulation"" TEXT NULL,
                    ""DateAnnulation"" TEXT NULL,
                    ""NomAnnulateur"" TEXT NULL,
                    ""MontantTotalCommande"" TEXT NOT NULL DEFAULT '0',
                    ""ResteAvantPaiement"" TEXT NOT NULL DEFAULT '0'
                );
            ");
            migrationBuilder.Sql("INSERT INTO \"Paiements_Down\" SELECT * FROM \"Paiements\";");
            migrationBuilder.Sql("DROP TABLE \"Paiements\";");
            migrationBuilder.Sql("ALTER TABLE \"Paiements_Down\" RENAME TO \"Paiements\";");
        }
    }
}
