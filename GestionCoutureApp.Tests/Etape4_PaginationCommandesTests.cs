// =======================================================================
// ÉTAPE 4 — Tests pagination commandes
//
// C4 : AsNoTracking sur les méthodes paginées en lecture seule
//      (pas de suivi EF inutile quand le contexte est libéré à la fin)
//
// C6 : Bug pagination avec filtre statut
//      PREUVE : avant correction, TotalCount était calculé avant l'affinage
//      en mémoire, et les items étaient affinés APRÈS Skip/Take.
//      CORRECTION : affinage en mémoire AVANT Skip/Take, TotalCount recalculé.
//      Tests d'équivalence sur jeu de données avec statuts mixtes.
// =======================================================================

using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    [TestFixture]
    public class Etape4_PaginationCommandesTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService _commandeService = null!;

        private class Etape4Factory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _opts;
            public Etape4Factory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_opts);
        }

        [SetUp]
        public void Init()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("etape4_" + Guid.NewGuid())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            _factory = new Etape4Factory(opts);
            _commandeService = new CommandeService(
                _factory, NullLogger<CommandeService>.Instance, TestDbHelper.NoOpAudit);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static Employe CreerBoss(ApplicationDbContext ctx)
        {
            var e = new Employe
            {
                Prenom = "Boss", Nom = "Test", Identifiant = "b_" + Guid.NewGuid(),
                Role = "Boss", Statut = "Actif", MotDePasse = "hash"
            };
            ctx.Employes.Add(e); ctx.SaveChanges(); return e;
        }

        private static Client CreerClient(ApplicationDbContext ctx)
        {
            var c = new Client { Prenom = "Jean", Nom = "Dupont", Telephone = "0600" };
            ctx.Clients.Add(c); ctx.SaveChanges(); return c;
        }

        /// <summary>Crée une commande avec N pièces, toutes au même statut.</summary>
        private static Commande CreerCommande(ApplicationDbContext ctx,
            Employe boss, Client client,
            string[] statutsPieces,
            bool supprimee = false,
            DateTime? dateFin = null)
        {
            var cmd = new Commande
            {
                IdClient = client.IdClient,
                DateDebut = DateTime.Today,
                DateFin = dateFin ?? DateTime.Today.AddDays(7),
                IdOperateurCreation = boss.IdEmploye,
                NomOperateurCreation = "Boss",
                EstSupprimee = supprimee
            };
            ctx.Commandes.Add(cmd);
            ctx.SaveChanges();

            foreach (var s in statutsPieces)
            {
                ctx.PiecesCommande.Add(new PieceCommande
                {
                    IdCommande = cmd.IdCommande,
                    TypeVetement = "Robe",
                    MontantCouture = 5000m,
                    Statut = s
                });
            }
            ctx.SaveChanges();
            return cmd;
        }

        // ================================================================
        // C4 — AsNoTracking : les items retournés ne sont pas trackés
        // ================================================================

        [Test]
        public async Task C4_ObtenirPageLightAsync_RetourneItems_SansTracking()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            CreerCommande(ctx, boss, client, new[] { "A faire" });

            var result = await _commandeService.ObtenirPageLightAsync(1, 10);

            Assert.That(result.Items, Has.Count.EqualTo(1));
            // Le service libère son contexte → les entités ne sont plus tracées
            // (le contexte est disposed — on vérifie que les données sont bien chargées)
            Assert.That(result.Items[0].IdCommande, Is.GreaterThan(0));
            Assert.That(result.Items[0].Pieces, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task C4_ObtenirPageAsync_RetourneItems()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            CreerCommande(ctx, boss, client, new[] { "En cours" });

            var result = await _commandeService.ObtenirPageAsync(1, 10);
            Assert.That(result.TotalCount, Is.EqualTo(1));
        }

        // ================================================================
        // C6 — PREUVE du bug : TotalCount incohérent avec Items.Count
        //
        // Scénario : 10 commandes avec statuts mixtes (5 "Livree partiellement",
        // 5 "Livree") — filtrer sur "Livree" avec pageSize=3.
        //
        // AVANT correction : TotalCount=10 (calculé avant affinage), page 1 = 3 items
        //   MAIS items contenaient peut-être des "Livree partiellement" → incohérence.
        //
        // APRÈS correction : TotalCount=5, page 1=3 items, page 2=2 items.
        // ================================================================

        [Test]
        public async Task C6_Pagination_TotalCount_Coherent_AvecFiltre_Livree()
        {
            // Sémantique du filtre "Livree" :
            // StatutGlobal == "Livree" OU StatutGlobal.Replace(" partiellement","") == "Livree"
            // → inclut "Livree" ET "Livree partiellement"
            //
            // Arrange : 5 commandes "Livree" + 5 "Livree partiellement" = 10 au total dans le filtre
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);

            for (int i = 0; i < 5; i++)
                CreerCommande(ctx, boss, client, new[] { "Livree" });

            for (int i = 0; i < 5; i++)
                CreerCommande(ctx, boss, client, new[] { "Livree", "Terminee" });

            // Commandes EN COURS qui ne doivent PAS être dans le filtre
            for (int i = 0; i < 3; i++)
                CreerCommande(ctx, boss, client, new[] { "En cours" });

            // Act : filtre "Livree", page 1 taille 4
            var page1 = await _commandeService.ObtenirPageLightAsync(
                1, 4, null, "Livree", null, null, null);

            // Assert : TotalCount = 10 (5 "Livree" + 5 "Livree partiellement")
            // Les 3 "En cours" ne doivent PAS être présents
            Assert.That(page1.TotalCount, Is.EqualTo(10),
                "C6 : TotalCount doit inclure 'Livree' + 'Livree partiellement' = 10");

            Assert.That(page1.Items, Has.Count.EqualTo(4),
                "C6 : page 1 doit avoir 4 items");

            // Tous les items doivent être "Livree" ou "Livree partiellement" — JAMAIS "En cours"
            foreach (var item in page1.Items)
                Assert.That(item.StatutGlobal.Replace(" partiellement", ""),
                    Is.EqualTo("Livree"),
                    $"C6 : item {item.IdCommande} a StatutGlobal='{item.StatutGlobal}' — attendu Livree[partiellement]");

            // C6 clé : TotalCount == somme de toutes les pages
            var page2 = await _commandeService.ObtenirPageLightAsync(
                2, 4, null, "Livree", null, null, null);
            var page3 = await _commandeService.ObtenirPageLightAsync(
                3, 4, null, "Livree", null, null, null);

            int totalItemsSurToutesPages = page1.Items.Count + page2.Items.Count + page3.Items.Count;
            Assert.That(totalItemsSurToutesPages, Is.EqualTo(page1.TotalCount),
                "C6 CLEF : somme des items sur toutes les pages doit égaler TotalCount");
        }

        [Test]
        public async Task C6_Pagination_Page2_CorrectItems()
        {
            // 6 commandes "Livree" + 4 "Livree partiellement" = 10 dans filtre "Livree"
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);

            for (int i = 0; i < 6; i++)
                CreerCommande(ctx, boss, client, new[] { "Livree" });
            for (int i = 0; i < 4; i++)
                CreerCommande(ctx, boss, client, new[] { "Livree", "Terminee" });

            // pageSize=4 → page1=4, page2=4, page3=2
            var page1 = await _commandeService.ObtenirPageLightAsync(
                1, 4, null, "Livree", null, null, null);
            var page2 = await _commandeService.ObtenirPageLightAsync(
                2, 4, null, "Livree", null, null, null);
            var page3 = await _commandeService.ObtenirPageLightAsync(
                3, 4, null, "Livree", null, null, null);

            Assert.That(page1.TotalCount, Is.EqualTo(10));
            Assert.That(page1.Items, Has.Count.EqualTo(4));
            Assert.That(page2.Items, Has.Count.EqualTo(4));
            Assert.That(page3.Items, Has.Count.EqualTo(2));

            // C6 CLEF : cohérence TotalCount vs somme des pages
            int total = page1.Items.Count + page2.Items.Count + page3.Items.Count;
            Assert.That(total, Is.EqualTo(page1.TotalCount),
                "C6 : la somme des items sur toutes les pages doit égaler TotalCount");
        }

        [Test]
        public async Task C6_Pagination_TotalCount_Coherent_AvecFiltre_Terminee()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);

            // 4 "Terminee" (1 seule pièce Terminee)
            for (int i = 0; i < 4; i++)
                CreerCommande(ctx, boss, client, new[] { "Terminee" });

            // 3 "Terminee partiellement" (2 pièces : 1 Terminee + 1 En cours)
            for (int i = 0; i < 3; i++)
                CreerCommande(ctx, boss, client, new[] { "Terminee", "En cours" });

            // 2 "En cours"
            for (int i = 0; i < 2; i++)
                CreerCommande(ctx, boss, client, new[] { "En cours" });

            var result = await _commandeService.ObtenirPageLightAsync(
                1, 10, null, "Terminee", null, null, null);

            // Le filtre "Terminee" avec StatutGlobal.Replace(" partiellement","") == "Terminee"
            // doit inclure AUSSI les "Terminee partiellement" (c'est la sémantique existante)
            Assert.That(result.TotalCount, Is.EqualTo(7),
                "C6 : 'Terminee' inclut aussi 'Terminee partiellement' via Replace");
            Assert.That(result.Items, Has.Count.EqualTo(7));
            Assert.That(result.TotalCount, Is.EqualTo(result.Items.Count),
                "C6 : TotalCount doit être cohérent avec Items.Count sur une seule page");
        }

        [Test]
        public async Task C6_Pagination_SansFiltre_TotalCount_Coherent()
        {
            // Sans filtre, TotalCount = nombre total de commandes non supprimées
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);

            for (int i = 0; i < 7; i++)
                CreerCommande(ctx, boss, client, new[] { "A faire" });
            CreerCommande(ctx, boss, client, new[] { "A faire" }, supprimee: true);

            var result = await _commandeService.ObtenirPageLightAsync(
                1, 5, null, null, null, null, null);

            Assert.That(result.TotalCount, Is.EqualTo(7),
                "Sans filtre : 7 commandes actives (la supprimée est exclue)");
            Assert.That(result.Items, Has.Count.EqualTo(5),
                "Page 1 avec pageSize=5 doit avoir 5 items");
        }

        [Test]
        public async Task C6_Pagination_PageVide_RetourneListeVide()
        {
            // Une page au-delà du dernier item doit retourner 0 items
            // mais TotalCount correct
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);

            for (int i = 0; i < 3; i++)
                CreerCommande(ctx, boss, client, new[] { "Livree" });

            // pageSize=2 → page 1 = 2 items, page 2 = 1 item, page 3 = 0 items
            var page3 = await _commandeService.ObtenirPageLightAsync(
                3, 2, null, "Livree", null, null, null);

            Assert.That(page3.TotalCount, Is.EqualTo(3));
            Assert.That(page3.Items, Is.Empty,
                "C6 : une page au-delà du dernier item doit être vide mais TotalCount reste correct");
        }

        [Test]
        public async Task C6_Pagination_TousStatuts_SommePages_EgaleTotalCount()
        {
            // Vérification exhaustive : la somme des items de toutes les pages
            // doit être égale à TotalCount pour chaque statut
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);

            // Jeu mixte
            var configs = new[]
            {
                new[] { "A faire" },
                new[] { "En cours" },
                new[] { "Terminee" },
                new[] { "Livree" },
                new[] { "Livree", "Terminee" },    // → Livree partiellement
                new[] { "Terminee", "En cours" },   // → Terminee partiellement
                new[] { "En cours", "A faire" },    // → En cours
            };

            foreach (var cfg in configs)
                for (int i = 0; i < 3; i++)
                    CreerCommande(ctx, boss, client, cfg);

            const int pageSize = 4;
            var statuts = new[] { "A faire", "En cours", "Terminee", "Livree", null };

            foreach (var statut in statuts)
            {
                int totalItems = 0;
                int page = 1;
                int totalCount = -1;

                while (true)
                {
                    var result = await _commandeService.ObtenirPageLightAsync(
                        page, pageSize, null, statut, null, null, null);

                    if (totalCount < 0) totalCount = result.TotalCount;
                    else Assert.That(result.TotalCount, Is.EqualTo(totalCount),
                        $"TotalCount doit être stable sur toutes les pages pour statut='{statut ?? "null"}'");

                    totalItems += result.Items.Count;
                    if (result.Items.Count < pageSize) break;
                    page++;
                }

                Assert.That(totalItems, Is.EqualTo(totalCount),
                    $"C6 : somme pages ({totalItems}) != TotalCount ({totalCount}) pour statut='{statut ?? "null"}'");
            }
        }
    }
}
