using GestionCoutureApp.Models;
using GestionCoutureApp.Data;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace GestionCoutureApp.Tests
{
    /// <summary>
    /// Tests Phase 2 — Intégrité financière.
    /// - Arrondi AwayFromZero
    /// - Répartition exacte (somme des parts == encaissé couture)
    /// - Blocage annulation paiement si commission active
    /// - PaiementService.Annuler autorisé si commission annulée
    /// </summary>
    [TestFixture]
    public class Phase2_IntegriteFinanciereTests
    {
        private IDbContextFactory<ApplicationDbContext> _contextFactory = null!;
        private PaiementService _paiementService = null!;
        private CommissionService _commissionService = null!;
        private ApplicationDbContext _context = null!;

        private const int IdBoss = 1;
        private const int IdCouturier = 2;
        private const int IdClient = 1;

        [SetUp]
        public void Setup()
        {
            CommandeService.ViderCacheDoublonPourTests();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"Phase2_{Guid.NewGuid()}")
                .Options;

            _contextFactory = new Phase2DbFactory(options);
            _context = _contextFactory.CreateDbContext();

            var logFact = NullLoggerFactory.Instance;
            _paiementService   = new PaiementService(
                _contextFactory, logFact.CreateLogger<PaiementService>());
            _commissionService = new CommissionService(
                _contextFactory, logFact.CreateLogger<CommissionService>(),
                new MockParametresService());

            SeedData();
        }

        private void SeedData()
        {
            _context.Employes.AddRange(
                new Employe { IdEmploye = IdBoss, Nom = "TRAORE", Prenom = "Ibrahim",
                    Identifiant = "boss", MotDePasse = "x", Role = "Boss", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier, Nom = "KABORE", Prenom = "Dramane",
                    Identifiant = "cut", MotDePasse = "x", Role = "Couturier", Statut = "Actif" }
            );
            _context.Clients.Add(new Client { IdClient = IdClient, Nom = "SARR",
                Prenom = "Fatou", Telephone = "70000000" });
            _context.SaveChanges();
        }

        [TearDown]
        public void TearDown() => _context?.Dispose();

        // ================================================================
        // TESTS — Arrondi AwayFromZero
        // ================================================================

        [Test]
        public void ARR01_MidpointRounding_AwayFromZero_0_5_Arrondi_A_1()
        {
            // 0.5 avec AwayFromZero doit donner 1, pas 0 (banker's rounding)
            decimal val = 0.5m;
            decimal arrondi = Math.Round(val, 0, MidpointRounding.AwayFromZero);
            Assert.That(arrondi, Is.EqualTo(1m));
        }

        [Test]
        public void ARR02_RepartirEncaisse_Somme_Egale_Encaisse()
        {
            // 3 pièces avec montants impairs : la somme des parts doit être == encaissé
            decimal encaisseCouture = 9999m; // montant impair intentionnel
            var pieces = new List<PieceCommande>
            {
                new PieceCommande { MontantCouture = 3333m },
                new PieceCommande { MontantCouture = 3333m },
                new PieceCommande { MontantCouture = 3334m },
            };
            var parts = CommissionService.RepartirEncaisseSurPieces(pieces, encaisseCouture);

            Assert.That(parts.Sum(), Is.EqualTo(encaisseCouture),
                "La somme des parts doit être exactement égale à l'encaissé couture.");
            Assert.That(parts, Has.All.GreaterThanOrEqualTo(0m),
                "Aucune part ne doit être négative.");
        }

        [Test]
        public void ARR03_RepartirEncaisse_Montants_Impairs_3_Pieces()
        {
            // Cas de test de la spec : montants impairs + 3 pièces
            decimal encaisseCouture = 7777m;
            var pieces = new List<PieceCommande>
            {
                new PieceCommande { MontantCouture = 2000m },
                new PieceCommande { MontantCouture = 3000m },
                new PieceCommande { MontantCouture = 1500m },
            };
            var parts = CommissionService.RepartirEncaisseSurPieces(pieces, encaisseCouture);

            // La somme doit être exactement l'encaissé
            Assert.That(parts.Sum(), Is.EqualTo(encaisseCouture));
            // Toutes les parts sont positives
            Assert.That(parts.Count, Is.EqualTo(3));
            Assert.That(parts, Has.All.GreaterThanOrEqualTo(0m));
        }

        [Test]
        public void ARR04_RepartirEncaisse_Liste_Vide_Retourne_Vide()
        {
            var parts = CommissionService.RepartirEncaisseSurPieces(
                new List<PieceCommande>(), 5000m);
            Assert.That(parts, Is.Empty);
        }

        // ================================================================
        // TESTS — Blocage annulation paiement si commission active
        // ================================================================

        private (Commande, PieceCommande) CreerCommandeAvecCommission()
        {
            var commande = new Commande
            {
                IdClient = IdClient,
                IdOperateurCreation = IdBoss,
                NomOperateurCreation = "Ibrahim TRAORE",
                DateDebut = DateTime.Now.AddDays(-10),
                DateFin   = DateTime.Now.AddDays(5),
                Statut    = "En cours",
#pragma warning disable CS0618
                TypeVetement = "Pantalon",
                MontantTotal = 0
#pragma warning restore CS0618
            };
            var piece = new PieceCommande
            {
                TypeVetement = "Pantalon",
                MontantCouture = 5000m,
                Statut = "Terminee",
                IdCouturier = IdCouturier,
                DateTerminee = DateTime.Now.AddDays(-3)
            };
            commande.Pieces = new List<PieceCommande> { piece };
            _context.Commandes.Add(commande);
            _context.SaveChanges();

            // Ajouter un paiement valide
            var paiement = new Paiement
            {
                IdCommande = commande.IdCommande,
                MontantPaye = 3000m,
                ModePaiement = "Especes",
                DatePaiement = DateTime.Now,
                IdOperateur = IdBoss,
                NomOperateur = "Ibrahim TRAORE",
                RecuNumero = "REC-TEST-0001",
                MontantTotalCommande = 5000m,
                ResteAvantPaiement = 5000m
            };
            _context.Paiements.Add(paiement);

            // Rattacher la pièce à une commission non annulée
            var commission = new Commission
            {
                IdEmploye = IdCouturier,
                NomEmployeSnapshot = "Dramane KABORE",
                DateDebutPeriode = DateTime.Now.AddDays(-30),
                DateFinPeriode   = DateTime.Now,
                MontantCommission = 2000m,
                EstAnnulee = false,
                DateCalcul = DateTime.Now,
                IdOperateur = IdBoss,
                NomOperateur = "Ibrahim TRAORE"
            };
            _context.Commissions.Add(commission);
            _context.SaveChanges();

            piece.IdCommission = commission.IdCommission;
            _context.SaveChanges();

            return (commande, piece);
        }

        [Test]
        public void PAI_ANN01_Annuler_Paiement_Bloque_Si_Commission_Active()
        {
            var (commande, piece) = CreerCommandeAvecCommission();
            var paiement = _context.Paiements
                .First(p => p.IdCommande == commande.IdCommande);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _paiementService.Annuler(paiement.IdPaiement, "Test", IdBoss, "Ibrahim TRAORE"));

            Assert.That(ex.Message, Does.Contain("commission").IgnoreCase,
                "Le message doit mentionner la commission concernée.");
        }

        [Test]
        public void PAI_ANN02_Annuler_Paiement_Ok_Si_Commission_Annulee()
        {
            var (commande, piece) = CreerCommandeAvecCommission();
            var paiement = _context.Paiements
                .First(p => p.IdCommande == commande.IdCommande);

            // Annuler d'abord la commission
            var commission = _context.Commissions
                .First(c => c.IdCommission == piece.IdCommission);
            commission.EstAnnulee = true;
            commission.MotifAnnulation = "Test";
            _context.SaveChanges();

            // Libérer le lien de la pièce
            piece.IdCommission = null;
            _context.SaveChanges();

            // Maintenant l'annulation du paiement doit réussir
            Assert.DoesNotThrow(() =>
                _paiementService.Annuler(paiement.IdPaiement, "Test annulation", IdBoss, "Ibrahim TRAORE"));
        }

        [Test]
        public void PAI_ANN03_Annuler_Paiement_Ok_Si_Aucune_Piece_Commissionnee()
        {
            // Commande sans pièce commissionnée
            var commande = new Commande
            {
                IdClient = IdClient,
                IdOperateurCreation = IdBoss,
                NomOperateurCreation = "Ibrahim TRAORE",
                DateDebut = DateTime.Now.AddDays(-5),
                DateFin = DateTime.Now.AddDays(3),
                Statut = "A faire",
#pragma warning disable CS0618
                TypeVetement = "Robe",
                MontantTotal = 0
#pragma warning restore CS0618
            };
            var piece = new PieceCommande
            {
                TypeVetement = "Robe",
                MontantCouture = 3000m,
                Statut = "A faire",
                IdCouturier = IdCouturier
            };
            commande.Pieces = new List<PieceCommande> { piece };
            _context.Commandes.Add(commande);
            var paiement = new Paiement
            {
                IdCommande = commande.IdCommande,
                MontantPaye = 1000m,
                ModePaiement = "Especes",
                DatePaiement = DateTime.Now,
                IdOperateur = IdBoss,
                NomOperateur = "Ibrahim TRAORE",
                RecuNumero = "REC-TEST-0002",
                MontantTotalCommande = 3000m,
                ResteAvantPaiement = 3000m
            };
            commande.Paiements = new List<Paiement> { paiement };
            _context.SaveChanges();

            // Doit réussir car aucune pièce n'est commissionnée
            Assert.DoesNotThrow(() =>
                _paiementService.Annuler(paiement.IdPaiement, "Erreur de saisie", IdBoss, "Ibrahim TRAORE"));
        }

        // ─── Factory pour InMemory ────────────────────────────────────────────
        private class Phase2DbFactory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _opts;
            public Phase2DbFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
            public ApplicationDbContext CreateDbContext() => new(_opts);
        }
    }
}
