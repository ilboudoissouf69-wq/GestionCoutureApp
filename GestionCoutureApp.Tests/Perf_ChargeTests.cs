// ============================================================
// ÉTAPE 9 — Tests de charge [Category("Perf")]
//
// ⚠️ Ces tests ne sont JAMAIS lancés par défaut (filtre Category).
// Pour les exécuter manuellement :
//   dotnet test --filter TestCategory=Perf
//
// Volumes :
//   5 000 clients
//  20 000 commandes
//  40 000 pièces (2 par commande en moyenne)
//  30 000 paiements
// 100 000 lignes d'audit
//
// Pour chaque vue / service, on mesure le temps d'exécution et on
// produit un tableau classé du plus lent au plus rapide.
// Seuil d'alerte : 2 000 ms (réglable via PERF_SEUIL_MS).
// ============================================================

using System.Diagnostics;
using System.IO;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    [TestFixture]
    [Category("Perf")]
    public class Perf_ChargeTests
    {
        // ── Configuration ────────────────────────────────────────────────
        private const int NB_CLIENTS    =  5_000;
        private const int NB_COMMANDES  = 20_000;
        private const int NB_PIECES     = 40_000;  // 2 par commande
        private const int NB_PAIEMENTS  = 30_000;
        private const int NB_AUDIT      = 100_000;
        private const long SEUIL_MS     =  2_000;  // alerte si > 2 s

        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private TresorerieService  _tresorerieService  = null!;
        private CommissionService  _commissionService  = null!;
        private AlerteService      _alerteService      = null!;
        private RetourService      _retourService      = null!;
        private AuditService       _auditService       = null!;
        private PaiementService    _paiementService    = null!;
        private CommandeService    _commandeService    = null!;

        // Résultats cumulés pour le tableau final
        private readonly List<(string Label, long Ms)> _mesures = new();

        // ── Données de références ────────────────────────────────────────
        private readonly Random _rng = new(42);
        private static readonly string[] Statuts = { "A faire", "En cours", "Terminee", "Livree" };
        private static readonly string[] Types   = { "Pantalon", "Chemise", "Robe", "Boubou", "Veste" };
        private static readonly string[] Modes   = { "Especes", "Mobile Money", "Virement" };

        // ── Dates de période pour les mesures ────────────────────────────
        private static readonly DateTime Debut = DateTime.Today.AddYears(-2);
        private static readonly DateTime Fin   = DateTime.Today;

        // ================================================================
        // SETUP — génération du jeu de données massif (SQLite sur disque)
        // ================================================================
        [OneTimeSetUp]
        public void SetupMassif()
        {
            // Base SQLite sur disque (InMemory ne tient pas 100k lignes
            // dans les contraintes de RAM acceptables pour le CI).
            var dbPath = Path.Combine(Path.GetTempPath(), $"perf_test_{Guid.NewGuid():N}.db");
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _factory = new PerfDbContextFactory(opts);

            using var ctx = _factory.CreateDbContext();
            ctx.Database.EnsureCreated();

            var swSeed = Stopwatch.StartNew();
            GenererDonnees(ctx);
            swSeed.Stop();
            TestContext.WriteLine($"[PERF] Génération données : {swSeed.ElapsedMilliseconds} ms");

            var log = NullLoggerFactory.Instance;
            var mockParam = new MockParametresService();

            _tresorerieService = new TresorerieService(_factory, new Microsoft.Extensions.Logging.Logger<TresorerieService>(log));
            _commissionService = new CommissionService(_factory, new Microsoft.Extensions.Logging.Logger<CommissionService>(log), mockParam);
            _auditService      = new AuditService(_factory, new PerfWhatsAppStub(), new MockParametresService(), NullLogger<AuditService>.Instance);
            _alerteService     = new AlerteService(_factory, mockParam, new Microsoft.Extensions.Logging.Logger<AlerteService>(log));
            _retourService     = new RetourService(_factory, _auditService);
            _paiementService   = new PaiementService(_factory, new Microsoft.Extensions.Logging.Logger<PaiementService>(log));
            _commandeService   = new CommandeService(_factory, new Microsoft.Extensions.Logging.Logger<CommandeService>(log));
        }

        [OneTimeTearDown]
        public void AfficherTableau()
        {
            // Tableau final trié du plus lent au plus rapide
            var sorted = _mesures.OrderByDescending(m => m.Ms).ToList();

            TestContext.WriteLine("");
            TestContext.WriteLine("╔══════════════════════════════════════════════════════════╗");
            TestContext.WriteLine("║         TABLEAU DE PERFORMANCE — ÉTAPE 9                ║");
            TestContext.WriteLine("╠═══════════════════════════════════════════╦═════════════╣");
            TestContext.WriteLine("║ Opération                                 ║   Durée (ms)║");
            TestContext.WriteLine("╠═══════════════════════════════════════════╬═════════════╣");
            foreach (var (label, ms) in sorted)
            {
                string flag = ms > SEUIL_MS ? " ⚠️" : "   ";
                TestContext.WriteLine($"║ {label,-41} ║ {ms,10}{flag} ║");
            }
            TestContext.WriteLine("╚═══════════════════════════════════════════╩═════════════╝");
            TestContext.WriteLine($"Seuil d'alerte : {SEUIL_MS} ms  |  Volumes : {NB_CLIENTS} clients / {NB_COMMANDES} commandes / {NB_PIECES} pièces / {NB_PAIEMENTS} paiements / {NB_AUDIT} lignes audit");

            // Assert global : toutes les opérations < 10 s
            // (un test de charge ne doit pas bloquer le CI indéfiniment)
            foreach (var (label, ms) in sorted)
                Assert.That(ms, Is.LessThan(10_000),
                    $"[PERF] '{label}' dépasse 10 s ({ms} ms) — investigation requise.");
        }

        // ================================================================
        // MESURES INDIVIDUELLES
        // ================================================================

        [Test, Order(1)]
        public void P01_TresorerieService_CalculerChiffreAffaires()
        {
            long ms = Mesurer(() => _tresorerieService.CalculerChiffreAffaires(Debut, Fin));
            _mesures.Add(("TresorerieService.CalculerChiffreAffaires", ms));
        }

        [Test, Order(2)]
        public void P02_TresorerieService_CalculerTotalDepenses()
        {
            long ms = Mesurer(() => _tresorerieService.CalculerTotalDepenses(Debut, Fin));
            _mesures.Add(("TresorerieService.CalculerTotalDepenses", ms));
        }

        [Test, Order(3)]
        public void P03_TresorerieService_CalculerTotalCommissions()
        {
            long ms = Mesurer(() => _tresorerieService.CalculerTotalCommissions(Debut, Fin));
            _mesures.Add(("TresorerieService.CalculerTotalCommissions", ms));
        }

        [Test, Order(4)]
        public void P04_TresorerieService_CalculerBilan()
        {
            long ms = Mesurer(() => _tresorerieService.CalculerBilan(Debut, Fin));
            _mesures.Add(("TresorerieService.CalculerBilan", ms));
        }

        [Test, Order(5)]
        public void P05_CommissionService_CalculerApercu()
        {
            long ms = Mesurer(() =>
                _commissionService.CalculerApercu(
                    Debut, Fin, 10m, false, null));
            _mesures.Add(("CommissionService.CalculerApercu", ms));
        }

        [Test, Order(6)]
        public void P06_CommissionService_ObtenirHistorique()
        {
            long ms = Mesurer(() => _commissionService.ObtenirHistorique());
            _mesures.Add(("CommissionService.ObtenirHistorique", ms));
        }

        [Test, Order(7)]
        public async Task P07_AlerteService_ObtenirAlertesActuelles()
        {
            long ms = await MesurerAsync(async () =>
                await _alerteService.ObtenirAlertesActuelles());
            _mesures.Add(("AlerteService.ObtenirAlertesActuelles", ms));
        }

        [Test, Order(8)]
        public async Task P08_AlerteService_ObtenirRetards()
        {
            long ms = await MesurerAsync(async () =>
                await _alerteService.ObtenirRetards());
            _mesures.Add(("AlerteService.ObtenirRetards", ms));
        }

        [Test, Order(9)]
        public async Task P09_AlerteService_ObtenirRendezVousSemaine()
        {
            long ms = await MesurerAsync(async () =>
                await _alerteService.ObtenirRendezVousSemaine());
            _mesures.Add(("AlerteService.ObtenirRendezVousSemaine", ms));
        }

        [Test, Order(10)]
        public async Task P10_AlerteService_ObtenirAlertesRetours()
        {
            long ms = await MesurerAsync(async () =>
                await _alerteService.ObtenirAlertesRetours());
            _mesures.Add(("AlerteService.ObtenirAlertesRetours", ms));
        }

        [Test, Order(11)]
        public async Task P11_RetourService_ObtenirPageAsync()
        {
            long ms = await MesurerAsync(async () =>
                await _retourService.ObtenirPageAsync(1, 20));
            _mesures.Add(("RetourService.ObtenirPageAsync(p.1, 20)", ms));
        }

        [Test, Order(12)]
        public async Task P12_CommandeService_ObtenirPageLightAsync()
        {
            long ms = await MesurerAsync(async () =>
                await _commandeService.ObtenirPageLightAsync(1, 20));
            _mesures.Add(("CommandeService.ObtenirPageLightAsync(p.1, 20)", ms));
        }

        [Test, Order(13)]
        public async Task P13_CommandeService_ObtenirPageLightAsync_AvecFiltres()
        {
            long ms = await MesurerAsync(async () =>
                await _commandeService.ObtenirPageLightAsync(
                    1, 20, null, "Terminee", null, Debut, Fin));
            _mesures.Add(("CommandeService.ObtenirPageLight(filtres)", ms));
        }

        [Test, Order(14)]
        public void P14_AuditService_ObtenirToutesLesEntrees()
        {
            long ms = Mesurer(() => _auditService.ObtenirToutesLesEntrees());
            _mesures.Add(("AuditService.ObtenirToutesLesEntrees", ms));
        }

        [Test, Order(15)]
        public void P15_AuditService_ObtenirPageSimulee()
        {
            // Simule la pagination que l'on va ajouter : top 50 entrées triées
            long ms = Mesurer(() =>
            {
                using var ctx = _factory.CreateDbContext();
                _ = ctx.JournalAudit.AsNoTracking()
                    .OrderByDescending(j => j.DateHeureUtc)
                    .Skip(0).Take(50)
                    .ToList();
            });
            _mesures.Add(("AuditService.ObtenirPage simulation (p.1, 50)", ms));
        }

        [Test, Order(16)]
        public void P16_TresorerieService_VerifierCoherence()
        {
            long ms = Mesurer(() =>
                _tresorerieService.VerifierCoherenceArgentTravail(Debut, Fin));
            _mesures.Add(("TresorerieService.VerifierCoherence", ms));
        }

        [Test, Order(17)]
        public async Task P17_PaiementService_ObtenirPageLightAsync()
        {
            long ms = await MesurerAsync(async () =>
                await _paiementService.ObtenirPageLightAsync(1, 15));
            _mesures.Add(("PaiementService.ObtenirPageLightAsync(p.1, 15)", ms));
        }

        [Test, Order(18)]
        public void P18_Dashboard_ChargerCartesStats()
        {
            // Simule les 4 requêtes du dashboard sans instancier la vue WPF
            long ms = Mesurer(() =>
            {
                using var ctx = _factory.CreateDbContext();
                var aujourd = DateTime.Today;
                var debut   = new DateTime(aujourd.Year, aujourd.Month, 1);

                var _ = ctx.Paiements.AsNoTracking()
                    .Where(p => p.DatePaiement.Date >= debut && !p.EstAnnule)
                    .AsEnumerable().Sum(p => p.MontantPaye);

                var __ = ctx.Commandes.AsNoTracking()
                    .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                    .Include(c => c.Pieces)
                    .ToList();

                var ___ = ctx.Depenses.AsNoTracking()
                    .Where(d => d.DateDepense.Date >= debut && d.StatutValidation == "Validee")
                    .AsEnumerable().Sum(d => d.Montant);
            });
            _mesures.Add(("Dashboard.ChargerCartesStats (simulation)", ms));
        }

        [Test, Order(19)]
        public void P19_Dashboard_StatsCouturiers_N_Plus_1()
        {
            // Mesure le N+1 actuel : 1 query par couturier
            long ms = Mesurer(() =>
            {
                using var ctx = _factory.CreateDbContext();
                var couturiers = ctx.Employes.AsNoTracking()
                    .Where(e => e.Role == "Couturier").ToList();
                foreach (var c in couturiers)
                {
                    ctx.PiecesCommande.AsNoTracking()
                        .Include(p => p.Commande)
                        .Where(p => p.IdCouturier == c.IdEmploye)
                        .ToList();
                }
            });
            _mesures.Add(("Dashboard.StatsCouturiers (N+1 avant fix)", ms));
        }

        // ================================================================
        // HELPERS
        // ================================================================

        private static long Mesurer(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        private static async Task<long> MesurerAsync(Func<Task> action)
        {
            var sw = Stopwatch.StartNew();
            await action();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        // ================================================================
        // GÉNÉRATION DES DONNÉES MASSIVES
        // ================================================================
        private void GenererDonnees(ApplicationDbContext ctx)
        {
            // ── Boss ──────────────────────────────────────────────────────
            ctx.Employes.Add(new Employe
            {
                IdEmploye = 1, Nom = "DIALLO", Prenom = "Boss",
                Identifiant = "boss", MotDePasse = "x", Role = "Boss", Statut = "Actif"
            });

            // ── Couturiers ────────────────────────────────────────────────
            var nbCouturiers = 150;
            for (int i = 2; i <= 1 + nbCouturiers; i++)
                ctx.Employes.Add(new Employe
                {
                    IdEmploye   = i,
                    Nom         = $"Couturier{i}",
                    Prenom      = $"Prenom{i}",
                    Identifiant = $"cout{i}",
                    MotDePasse  = "x",
                    Role        = "Couturier",
                    Statut      = "Actif"
                });
            ctx.SaveChanges();

            // ── Clients par lots de 500 ───────────────────────────────────
            for (int lot = 0; lot < NB_CLIENTS / 500; lot++)
            {
                var batch = new List<Client>(500);
                for (int i = 0; i < 500; i++)
                {
                    int id = lot * 500 + i + 1;
                    batch.Add(new Client
                    {
                        IdClient   = id,
                        Nom        = $"NomClient{id}",
                        Prenom     = $"Prenom{id}",
                        Telephone  = $"7{id:D8}"
                    });
                }
                ctx.Clients.AddRange(batch);
                ctx.SaveChanges();
            }

            // ── Commandes + Pièces par lots de 500 ───────────────────────
            // NB_COMMANDES commandes, NB_PIECES pièces au total
            // = 2 pièces pour les 40 % premières commandes, 1 pièce pour le reste
            int pieceId = 0;
            int paiementId = 0;
            int paiementsRestants = NB_PAIEMENTS;

            for (int lot = 0; lot < NB_COMMANDES / 500; lot++)
            {
                var commandes = new List<Commande>(500);
                var pieces    = new List<PieceCommande>();
                var paiements = new List<Paiement>();

                for (int i = 0; i < 500; i++)
                {
                    int idCmd     = lot * 500 + i + 1;
                    int idClient  = (idCmd % NB_CLIENTS) + 1;
                    int idCout    = (idCmd % nbCouturiers) + 2;
                    var statut    = Statuts[idCmd % Statuts.Length];
                    var dateDebut = DateTime.Today.AddDays(-(_rng.Next(1, 730)));
                    var dateFin   = dateDebut.AddDays(_rng.Next(3, 21));

                    var commande = new Commande
                    {
                        IdCommande          = idCmd,
                        IdClient            = idClient,
                        DateDebut           = dateDebut,
                        HeureDebut          = TimeSpan.FromHours(8),
                        DateFin             = dateFin,
                        Statut              = statut,
                        TypeVetement        = Types[idCmd % Types.Length],
                        MontantTotal        = 5000m + (idCmd % 20) * 500m,
                        EstSupprimee        = false,
                        EstAnnulee          = false,
                        IdOperateurCreation = 1,
                        NomOperateurCreation = "Boss",
                        DateCreation        = dateDebut.ToUniversalTime()
                    };
                    commandes.Add(commande);

                    // 2 pièces pour la moitié des commandes
                    int nbPieces = (idCmd % 2 == 0) ? 2 : 1;
                    for (int p = 0; p < nbPieces; p++)
                    {
                        pieceId++;
                        var pieceStatut = (p == 1 && statut == "A faire") ? "A faire" : statut;
                        pieces.Add(new PieceCommande
                        {
                            IdPieceCommande = pieceId,
                            IdCommande      = idCmd,
                            TypeVetement    = Types[(pieceId) % Types.Length],
                            MontantCouture  = 2500m + (pieceId % 10) * 250m,
                            Statut          = pieceStatut,
                            IdCouturier     = idCout,
                            DateTerminee    = (pieceStatut == "Terminee" || pieceStatut == "Livree")
                                             ? dateDebut.AddDays(2) : (DateTime?)null
                        });
                    }

                    // Paiements (distribués sur 75 % des commandes)
                    if (paiementsRestants > 0 && idCmd % 4 != 0)
                    {
                        paiementId++;
                        paiementsRestants--;
                        decimal montant = commande.MontantTotal * (_rng.Next(3, 11) / 10m);
                        paiements.Add(new Paiement
                        {
                            IdPaiement    = paiementId,
                            IdCommande    = idCmd,
                            MontantPaye   = montant,
                            DatePaiement  = dateDebut.AddDays(1),
                            ModePaiement  = Modes[idCmd % Modes.Length],
                            EstAnnule     = false,
                            IdOperateur   = 1,
                            NomOperateur  = "Boss"
                        });
                    }
                }

                ctx.Commandes.AddRange(commandes);
                ctx.SaveChanges();
                ctx.PiecesCommande.AddRange(pieces);
                ctx.SaveChanges();
                if (paiements.Count > 0)
                {
                    ctx.Paiements.AddRange(paiements);
                    ctx.SaveChanges();
                }
            }

            // ── Journal d'audit par lots de 1 000 ────────────────────────
            var actions = new[] { "CREATION", "MODIFICATION", "SUPPRESSION", "PAIEMENT", "LIVRAISON" };
            string? hashPrev = null;
            for (int lot = 0; lot < NB_AUDIT / 1000; lot++)
            {
                var batch = new List<JournalAudit>(1000);
                for (int i = 0; i < 1000; i++)
                {
                    int id  = lot * 1000 + i + 1;
                    string data = $"Action{id}";
                    string hash = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes((hashPrev ?? "") + data)));
                    batch.Add(new JournalAudit
                    {
                        IdJournal     = id,
                        DateHeureUtc  = DateTime.UtcNow.AddMinutes(-id),
                        TypeAction    = actions[id % actions.Length],
                        Entite        = "Commande",
                        IdEntite      = (id % NB_COMMANDES) + 1,
                        NomOperateur  = "Boss",
                        IdOperateur   = 1,
                        HashPrecedent = hashPrev,
                        HashCourant   = hash
                    });
                    hashPrev = hash;
                }
                ctx.JournalAudit.AddRange(batch);
                ctx.SaveChanges();
            }

            TestContext.WriteLine($"[PERF] Base générée : {NB_CLIENTS} clients, {NB_COMMANDES} commandes, {pieceId} pièces, {paiementId} paiements, {NB_AUDIT} lignes audit");
        }
    }

    // ── Factory locale pour le test de charge ────────────────────────────
    internal class PerfDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _opts;
        public PerfDbContextFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_opts);
    }

    // ── Stub WhatsApp pour le test de charge ──────────────────────────────
    internal class PerfWhatsAppStub : IWhatsAppService
    {
        public string NormaliserNumero(string n) => n;
        public void OuvrirConversation(string numeroLocal, string message) { }
        public Task<string> MessageCommandePreteAsync(Commande c) => Task.FromResult("");
        public Task<string> MessageRappelRdvAsync(Commande c) => Task.FromResult("");
        public Task<string> MessageContactGeneralAsync(Client c) => Task.FromResult("");
        public Task NotifierCommandePreteAsync(Commande c) => Task.CompletedTask;
        public Task NotifierRappelRdvAsync(Commande c) => Task.CompletedTask;
        public Task ContacterClientAsync(Client c) => Task.CompletedTask;
    }
}
