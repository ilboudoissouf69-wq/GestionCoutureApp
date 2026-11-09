using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace GestionCoutureApp.Tests;

[TestFixture]
public sealed class MigrationReelleTests
{
    [Test]
    public void Demarrage_AppliqueLesMigrationsPerformanceSurSchemaAncienEtBackfill1000Pieces()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"migration_reelle_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
CREATE TABLE __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT NOT NULL);
CREATE TABLE Clients (IdClient INTEGER PRIMARY KEY, Nom TEXT NOT NULL, Prenom TEXT NOT NULL, Telephone TEXT NULL);
CREATE TABLE Commandes (IdCommande INTEGER PRIMARY KEY, IdClient INTEGER NOT NULL, DateDebut TEXT NOT NULL,
    HeureDebut TEXT NOT NULL, DateFin TEXT NOT NULL, HeureFin TEXT NULL, Statut TEXT NOT NULL DEFAULT 'A faire', EstSupprimee INTEGER NOT NULL DEFAULT 0,
    EstAnnulee INTEGER NOT NULL DEFAULT 0);
CREATE TABLE PiecesCommande (IdPieceCommande INTEGER PRIMARY KEY, IdCommande INTEGER NOT NULL, Statut TEXT NOT NULL,
    IdCouturier INTEGER NULL, RendezVousException TEXT NULL, DateTerminee TEXT NULL,
    MontantCouture TEXT NOT NULL DEFAULT '0');
CREATE TABLE Employes (IdEmploye INTEGER PRIMARY KEY, Identifiant TEXT NOT NULL);
CREATE TABLE MaterielsSupplements (IdMateriel INTEGER PRIMARY KEY, IdCommande INTEGER, Designation TEXT, Quantite INTEGER, PrixUnitaire TEXT);
CREATE TABLE Paiements (IdPaiement INTEGER PRIMARY KEY, IdCommande INTEGER NOT NULL, MontantPaye TEXT NOT NULL,
    DatePaiement TEXT NOT NULL, ModePaiement TEXT NOT NULL, RecuNumero TEXT NOT NULL DEFAULT '',
    IdOperateur INTEGER, NomOperateur TEXT NOT NULL DEFAULT '', EstAnnule INTEGER NOT NULL DEFAULT 0,
    MotifsAnnulation TEXT NULL, DateAnnulation TEXT NULL, NomAnnulateur TEXT NULL,
    MontantTotalCommande TEXT NOT NULL DEFAULT '0', ResteAvantPaiement TEXT NOT NULL DEFAULT '0');
CREATE TABLE Depenses (IdDepense INTEGER PRIMARY KEY, DateDepense TEXT, StatutValidation TEXT);
CREATE TABLE Retours (IdRetour INTEGER PRIMARY KEY, NumeroRetour TEXT, Statut TEXT, EstAnnule INTEGER,
    DateSignalement TEXT, DateRdvReprise TEXT, IdPieceCommande INTEGER);
CREATE TABLE JournalAudit (IdJournal INTEGER PRIMARY KEY, DateHeureUtc TEXT, TypeAction TEXT, IdOperateur INTEGER);
CREATE TABLE Commissions (IdCommission INTEGER PRIMARY KEY, DateCalcul TEXT, EstAnnulee INTEGER, PrimeQualite TEXT NOT NULL DEFAULT '0');
CREATE TABLE SuivisAlerteRetard (IdSuivi INTEGER PRIMARY KEY, TypeCible TEXT, IdCible INTEGER, NbPreventions INTEGER,
    DateRdvTraite TEXT, DateDernierMessage TEXT, IdClient INTEGER);
INSERT INTO Clients VALUES (1, 'Élodie', 'Dûval', '70000000');
INSERT INTO Commandes VALUES (1, 1, '2026-11-01 00:00:00', '08:00:00', '2026-12-01 00:00:00', '16:30:00', 'A faire', 0, 0);
WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<1000)
INSERT INTO PiecesCommande (IdPieceCommande, IdCommande, Statut, IdCouturier) SELECT x, 1, 'A faire', 2 FROM n;
INSERT INTO Paiements (IdPaiement, IdCommande, MontantPaye, DatePaiement, ModePaiement, RecuNumero)
VALUES (1, 1, '0', '2026-11-02 00:00:00', 'Especes', ''),
       (2, 1, '0', '2026-11-02 00:00:00', 'Especes', '');";
            command.ExecuteNonQuery();
        }

        string[] dejaAppliquees =
        [
            "20260916000000_AjoutRetoursChampsReprise",
            "20260918000001_AjoutDerniereModificationMotDePasse",
            "20261005000001_DateTermineePieceCommande",
            "20261010000000_Phase1_VerrouillageProgressif",
            "20261015000000_Etape2_VerrouillageCommandes",
            "20261015000000_AjoutMesuresClient",
            "20261015000001_Etape6_RetourNumeroStatut",
            "20261016000000_AjoutBrouillonCommande",
            "20261016120000_AjoutSuiviAlerteRetard"
        ];
        foreach (var id in dejaAppliquees)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO __EFMigrationsHistory VALUES ($id, '8.0.11')";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString).Options;
        using var context = new ApplicationDbContext(options);
        GestionCoutureApp.App.AppliquerMigrationsManquantes(context, new JournalMuet(), connectionString);

        // Simuler une échéance historique enregistrée avec les secondes, puis
        // rejouer uniquement le backfill de normalisation ajouté le 09/11.
        using (var seedPrecision = connection.CreateCommand())
        {
            seedPrecision.CommandText = @"UPDATE PiecesCommande
                SET DateRdvEffective='2026-12-01 16:30:47' WHERE IdPieceCommande=1;
                DELETE FROM __EFMigrationsHistory
                WHERE MigrationId='20261109000000_NormaliserDateRdvEffectiveMinute';";
            seedPrecision.ExecuteNonQuery();
        }
        GestionCoutureApp.App.AppliquerMigrationsManquantes(context, new JournalMuet(), connectionString);

        using var verify = connection.CreateCommand();
        verify.CommandText = @"SELECT COUNT(*) FROM PiecesCommande
            WHERE DateRdvEffective='2026-12-01 16:30:00'
              AND DateMiParcours='2026-11-16 12:15:00' AND CommandeActive=1";
        Assert.That(Convert.ToInt32(verify.ExecuteScalar()), Is.EqualTo(1000));

        verify.CommandText = "SELECT NomRecherche || '|' || PrenomNomRecherche FROM Clients WHERE IdClient=1";
        Assert.That(verify.ExecuteScalar()?.ToString(), Is.EqualTo("elodie duval|duval elodie"));

        verify.CommandText = @"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN
            ('IX_PiecesCommande_Active_DateRdv_Statut','IX_PiecesCommande_Active_Statut_DateRdv_IdCommande',
             'IX_PiecesCommande_Active_Statut_DateMiParcours','IX_Clients_NomRecherche',
             'IX_Clients_PrenomNomRecherche','IX_Retours_EstAnnule_Statut_DateRdvReprise')";
        Assert.That(Convert.ToInt32(verify.ExecuteScalar()), Is.EqualTo(6));

        verify.CommandText = "SELECT COUNT(*) FROM Paiements WHERE RecuNumero = ''";
        Assert.That(Convert.ToInt32(verify.ExecuteScalar()), Is.EqualTo(2),
            "Les reçus vides historiques doivent être conservés.");
        verify.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND name='IX_Paiements_RecuNumero'";
        Assert.That(verify.ExecuteScalar()?.ToString(), Does.Contain("WHERE \"RecuNumero\" != ''"));
        verify.CommandText = @"INSERT INTO Paiements
            (IdPaiement, IdCommande, MontantPaye, DatePaiement, ModePaiement, RecuNumero)
            VALUES (3, 1, '0', '2026-11-02 00:00:00', 'Especes', 'REC-UNIQUE')";
        verify.ExecuteNonQuery();
        verify.CommandText = @"INSERT INTO Paiements
            (IdPaiement, IdCommande, MontantPaye, DatePaiement, ModePaiement, RecuNumero)
            VALUES (4, 1, '0', '2026-11-02 00:00:00', 'Especes', 'REC-UNIQUE')";
        Assert.Throws<SqliteException>(() => verify.ExecuteNonQuery(),
            "L'index filtré doit continuer à refuser deux reçus non vides identiques.");
    }

    private sealed class JournalMuet : ILogService
    {
        public void LogInfo(string message, string? utilisateur = null) { }
        public void LogWarning(string message, string? utilisateur = null) { }
        public void LogError(string message, Exception? exception = null, string? utilisateur = null) { }
        public void LogAction(string action, string details, string? utilisateur = null) { }
        public string ObtenirCheminDossierLogs() => Path.GetTempPath();
    }
}
