using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class AjoutJournalAuditEtSecuriteFinanciere : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Créer la table JournalAudit (append-only, immuable)
            migrationBuilder.CreateTable(
                name: "JournalAudit",
                columns: table => new
                {
                    IdJournal = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DateHeureUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IdOperateur = table.Column<int>(type: "INTEGER", nullable: false),
                    NomOperateur = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RoleOperateur = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TypeAction = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Entite = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IdEntite = table.Column<int>(type: "INTEGER", nullable: false),
                    ValeursAvant = table.Column<string>(type: "TEXT", nullable: true),
                    ValeursApres = table.Column<string>(type: "TEXT", nullable: true),
                    Motif = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    HashPrecedent = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    HashCourant = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AdresseIp = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    NotificationEnvoyee = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalAudit", x => x.IdJournal);
                });

            // 2. Ajouter des index pour les requêtes fréquentes
            migrationBuilder.CreateIndex(
                name: "IX_JournalAudit_DateHeureUtc",
                table: "JournalAudit",
                column: "DateHeureUtc");

            migrationBuilder.CreateIndex(
                name: "IX_JournalAudit_IdOperateur",
                table: "JournalAudit",
                column: "IdOperateur");

            migrationBuilder.CreateIndex(
                name: "IX_JournalAudit_TypeAction",
                table: "JournalAudit",
                column: "TypeAction");

            migrationBuilder.CreateIndex(
                name: "IX_JournalAudit_Entite_IdEntite",
                table: "JournalAudit",
                columns: new[] { "Entite", "IdEntite" });

            // 3. Rendre Paiement.IdOperateur obligatoire (non-nullable)
            // Note: SQLite ne supporte pas ALTER COLUMN directement
            // On doit recréer la table avec une colonne NOT NULL
            
            // Créer table temporaire avec la nouvelle structure
            migrationBuilder.Sql(@"
                CREATE TABLE Paiements_Temp (
                    IdPaiement INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    IdCommande INTEGER NOT NULL,
                    MontantPaye TEXT NOT NULL,
                    DatePaiement TEXT NOT NULL,
                    ModePaiement TEXT NOT NULL DEFAULT 'Especes',
                    RecuNumero TEXT NOT NULL DEFAULT '',
                    IdOperateur INTEGER NOT NULL DEFAULT 0,
                    NomOperateur TEXT NOT NULL DEFAULT '',
                    EstAnnule INTEGER NOT NULL DEFAULT 0,
                    MotifsAnnulation TEXT NULL,
                    DateAnnulation TEXT NULL,
                    NomAnnulateur TEXT NULL,
                    MontantTotalCommande TEXT NOT NULL,
                    ResteAvantPaiement TEXT NOT NULL,
                    FOREIGN KEY (IdCommande) REFERENCES Commandes(IdCommande) ON DELETE RESTRICT
                );
            ");

            // Copier les données existantes
            migrationBuilder.Sql(@"
                INSERT INTO Paiements_Temp 
                SELECT IdPaiement, IdCommande, MontantPaye, DatePaiement, ModePaiement, RecuNumero,
                       COALESCE(IdOperateur, 0), NomOperateur, EstAnnule, MotifsAnnulation, 
                       DateAnnulation, NomAnnulateur, MontantTotalCommande, ResteAvantPaiement
                FROM Paiements;
            ");

            // Supprimer l'ancienne table
            migrationBuilder.DropTable(name: "Paiements");

            // Renommer la table temporaire
            migrationBuilder.RenameTable(
                name: "Paiements_Temp",
                newName: "Paiements");

            // 4. Ajouter les champs de suppression logique à la table Commandes
            migrationBuilder.AddColumn<bool>(
                name: "EstSupprimee",
                table: "Commandes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MotifSuppression",
                table: "Commandes",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateSuppression",
                table: "Commandes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdOperateurSuppression",
                table: "Commandes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomOperateurSuppression",
                table: "Commandes",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            // 5. Créer un index pour les commandes non supprimées (performance)
            migrationBuilder.CreateIndex(
                name: "IX_Commandes_EstSupprimee",
                table: "Commandes",
                column: "EstSupprimee");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Supprimer la table JournalAudit
            migrationBuilder.DropTable(name: "JournalAudit");

            // Retirer les colonnes de suppression logique de Commandes
            migrationBuilder.DropIndex(
                name: "IX_Commandes_EstSupprimee",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "EstSupprimee",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "MotifSuppression",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "DateSuppression",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "IdOperateurSuppression",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "NomOperateurSuppression",
                table: "Commandes");

            // Recréer Paiements avec IdOperateur nullable
            migrationBuilder.Sql(@"
                CREATE TABLE Paiements_Temp (
                    IdPaiement INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    IdCommande INTEGER NOT NULL,
                    MontantPaye TEXT NOT NULL,
                    DatePaiement TEXT NOT NULL,
                    ModePaiement TEXT NOT NULL DEFAULT 'Especes',
                    RecuNumero TEXT NOT NULL DEFAULT '',
                    IdOperateur INTEGER NULL,
                    NomOperateur TEXT NOT NULL DEFAULT '',
                    EstAnnule INTEGER NOT NULL DEFAULT 0,
                    MotifsAnnulation TEXT NULL,
                    DateAnnulation TEXT NULL,
                    NomAnnulateur TEXT NULL,
                    MontantTotalCommande TEXT NOT NULL,
                    ResteAvantPaiement TEXT NOT NULL,
                    FOREIGN KEY (IdCommande) REFERENCES Commandes(IdCommande) ON DELETE RESTRICT
                );
            ");

            migrationBuilder.Sql(@"
                INSERT INTO Paiements_Temp 
                SELECT * FROM Paiements;
            ");

            migrationBuilder.DropTable(name: "Paiements");

            migrationBuilder.RenameTable(
                name: "Paiements_Temp",
                newName: "Paiements");
        }
    }
}
