// ============================================================
// ÉTAPE 1 PERF — Tests de non-régression des colonnes dénormalisées
// DateRdvEffective et CommandeActive sur PieceCommande.
//
// Vérifie que :
//   1. Le calcul centralisé (DateRdvHelper) correspond à la règle métier
//   2. Les colonnes sont remplies à la création (Ajouter)
//   3. Les colonnes sont recalculées après modification (ModifierInfosCommande)
//   4. CommandeActive = false après suppression/annulation
//   5. La formule unifiée utilise HeureFin ?? 17:00 (pas HeureDebut)
// ============================================================

using System.IO;
using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace GestionCoutureApp.Tests
{
    [TestFixture]
    public class Etape1perf_ColonnesDenormaliseesTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService _commandeService = null!;

        [SetUp]
        public void Setup()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"etape1perf_{Guid.NewGuid():N}.db");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            _factory = new SimpleFactory(opts);

            using var ctx = _factory.CreateDbContext();
            ctx.Database.EnsureCreated();

            // Boss + 1 couturier
            ctx.Employes.Add(new Employe
            {
                IdEmploye = 1, Nom = "Boss", Prenom = "Test", Identifiant = "boss1",
                MotDePasse = "x", Role = "Boss", Statut = "Actif"
            });
            ctx.Employes.Add(new Employe
            {
                IdEmploye = 2, Nom = "Cout", Prenom = "Prenom", Identifiant = "cout1",
                MotDePasse = "x", Role = "Couturier", Statut = "Actif"
            });
            ctx.Clients.Add(new Client { IdClient = 1, Nom = "Dupont", Prenom = "Jean", Telephone = "770000000" });
            ctx.SaveChanges();
            ctx.ChangeTracker.Clear();

            _commandeService = new CommandeService(_factory, NullLogger<CommandeService>.Instance,
                TestDbHelper.NoOpAudit);
            CommandeService.ViderCacheDoublonPourTests();
        }

        // ── 1. Calcul centralisé ──────────────────────────────────────────

        [Test]
        public void DateRdvHelper_RendezVousException_Prioritaire()
        {
            var rdv = new DateTime(2026, 11, 15, 10, 30, 0);
            var dateFin = new DateTime(2026, 11, 20, 14, 0, 0);
            var result = DateRdvHelper.CalculerDateRdvEffective(rdv, dateFin, TimeSpan.FromHours(17));
            Assert.That(result, Is.EqualTo(rdv));
        }

        [Test]
        public void DateRdvHelper_SansException_UtiliseDateFin_PlusHeureFin()
        {
            var dateFin = new DateTime(2026, 11, 20, 14, 0, 0); // 14h mais .Date = minuit
            var heureFin = new TimeSpan(16, 30, 0);
            var result = DateRdvHelper.CalculerDateRdvEffective(null, dateFin, heureFin);
            Assert.That(result, Is.EqualTo(new DateTime(2026, 11, 20, 16, 30, 0)));
        }

        [Test]
        public void DateRdvHelper_SansHeureFin_Default17h()
        {
            var dateFin = new DateTime(2026, 11, 20, 14, 0, 0);
            var result = DateRdvHelper.CalculerDateRdvEffective(null, dateFin, null);
            Assert.That(result, Is.EqualTo(new DateTime(2026, 11, 20, 17, 0, 0)));
        }

        [Test]
        public void DateRdvHelper_CommandeActive_NonSupprimee_NonAnnulee()
        {
            Assert.That(DateRdvHelper.CalculerCommandeActive(false, false), Is.True);
            Assert.That(DateRdvHelper.CalculerCommandeActive(true, false), Is.False);
            Assert.That(DateRdvHelper.CalculerCommandeActive(false, true), Is.False);
            Assert.That(DateRdvHelper.CalculerCommandeActive(true, true), Is.False);
        }

        // ── 2. Remplissage à la création (Ajouter) ────────────────────────

        [Test]
        public void Ajouter_Piece_DateRdvEffective_Calculee()
        {
            var dateFin = new DateTime(2026, 11, 20, 0, 0, 0);
            var commande = new Commande { IdClient = 1, DateFin = dateFin, HeureDebut = TimeSpan.FromHours(8) };
            var piece = new PieceCommande { TypeVetement = "Robe", MontantCouture = 5000m };

            _commandeService.Ajouter(commande, piece, new List<Mesure>(), 1, "Boss Test");

            using var ctx = _factory.CreateDbContext();
            var pieceDb = ctx.PiecesCommande.AsNoTracking().First();
            // HeureFin est null → défaut 17:00
            Assert.That(pieceDb.DateRdvEffective, Is.EqualTo(new DateTime(2026, 11, 20, 17, 0, 0)));
            Assert.That(pieceDb.CommandeActive, Is.True);
        }

        [Test]
        public void Ajouter_Piece_AvecHeureFin_DateRdvEffective_UtiliseHeureFin()
        {
            var dateFin = new DateTime(2026, 11, 20, 0, 0, 0);
            var commande = new Commande
            {
                IdClient = 1, DateFin = dateFin,
                HeureDebut = TimeSpan.FromHours(8),
                HeureFin = TimeSpan.FromHours(14)
            };
            var piece = new PieceCommande { TypeVetement = "Robe", MontantCouture = 5000m };

            _commandeService.Ajouter(commande, piece, new List<Mesure>(), 1, "Boss Test");

            using var ctx = _factory.CreateDbContext();
            var pieceDb = ctx.PiecesCommande.AsNoTracking().First();
            Assert.That(pieceDb.DateRdvEffective, Is.EqualTo(new DateTime(2026, 11, 20, 14, 0, 0)));
        }

        // ── 3. Recalcul après modification (ModifierInfosCommande) ──────────

        [Test]
        public void ModifierInfosCommande_Recalcule_DateRdvEffective()
        {
            var dateFin = new DateTime(2026, 11, 20);
            var commande = new Commande { IdClient = 1, DateFin = dateFin, HeureDebut = TimeSpan.FromHours(8) };
            var piece = new PieceCommande { TypeVetement = "Robe", MontantCouture = 5000m };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), 1, "Boss Test");

            // Modifier DateFin → recalculer
            var nouvelleDateFin = new DateTime(2026, 12, 1);
            _commandeService.ModifierInfosCommande(commande.IdCommande, 1, nouvelleDateFin,
                TimeSpan.FromHours(8), TimeSpan.FromHours(10), 1, "Boss Test");

            using var ctx = _factory.CreateDbContext();
            var pieceDb = ctx.PiecesCommande.AsNoTracking().First();
            Assert.That(pieceDb.DateRdvEffective, Is.EqualTo(new DateTime(2026, 12, 1, 10, 0, 0)));
        }

        // ── 4. CommandeActive après suppression/annulation ─────────────────

        [Test]
        public void SupprimerAsync_Piece_CommandeActive_False()
        {
            var commande = new Commande { IdClient = 1, DateFin = DateTime.Now.AddDays(7), HeureDebut = TimeSpan.FromHours(8) };
            var piece = new PieceCommande { TypeVetement = "Robe", MontantCouture = 5000m };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), 1, "Boss Test");

            _commandeService.SupprimerAsync(commande.IdCommande, 1, "Boss Test", "Test suppression").GetAwaiter().GetResult();

            using var ctx = _factory.CreateDbContext();
            var pieceDb = ctx.PiecesCommande.AsNoTracking().First();
            Assert.That(pieceDb.CommandeActive, Is.False);
        }

        // ── 5. Formule unifiée : 17:00 et non HeureDebut ───────────────────

        [Test]
        public void RetardAlerteService_CalculerEcheancePiece_Utilise17h_Defaut()
        {
            var piece = new PieceCommande { RendezVousException = null };
            var commande = new Commande
            {
                DateFin = new DateTime(2026, 11, 20, 0, 0, 0),
                HeureDebut = TimeSpan.FromHours(8),  // HeureDebut = 8h
                HeureFin = null                       // pas d'HeureFin
            };

            var echeance = RetardAlerteService.CalculerEcheancePiece(piece, commande);
            // Unifié : HeureFin ?? 17:00 (PAS HeureDebut = 8h)
            Assert.That(echeance, Is.EqualTo(new DateTime(2026, 11, 20, 17, 0, 0)));
            Assert.That(echeance.Hour, Is.EqualTo(17), "Doit utiliser 17h par défaut, pas HeureDebut (8h)");
        }

        // ── Factory ───────────────────────────────────────────────────────
        internal class SimpleFactory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _opts;
            public SimpleFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_opts);
        }
    }
}
