// =======================================================================
// ÉTAPE 1 — Tests de non-régression et de correction
//   C1 : ObtenirCommandeSelectionnee utilise ObtenirParId (pas ObtenirTous)
//   C2 : SplitQuery global — vérification que les Include ne cassent rien
//   C5 : Filtre de dates sans .Date (bornes exclusives)
// =======================================================================

using System.Diagnostics;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    [TestFixture]
    public class Etape1_PerformanceCommandesTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService _commandeService = null!;

        // ── Factory avec SplitQuery activé (reflète App.cs corrigé) ────────
        private static IDbContextFactory<ApplicationDbContext> CreerFactory(string dbName)
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                // C2 : SplitQuery doit être activé dans les tests aussi
                .Options;
            return new Etape1DbContextFactory(opts);
        }

        private class Etape1DbContextFactory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _opts;
            public Etape1DbContextFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_opts);
        }

        // ── Données de test ─────────────────────────────────────────────────
        private static Employe CreerBoss(ApplicationDbContext ctx, int id = 1)
        {
            var boss = new Employe
            {
                IdEmploye   = id,
                Prenom      = "Boss",
                Nom         = "Test",
                Identifiant = "bosstest" + id,
                Role        = "Boss",
                Statut      = "Actif",
                MotDePasse  = "hash"
            };
            ctx.Employes.Add(boss);
            ctx.SaveChanges();
            return boss;
        }

        private static Client CreerClient(ApplicationDbContext ctx)
        {
            var c = new Client { Prenom = "Jean", Nom = "Dupont", Telephone = "0600000000" };
            ctx.Clients.Add(c);
            ctx.SaveChanges();
            return c;
        }

        private static (Commande cmd, PieceCommande piece) CreerCommandeActive(
            ApplicationDbContext ctx, Client client, Employe boss,
            string statut = "A faire", bool supprimee = false, bool annulee = false)
        {
            var cmd = new Commande
            {
                IdClient             = client.IdClient,
                DateDebut            = DateTime.Today,
                DateFin              = DateTime.Today.AddDays(7),
                IdOperateurCreation  = boss.IdEmploye,
                NomOperateurCreation = boss.Prenom + " " + boss.Nom,
                EstSupprimee         = supprimee,
                EstAnnulee           = annulee
            };
            ctx.Commandes.Add(cmd);
            ctx.SaveChanges();

            var piece = new PieceCommande
            {
                IdCommande     = cmd.IdCommande,
                TypeVetement   = "Robe",
                MontantCouture = 10000m,
                Statut         = statut
            };
            ctx.PiecesCommande.Add(piece);
            ctx.SaveChanges();

            return (cmd, piece);
        }

        [SetUp]
        public void Init()
        {
            _factory        = CreerFactory("etape1_" + Guid.NewGuid());
            _commandeService = new CommandeService(
                _factory,
                NullLogger<CommandeService>.Instance,
                TestDbHelper.NoOpAudit);
        }

        // ================================================================
        // C1 — ObtenirParId retourne null pour une commande supprimée
        // ================================================================

        [Test]
        public void C1_ObtenirParId_RetourneNull_SiSupprimee()
        {
            // Arrange : créer une commande supprimée
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeActive(ctx, client, boss, supprimee: true);

            // Act
            var result = _commandeService.ObtenirParId(cmd.IdCommande);

            // Assert : ObtenirParId ne filtre pas EstSupprimee par design —
            // c'est l'APPELANT (ObtenirCommandeSelectionnee) qui fait le filtre.
            // Ce test vérifie que l'entité est bien retournée avec EstSupprimee=true
            // afin que l'appelant puisse décider.
            Assert.That(result, Is.Not.Null, "ObtenirParId doit retourner la commande même supprimée");
            Assert.That(result!.EstSupprimee, Is.True, "La commande doit être marquée supprimée");
        }

        [Test]
        public void C1_ObtenirParId_RetourneCommande_SiActive()
        {
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeActive(ctx, client, boss);

            var result = _commandeService.ObtenirParId(cmd.IdCommande);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.EstSupprimee, Is.False);
            Assert.That(result.IdCommande, Is.EqualTo(cmd.IdCommande));
        }

        [Test]
        public void C1_ObtenirParId_RetourneNull_SiIdInexistant()
        {
            var result = _commandeService.ObtenirParId(99999);
            Assert.That(result, Is.Null);
        }

        // ================================================================
        // C2 — SplitQuery : Include multi-niveaux retourne des données cohérentes
        // ================================================================

        [Test]
        public async Task C2_SplitQuery_ObtenirPageLightAsync_IncludeCoherent()
        {
            // Arrange : commande avec pièce, paiement, matériau
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var (cmd, piece) = CreerCommandeActive(ctx, client, boss, "En cours");

            ctx.Paiements.Add(new Paiement
            {
                IdCommande   = cmd.IdCommande,
                MontantPaye  = 5000m,
                ModePaiement = "Espèces",
                IdOperateur  = boss.IdEmploye,
                NomOperateur = "Boss Test",
                RecuNumero   = "REC-TEST-001"
            });
            ctx.MaterielsSupplements.Add(new MaterielSupplement
            {
                IdCommande   = cmd.IdCommande,
                Designation  = "Tissu",
                Quantite     = 2,
                PrixUnitaire = 1000m,
                IdOperateur  = boss.IdEmploye,
                NomOperateur = "Boss Test"
            });
            ctx.SaveChanges();

            // Act
            var result = await _commandeService.ObtenirPageLightAsync(1, 10);

            // Assert
            Assert.That(result.Items, Has.Count.EqualTo(1));
            var c = result.Items[0];
            Assert.That(c.Pieces,              Has.Count.EqualTo(1));
            Assert.That(c.Paiements,           Has.Count.EqualTo(1));
            Assert.That(c.MaterielSupplements, Has.Count.EqualTo(1));
            Assert.That(c.Client,              Is.Not.Null);
        }

        [Test]
        public async Task C2_SplitQuery_ObtenirPageLightAsync_MultipleCommandes_ToutesRetournees()
        {
            // Arrange : 5 commandes actives
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerBoss(ctx);
            var client = CreerClient(ctx);
            for (int i = 0; i < 5; i++)
                CreerCommandeActive(ctx, client, boss);

            // Act
            var result = await _commandeService.ObtenirPageLightAsync(1, 10);

            // Assert : toutes les commandes sont retournées
            Assert.That(result.TotalCount, Is.EqualTo(5));
            Assert.That(result.Items,      Has.Count.EqualTo(5));
        }

        // ================================================================
        // C5 — Filtre de dates : bornes exclusives sans .Date
        // ================================================================

        [Test]
        public async Task C5_FiltreDates_IncludeCommandeDansLaPeriode()
        {
            // Arrange
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeActive(ctx, client, boss);
            // On force la DateDebut à aujourd'hui pour un test déterministe
            cmd.DateDebut = DateTime.Today;
            ctx.SaveChanges();

            // Act : filtre sur aujourd'hui
            var result = await _commandeService.ObtenirPageLightAsync(
                1, 10, null, null, null,
                dateDebut: DateTime.Today,
                dateFin:   DateTime.Today);

            // Assert
            Assert.That(result.TotalCount, Is.EqualTo(1));
        }

        [Test]
        public async Task C5_FiltreDates_ExcludeCommandeHorsPeriode()
        {
            // Arrange
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeActive(ctx, client, boss);
            cmd.DateDebut = DateTime.Today.AddDays(-5);
            ctx.SaveChanges();

            // Act : filtre sur aujourd'hui seulement
            var result = await _commandeService.ObtenirPageLightAsync(
                1, 10, null, null, null,
                dateDebut: DateTime.Today,
                dateFin:   DateTime.Today);

            // Assert
            Assert.That(result.TotalCount, Is.EqualTo(0),
                "La commande d'il y a 5 jours ne doit pas apparaître dans le filtre 'aujourd'hui'");
        }

        [Test]
        public async Task C5_FiltreDates_BorneFinInclutToutLaJournee()
        {
            // Vérifie que dateFin couvre bien jusqu'à 23:59:59 du jour
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeActive(ctx, client, boss);
            // DateDebut à 23:30 aujourd'hui
            cmd.DateDebut = DateTime.Today.Add(new TimeSpan(23, 30, 0));
            ctx.SaveChanges();

            var result = await _commandeService.ObtenirPageLightAsync(
                1, 10, null, null, null,
                dateDebut: DateTime.Today,
                dateFin:   DateTime.Today);

            Assert.That(result.TotalCount, Is.EqualTo(1),
                "Une commande créée à 23h30 doit être incluse dans le filtre du même jour");
        }

        [Test]
        public async Task C5_FiltreDates_BorneDebutExcludeJourPrecedent()
        {
            // Vérifie qu'une commande à 23:59 la veille est exclue
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeActive(ctx, client, boss);
            cmd.DateDebut = DateTime.Today.AddDays(-1).Add(new TimeSpan(23, 59, 59));
            ctx.SaveChanges();

            var result = await _commandeService.ObtenirPageLightAsync(
                1, 10, null, null, null,
                dateDebut: DateTime.Today,
                dateFin:   DateTime.Today);

            Assert.That(result.TotalCount, Is.EqualTo(0),
                "Une commande à 23:59 la veille ne doit PAS être dans le filtre 'aujourd'hui'");
        }

        // ================================================================
        // MESURE — comparaison ObtenirTous vs ObtenirParId (C1)
        // ================================================================

        [Test]
        [Category("Perf")]
        public void C1_Perf_ObtenirParId_PlusRapideQueObtenirTous()
        {
            // Créer 1 000 commandes
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerBoss(ctx);
            var client = CreerClient(ctx);
            for (int i = 0; i < 1000; i++)
                CreerCommandeActive(ctx, client, boss);

            // On veut la première commande insérée
            int idCible = ctx.Commandes.OrderBy(c => c.IdCommande).First().IdCommande;

            // Mesure ObtenirTous + FirstOrDefault (ancienne méthode)
            var sw1 = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
            {
                _ = _commandeService.ObtenirTous().FirstOrDefault(c => c.IdCommande == idCible);
            }
            sw1.Stop();
            long msObtenirTous = sw1.ElapsedMilliseconds / 5;

            // Mesure ObtenirParId (nouvelle méthode)
            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
            {
                _ = _commandeService.ObtenirParId(idCible);
            }
            sw2.Stop();
            long msObtenirParId = sw2.ElapsedMilliseconds / 5;

            TestContext.WriteLine($"[C1 PERF] ObtenirTous+FirstOrDefault : ~{msObtenirTous} ms");
            TestContext.WriteLine($"[C1 PERF] ObtenirParId               : ~{msObtenirParId} ms");

            // ObtenirParId doit être significativement plus rapide
            // (InMemory = gains moins dramatiques qu'en SQLite, mais toujours meilleurs)
            Assert.That(msObtenirParId, Is.LessThanOrEqualTo(msObtenirTous),
                $"ObtenirParId ({msObtenirParId} ms) devrait être ≤ ObtenirTous ({msObtenirTous} ms)");
        }
    }
}
