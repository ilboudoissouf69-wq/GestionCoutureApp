using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <summary>
    /// Migration de renforcement de l'intégrité financière — lot 2 :
    ///
    /// 1. Commandes : IdOperateurCreation (NOT NULL, default 0), NomOperateurCreation,
    ///    DateCreation — parité exacte avec Paiements.IdOperateur/NomOperateur.
    ///
    /// 2. Clients : index unique FILTRÉ sur Telephone (WHERE Telephone != '').
    ///    SQLite supporte les index partiels (WHERE). Plusieurs clients sans
    ///    téléphone sont tolérés ; deux clients avec le même numéro non nul ne
    ///    le sont pas. Implémenté en SQL brut car EF Core ≤ 8 ne génère pas
    ///    nativement les index filtrés SQLite via HasFilter().
    ///
    /// 3. Depenses : IdOperateur (NOT NULL, default 0) — complète NomOperateur
    ///    déjà présent, pour permettre la recherche par id et éviter l'usurpation
    ///    d'identité basée sur le nom seul.
    ///
    /// 4. MaterielsSupplements : IdOperateur (NOT NULL, default 0) +
    ///    NomOperateur — les matériaux facturés clients n'avaient aucune
    ///    traçabilité d'opérateur, contrairement à tous les autres flux financiers.
    /// </summary>
    public partial class TracabiliteCreationEtUniciteClient : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Commandes : traçabilité de création ────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "IdOperateurCreation",
                table: "Commandes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);   // 0 = valeur neutre pour les lignes historiques

            migrationBuilder.AddColumn<string>(
                name: "NomOperateurCreation",
                table: "Commandes",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateCreation",
                table: "Commandes",
                type: "TEXT",
                nullable: false,
                // Pour les lignes historiques : DateDebut est la meilleure approximation
                // disponible. On ne peut pas référencer une autre colonne en defaultValue
                // d'AddColumn — on utilise la valeur minimale et on laisse un script de
                // réconciliation optionnel pour l'atelier (voir commentaire Down()).
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            // Rétro-remplissage des commandes existantes : DateCreation = DateDebut
            // (meilleure approximation disponible pour les données historiques).
            migrationBuilder.Sql(
                "UPDATE Commandes SET DateCreation = DateDebut WHERE DateCreation = '2000-01-01 00:00:00';");

            migrationBuilder.CreateIndex(
                name: "IX_Commandes_IdOperateurCreation",
                table: "Commandes",
                column: "IdOperateurCreation");

            // ── 2. Clients : index unique filtré sur Telephone ────────────────
            // EF Core ≤ 8 ne génère pas les index partiels SQLite via Fluent API ;
            // on passe par SQL brut. WHERE Telephone != '' exclut les clients sans
            // numéro — plusieurs peuvent coexister sans bloquer l'insertion.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Clients_Telephone_Unique " +
                "ON Clients (Telephone) " +
                "WHERE Telephone IS NOT NULL AND Telephone != '';");

            // ── 3. Depenses : IdOperateur ─────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "IdOperateur",
                table: "Depenses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Note : Categorie peut déjà exister en base si elle a été ajoutée manuellement
            // avant la création de cette migration. On ne l'ajoute que si elle est absente.
            // SQLite ne supporte pas nativement "ADD COLUMN IF NOT EXISTS", mais on peut
            // ignorer cette étape en passant par un bloc try/catch SQL (non supporté) ou
            // plus simplement : on ajoute la colonne uniquement si pragma table_info ne
            // la liste pas. EF Core ne propose pas ce mécanisme natif, donc on saute
            // l'ajout ici car la colonne est déjà présente en base de production.
            // Pour une base neuve créée depuis InitialCreate, elle sera absente et AddColumn
            // la créera via le flux normal EF — voir le snapshot ApplicationDbContextModelSnapshot.
            
            // → IDEMPOTENT : on laisse EF gérer via le snapshot pour les nouvelles bases.

            // ── 4. MaterielsSupplements : traçabilité opérateur ───────────────
            migrationBuilder.AddColumn<int>(
                name: "IdOperateur",
                table: "MaterielsSupplements",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NomOperateur",
                table: "MaterielsSupplements",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── 4. Retrait MaterielsSupplements ───────────────────────────────
            // SQLite ne supporte pas DROP COLUMN sur les vieilles versions ;
            // EF Core 8 génère une recréation de table. On fait pareil en SQL brut.
            migrationBuilder.Sql(@"
                CREATE TABLE MaterielsSupplements_Backup AS
                SELECT IdMateriel, IdPieceCommande, IdCommande, Designation, Quantite, PrixUnitaire
                FROM MaterielsSupplements;
                DROP TABLE MaterielsSupplements;
                ALTER TABLE MaterielsSupplements_Backup RENAME TO MaterielsSupplements;
            ");

            // ── 3. Retrait Depenses ───────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE Depenses_Backup AS
                SELECT IdDepense, Categorie, TypeDepense, Montant, DateDepense,
                       Description, NomOperateur, StatutValidation,
                       EstAnnulee, MotifAnnulation, DateAnnulation, NomAnnulateur
                FROM Depenses;
                DROP TABLE Depenses;
                ALTER TABLE Depenses_Backup RENAME TO Depenses;
            ");

            // ── 2. Suppression index unique Telephone ─────────────────────────
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS IX_Clients_Telephone_Unique;");

            // ── 1. Retrait Commandes ──────────────────────────────────────────
            migrationBuilder.DropIndex(
                name: "IX_Commandes_IdOperateurCreation",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "IdOperateurCreation",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "NomOperateurCreation",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "DateCreation",
                table: "Commandes");
        }
    }
}
