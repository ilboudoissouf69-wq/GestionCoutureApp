// ============================================================
// ÉTAPE 0 — Tests de charge avec BUDGET (base SQLite fichier, ≥ 200 000 pièces)
//
// ⚠️ Ces tests sont dans la catégorie "Perf" : JAMAIS lancés par défaut.
//   Pour les exécuter : dotnet test --filter TestCategory=Perf
//
// Budget cible (critères d'acceptation) :
//   - Chaque méthode de service d'écran < 300 ms
//   - Aucune ne retourne plus de 50 lignes de détail (hors GROUP BY agrégés)
//
// Ces tests mesurent l'état ACTUEL. Les méthodes non encore optimisées
// échouent délibérément : c'est le « filet de sécurité » qui montre ce qu'il
// reste à corriger. Au fur et à mesure des étapes 1→7, les méthodes
// optimisées feront passer leurs tests.
// ============================================================

using System.Diagnostics;
using System.IO;
using GestionCoutureApp.Data;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace GestionCoutureApp.Tests
{
    [TestFixture]
    [Category("Perf")]
    public class Perf_BudgetTests
    {
        // ── Volumes : ≥ 200 000 pièces (critère d'acceptation) ────────────
        private const int NB_CLIENTS   = 130_000;
        private const int NB_COMMANDES = 130_000;
        private const int NB_PIECES    = 200_000; // ≥ 200 000
        private const int NB_PAIEMENTS = 100_000;
        private const int NB_RETOURS   = 10_000;
        private const int NB_AUDIT     = 50_000;

        // ── Budgets ───────────────────────────────────────────────────────
        private const long BUDGET_MS   = 300;
        private const int BUDGET_ITEMS = 50;

        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private AlerteService _alerteService = null!;
        private RetardAlerteService _retardAlerteService = null!;
        private RetourService _retourService = null!;
        private CommandeService _commandeService = null!;
        private ClientService _clientService = null!;
        private PaiementService _paiementService = null!;

        [OneTimeSetUp]
        public void Setup()
        {
            // Base SQLite sur disque (les volumes ne tiennent pas en mémoire)
            string dbPath = Path.Combine(Path.GetTempPath(), $"perf_budget_{Guid.NewGuid():N}.db");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            _factory = new PerfDbContextFactory(opts);

            using var ctx = _factory.CreateDbContext();
            ctx.Database.EnsureCreated();

            var sw = Stopwatch.StartNew();
            ctx.ChangeTracker.AutoDetectChangesEnabled = false;
            StressDataHelper.Generer(ctx, NB_CLIENTS, NB_COMMANDES, NB_PIECES,
                NB_PAIEMENTS, NB_RETOURS, NB_AUDIT);
            sw.Stop();
            TestContext.WriteLine($"[PERF] Génération : {sw.ElapsedMilliseconds} ms " +
                $"({NB_CLIENTS} clients, {NB_PIECES} pièces)");

            // ANALYZE pour que le query planner ait des statistiques
            ctx.Database.ExecuteSqlRaw("ANALYZE;");

            var mockParam = new MockParametresService();
            _alerteService = new AlerteService(_factory, mockParam,
                NullLogger<AlerteService>.Instance);
            _retardAlerteService = new RetardAlerteService(_factory, mockParam,
                TestDbHelper.NoOpAudit);
            _retourService = new RetourService(_factory, TestDbHelper.NoOpAudit);
            _commandeService = new CommandeService(_factory,
                NullLogger<CommandeService>.Instance, TestDbHelper.NoOpAudit);
            _clientService = new ClientService(_factory, NullLogger<ClientService>.Instance);
            _paiementService = new PaiementService(_factory,
                NullLogger<PaiementService>.Instance);
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            // Nettoyer le fichier de base temporaire
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }

        // ================================================================
        // MÉTHODES PAGINÉES (devraient déjà respecter le budget)
        // ================================================================

        [Test]
        public void B01_RetourService_ObtenirPage_Budget()
        {
            var sw = Stopwatch.StartNew();
            var result = _retourService.ObtenirPageAsync(1, 20).GetAwaiter().GetResult();
            sw.Stop();

            TestContext.WriteLine($"[PERF] RetourService.ObtenirPageAsync : {sw.ElapsedMilliseconds} ms, {result.Items.Count} items");
            Assert.Multiple(() =>
            {
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(BUDGET_MS),
                    $"RetourService.ObtenirPageAsync : {sw.ElapsedMilliseconds} ms > {BUDGET_MS} ms");
                Assert.That(result.Items.Count, Is.LessThanOrEqualTo(BUDGET_ITEMS),
                    $"RetourService.ObtenirPageAsync : {result.Items.Count} items > {BUDGET_ITEMS}");
            });
        }

        [Test]
        public void B02_CommandeService_ObtenirPageLight_Budget()
        {
            var sw = Stopwatch.StartNew();
            var result = _commandeService.ObtenirPageLightAsync(1, 20).GetAwaiter().GetResult();
            sw.Stop();

            TestContext.WriteLine($"[PERF] CommandeService.ObtenirPageLightAsync : {sw.ElapsedMilliseconds} ms, {result.Items.Count} items");
            Assert.Multiple(() =>
            {
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(BUDGET_MS),
                    $"CommandeService.ObtenirPageLightAsync : {sw.ElapsedMilliseconds} ms > {BUDGET_MS} ms");
                Assert.That(result.Items.Count, Is.LessThanOrEqualTo(BUDGET_ITEMS),
                    $"CommandeService.ObtenirPageLightAsync : {result.Items.Count} items > {BUDGET_ITEMS}");
            });
        }

        [Test]
        public void B03_ClientService_ObtenirPage_Budget()
        {
            var sw = Stopwatch.StartNew();
            var result = _clientService.ObtenirPageAsync(1, 50).GetAwaiter().GetResult();
            sw.Stop();

            TestContext.WriteLine($"[PERF] ClientService.ObtenirPageAsync : {sw.ElapsedMilliseconds} ms, {result.Items.Count} items");
            Assert.Multiple(() =>
            {
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(BUDGET_MS),
                    $"ClientService.ObtenirPageAsync : {sw.ElapsedMilliseconds} ms > {BUDGET_MS} ms");
                Assert.That(result.Items.Count, Is.LessThanOrEqualTo(BUDGET_ITEMS),
                    $"ClientService.ObtenirPageAsync : {result.Items.Count} items > {BUDGET_ITEMS}");
            });
        }

        [Test]
        public void B04_PaiementService_ObtenirPageLight_Budget()
        {
            var sw = Stopwatch.StartNew();
            var result = _paiementService.ObtenirPageLightAsync(1, 15).GetAwaiter().GetResult();
            sw.Stop();

            TestContext.WriteLine($"[PERF] PaiementService.ObtenirPageLightAsync : {sw.ElapsedMilliseconds} ms, {result.Items.Count} items");
            Assert.Multiple(() =>
            {
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(BUDGET_MS),
                    $"PaiementService.ObtenirPageLightAsync : {sw.ElapsedMilliseconds} ms > {BUDGET_MS} ms");
                Assert.That(result.Items.Count, Is.LessThanOrEqualTo(BUDGET_ITEMS),
                    $"PaiementService.ObtenirPageLightAsync : {result.Items.Count} items > {BUDGET_ITEMS}");
            });
        }

        // ================================================================
        // MÉTHODES NON BORNÉES (doivent être optimisées — actuellement hors budget)
        // Ces tests mesurent l'état actuel et échouent jusqu'à l'optimisation.
        // ================================================================

        [Test]
        public void B05_AlerteService_ObtenirToutesAlertes_Budget()
        {
            var sw = Stopwatch.StartNew();
            var dto = _alerteService.ObtenirToutesAlertesAsync().GetAwaiter().GetResult();
            sw.Stop();

            int total = dto.Production.Count + dto.Retrait.Count + dto.Retards.Count + dto.Retours.Count;
            TestContext.WriteLine($"[PERF] AlerteService.ObtenirToutesAlertesAsync : {sw.ElapsedMilliseconds} ms, " +
                $"Prod={dto.Production.Count} Retrait={dto.Retrait.Count} Retards={dto.Retards.Count} Retours={dto.Retours.Count}");

            // Budget : chaque section ≤ 50, total < 300 ms
            // ⚠️ Actuellement hors budget (charge 150k+ pièces) — à optimiser à l'étape 2
            Assert.Multiple(() =>
            {
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(BUDGET_MS),
                    $"ObtenirToutesAlertesAsync : {sw.ElapsedMilliseconds} ms > {BUDGET_MS} ms");
                Assert.That(dto.Retards.Count, Is.LessThanOrEqualTo(BUDGET_ITEMS),
                    $"Section Retards : {dto.Retards.Count} > {BUDGET_ITEMS}");
                Assert.That(dto.Retrait.Count, Is.LessThanOrEqualTo(BUDGET_ITEMS),
                    $"Section Retrait : {dto.Retrait.Count} > {BUDGET_ITEMS}");
            });
        }

        [Test]
        public void B06_RetardAlerteService_ObtenirAlertesRisqueRetard_Budget()
        {
            var sw = Stopwatch.StartNew();
            var alertes = _retardAlerteService.ObtenirAlertesRisqueRetardAsync().GetAwaiter().GetResult();
            sw.Stop();

            TestContext.WriteLine($"[PERF] RetardAlerteService.ObtenirAlertesRisqueRetardAsync : {sw.ElapsedMilliseconds} ms, {alertes.Count} alertes");

            // Budget : ≤ 50 alertes, < 300 ms
            // ⚠️ Actuellement hors budget (~89k alertes) — à optimiser à l'étape 2
            Assert.Multiple(() =>
            {
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(BUDGET_MS),
                    $"ObtenirAlertesRisqueRetardAsync : {sw.ElapsedMilliseconds} ms > {BUDGET_MS} ms");
                Assert.That(alertes.Count, Is.LessThanOrEqualTo(BUDGET_ITEMS),
                    $"Alertes risque retard : {alertes.Count} > {BUDGET_ITEMS}");
            });
        }

        [Test]
        public void B07_RetardAlerteService_ObtenirStatsDashboard_Budget()
        {
            var sw = Stopwatch.StartNew();
            var stats = _retardAlerteService.ObtenirStatsDashboardAsync().GetAwaiter().GetResult();
            sw.Stop();

            TestContext.WriteLine($"[PERF] RetardAlerteService.ObtenirStatsDashboardAsync : {sw.ElapsedMilliseconds} ms, " +
                $"retards={stats.NbCommandesEnRetard} reports={stats.NbReportsMois} clients={stats.NbClientsPrevenusMois}");

            // Budget : < 300 ms (un compteur ne doit pas charger 89k lignes)
            // ⚠️ Actuellement hors budget — à optimiser à l'étape 3
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(BUDGET_MS),
                $"ObtenirStatsDashboardAsync : {sw.ElapsedMilliseconds} ms > {BUDGET_MS} ms");
        }

        [Test]
        public void B08_AlerteService_ObtenirRetards_Budget()
        {
            var sw = Stopwatch.StartNew();
            var retards = _alerteService.ObtenirRetards().GetAwaiter().GetResult();
            sw.Stop();

            TestContext.WriteLine($"[PERF] AlerteService.ObtenirRetards : {sw.ElapsedMilliseconds} ms, {retards.Count} retards");

            // Budget : ≤ 50 retards, < 300 ms
            // ⚠️ Actuellement hors budget — à optimiser à l'étape 2
            Assert.Multiple(() =>
            {
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(BUDGET_MS),
                    $"ObtenirRetards : {sw.ElapsedMilliseconds} ms > {BUDGET_MS} ms");
                Assert.That(retards.Count, Is.LessThanOrEqualTo(BUDGET_ITEMS),
                    $"ObtenirRetards : {retards.Count} > {BUDGET_ITEMS}");
            });
        }

        [Test]
        public void B09_AlerteService_ObtenirRendezVousSemaine_Budget()
        {
            var sw = Stopwatch.StartNew();
            var semaine = _alerteService.ObtenirRendezVousSemaine().GetAwaiter().GetResult();
            sw.Stop();

            TestContext.WriteLine($"[PERF] AlerteService.ObtenirRendezVousSemaine : {sw.ElapsedMilliseconds} ms, {semaine.Count} items");

            // Budget : ≤ 50, < 300 ms
            // ⚠️ Actuellement potentiellement hors budget — à optimiser à l'étape 2
            Assert.Multiple(() =>
            {
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(BUDGET_MS),
                    $"ObtenirRendezVousSemaine : {sw.ElapsedMilliseconds} ms > {BUDGET_MS} ms");
                Assert.That(semaine.Count, Is.LessThanOrEqualTo(BUDGET_ITEMS),
                    $"ObtenirRendezVousSemaine : {semaine.Count} > {BUDGET_ITEMS}");
            });
        }

        // ================================================================
        // FACTORY LOCALE
        // ================================================================
        internal class PerfDbContextFactory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _opts;
            public PerfDbContextFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_opts);
        }
    }
}
