using NUnit.Framework;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // ÉTAPE 2-ter — Tests complets de la suppression/annulation de commandes
    //
    //   T2ter_01 : Secrétaire annule à 9:59 → autorisée  (délai=10 min)
    //   T2ter_02 : Secrétaire annule à 10:00 → refusée   (délai=10 min)
    //   T2ter_03 : Secrétaire annule à 10:01 → refusée
    //   T2ter_04 : Délai paramétré à 5 min respecté
    //   T2ter_05 : Paiement lié → refusé
    //   T2ter_06 : Reçu imprimé → refusé
    //   T2ter_07 : Pièce en statut > A faire → refusé
    //   T2ter_08 : Autre opérateur (pas le créateur) → refusé
    //   T2ter_09 : Boss — motif vide → exception
    //   T2ter_10 : Boss — suppression logique OK avec motif
    //   T2ter_11 : Compteurs anti-fraude : suppressions et annulations comptées
    //   T2ter_12 : MarquerRecuImprime : double appel idempotent
    // ====================================================================

    [TestFixture]
    public class Etape2ter_SuppressionTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService _commandeService = null!;
        private FakeAuditService _auditService = null!;
        private FakeClock _clock = null!;

        private const int IdBoss       = 1;
        private const int IdSecretaire = 2;
        private const int IdSecretaire2 = 3; // une deuxième secrétaire pour tester l'auteur
        private const int IdCouturier  = 4;
        private const int IdClient     = 1;

        [SetUp]
        public void SetUp()
        {
            CommandeService.ViderCacheDoublonPourTests();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"E2ter_{Guid.NewGuid()}")
                .Options;
            _factory = new E2terDbContextFactory(options);
            _clock = new FakeClock(DateTime.UtcNow);
            _auditService = new FakeAuditService();
            var log = NullLoggerFactory.Instance;
            _commandeService = new CommandeService(
                _factory,
                new Logger<CommandeService>(log),
                _auditService,
                _clock);

            using var ctx = _factory.CreateDbContext();
            ctx.Employes.AddRange(
                new Employe { IdEmploye = IdBoss,        Nom = "DIALLO",  Prenom = "Mamadou",
                              Identifiant = "boss",   MotDePasse = "x", Role = "Boss",       Statut = "Actif" },
                new Employe { IdEmploye = IdSecretaire,  Nom = "FALL",    Prenom = "Marie",
                              Identifiant = "sec1",   MotDePasse = "x", Role = "Secretaire", Statut = "Actif" },
                new Employe { IdEmploye = IdSecretaire2, Nom = "NGOM",    Prenom = "Aissatou",
                              Identifiant = "sec2",   MotDePasse = "x", Role = "Secretaire", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier,   Nom = "CISSE",   Prenom = "Issa",
                              Identifiant = "cout",   MotDePasse = "x", Role = "Couturier",  Statut = "Actif" }
            );
            ctx.Clients.Add(new Client
            {
                IdClient = IdClient, Nom = "SARR", Prenom = "Fatou", Telephone = "771234567"
            });
            ctx.SaveChanges();
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>Crée une commande neuve "A faire" par la Secrétaire.</summary>
        private int CreerCommandeParSecretaire(int idSecretaire = IdSecretaire)
        {
            var cmd = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            var pce = new PieceCommande
            {
                TypeVetement = "Chemise", MontantCouture = 10_000m, IdCouturier = IdCouturier
            };
            _commandeService.Ajouter(cmd, pce, new List<Mesure>(), idSecretaire, "Marie FALL");
            return cmd.IdCommande;
        }

        /// <summary>Définit le délai dans la table Parametres.</summary>
        private void DefinirDelai(int minutes)
        {
            using var ctx = _factory.CreateDbContext();
            var cle = ParametresService.CleDelaiAnnulationSecretaire;
            var param = ctx.Parametres.Find(cle);
            if (param == null)
                ctx.Parametres.Add(new Parametre { Cle = cle, Valeur = minutes.ToString() });
            else
                param.Valeur = minutes.ToString();
            ctx.SaveChanges();
        }

        // ── T2ter_01 : Annulation à 9:59 → autorisée (délai 10 min) ──────

        [Test]
        public async Task T2ter_01_Secretaire_Annule_Avant_Delai_OK()
        {
            // La commande est créée maintenant
            int idCommande = CreerCommandeParSecretaire();

            // On avance l'horloge de 9 min 59 sec
            _clock.AvancerUtc(TimeSpan.FromMinutes(9).Add(TimeSpan.FromSeconds(59)));

            Assert.DoesNotThrowAsync(async () =>
                await _commandeService.AnnulerParSecretaireAsync(
                    idCommande, IdSecretaire, "Marie FALL", motif: "Test"));

            using var ctx = _factory.CreateDbContext();
            var cmd = ctx.Commandes.Find(idCommande)!;
            Assert.That(cmd.EstAnnulee || cmd.EstSupprimee, Is.True,
                "La commande doit être annulée/supprimée");
        }

        // ── T2ter_02 : Annulation à exactement 10:00 → refusée ────────────

        [Test]
        public async Task T2ter_02_Secretaire_Annule_A_Limite_Refusee()
        {
            int idCommande = CreerCommandeParSecretaire();

            // Avancer de 10 minutes exactement (délai = 10 min => >= refusé)
            _clock.AvancerUtc(TimeSpan.FromMinutes(10));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.AnnulerParSecretaireAsync(
                    idCommande, IdSecretaire, "Marie FALL", motif: "Test"));
            Assert.That(ex!.Message, Does.Contain("délai").IgnoreCase
                .Or.Contain("dépassé").IgnoreCase.Or.Contain("10"));
        }

        // ── T2ter_03 : Annulation à 10:01 → refusée ──────────────────────

        [Test]
        public async Task T2ter_03_Secretaire_Annule_Apres_Delai_Refusee()
        {
            int idCommande = CreerCommandeParSecretaire();

            _clock.AvancerUtc(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.AnnulerParSecretaireAsync(
                    idCommande, IdSecretaire, "Marie FALL", motif: "Test"));
            Assert.That(ex!.Message, Does.Contain("délai").IgnoreCase
                .Or.Contain("dépassé").IgnoreCase);
        }

        // ── T2ter_04 : Délai paramétré à 5 min respecté ──────────────────

        [Test]
        public async Task T2ter_04_Delai_Parametre_5_Minutes_Respecte()
        {
            DefinirDelai(5);
            int idCommande = CreerCommandeParSecretaire();

            // 4:59 → autorisé
            _clock.AvancerUtc(TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(59)));
            Assert.DoesNotThrowAsync(async () =>
                await _commandeService.AnnulerParSecretaireAsync(
                    idCommande, IdSecretaire, "Marie FALL", motif: "Test 5min OK"));

            // Nouvelle commande, 5:00 → refusé
            _clock.ReinitialiserA(DateTime.UtcNow);
            int idCommande2 = CreerCommandeParSecretaire();
            _clock.AvancerUtc(TimeSpan.FromMinutes(5));
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.AnnulerParSecretaireAsync(
                    idCommande2, IdSecretaire, "Marie FALL", motif: "Test 5min refus"));
            Assert.That(ex!.Message, Does.Contain("délai").IgnoreCase
                .Or.Contain("5").Or.Contain("dépassé").IgnoreCase);
        }

        // ── T2ter_05 : Paiement lié → refusé ─────────────────────────────

        [Test]
        public async Task T2ter_05_Paiement_Lie_Refus_Annulation()
        {
            int idCommande = CreerCommandeParSecretaire();

            // Ajouter un paiement directement en base (bypass le PaiementService pour éviter dépendances)
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.Paiements.Add(new Paiement
                {
                    IdCommande   = idCommande,
                    MontantPaye  = 2000m,
                    DatePaiement = DateTime.Now,
                    ModePaiement = "Espèces"
                });
                ctx.SaveChanges();
            }

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.AnnulerParSecretaireAsync(
                    idCommande, IdSecretaire, "Marie FALL", motif: "Test"));
            Assert.That(ex!.Message, Does.Contain("paiement").IgnoreCase);
        }

        // ── T2ter_06 : Reçu imprimé → refusé ─────────────────────────────

        [Test]
        public async Task T2ter_06_Recu_Imprime_Refus_Annulation()
        {
            int idCommande = CreerCommandeParSecretaire();

            // Simuler l'impression du reçu
            _commandeService.MarquerRecuImprime(idCommande);

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.AnnulerParSecretaireAsync(
                    idCommande, IdSecretaire, "Marie FALL", motif: "Test"));
            Assert.That(ex!.Message, Does.Contain("reçu").IgnoreCase.Or.Contain("imprimé").IgnoreCase);
        }

        // ── T2ter_07 : Pièce > A faire → refusé ──────────────────────────

        [Test]
        public async Task T2ter_07_Piece_StatutAvance_Refus_Annulation()
        {
            int idCommande = CreerCommandeParSecretaire();

            // Le Boss passe la pièce en "En cours"
            using (var ctx = _factory.CreateDbContext())
            {
                var piece = ctx.PiecesCommande
                    .FirstOrDefaultAsync(p => p.IdCommande == idCommande).Result!;
                piece.Statut = "En cours";
                ctx.SaveChanges();
            }

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.AnnulerParSecretaireAsync(
                    idCommande, IdSecretaire, "Marie FALL", motif: "Test"));
            Assert.That(ex!.Message, Does.Contain("faire").IgnoreCase
                .Or.Contain("statut").IgnoreCase.Or.Contain("Boss"));
        }

        // ── T2ter_08 : Autre secrétaire (pas le créateur) → refusé ──────

        [Test]
        public async Task T2ter_08_AutreSecretaire_Ne_Peut_Pas_Annuler()
        {
            // Commande créée par la Secrétaire 1
            int idCommande = CreerCommandeParSecretaire(IdSecretaire);

            // La Secrétaire 2 tente d'annuler
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.AnnulerParSecretaireAsync(
                    idCommande, IdSecretaire2, "Aissatou NGOM", motif: "Test"));
            Assert.That(ex!.Message, Does.Contain("créé").IgnoreCase
                .Or.Contain("vous").IgnoreCase.Or.Contain("vous-même").IgnoreCase);
        }

        // ── T2ter_09 : Boss — motif vide → exception ──────────────────────

        [Test]
        public async Task T2ter_09_Boss_Motif_Vide_Exception()
        {
            int idCommande = CreerCommandeParSecretaire();

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.SupprimerAsync(
                    idCommande, IdBoss, "Mamadou DIALLO", motif: "   ",
                    auditService: _auditService));
            Assert.That(ex!.Message, Does.Contain("motif").IgnoreCase
                .Or.Contain("obligatoire").IgnoreCase);
        }

        // ── T2ter_10 : Boss — suppression logique OK avec motif ──────────

        [Test]
        public async Task T2ter_10_Boss_Suppression_Avec_Motif_OK()
        {
            int idCommande = CreerCommandeParSecretaire();

            Assert.DoesNotThrowAsync(async () =>
                await _commandeService.SupprimerAsync(
                    idCommande, IdBoss, "Mamadou DIALLO",
                    motif: "Client injoignable après 3 appels",
                    auditService: _auditService));

            using var ctx = _factory.CreateDbContext();
            var cmd = ctx.Commandes.Find(idCommande)!;
            Assert.That(cmd.EstSupprimee, Is.True);
            Assert.That(cmd.MotifSuppression,
                Is.EqualTo("Client injoignable après 3 appels"));
        }

        // ── T2ter_11 : Compteurs anti-fraude ─────────────────────────────

        [Test]
        public async Task T2ter_11_CompteurAntiFraude_Suppressions_Et_Annulations()
        {
            // Boss supprime 2 commandes
            int id1 = CreerCommandeParSecretaire();
            int id2 = CreerCommandeParSecretaire();
            await _commandeService.SupprimerAsync(id1, IdBoss, "Mamadou DIALLO",
                "Motif 1", _auditService);
            await _commandeService.SupprimerAsync(id2, IdBoss, "Mamadou DIALLO",
                "Motif 2", _auditService);

            // Secrétaire annule 1 commande (dans le délai)
            int id3 = CreerCommandeParSecretaire();
            await _commandeService.AnnulerParSecretaireAsync(
                id3, IdSecretaire, "Marie FALL", "Annulation test");

            // Vérifier les entrées dans le FakeAuditService
            Assert.That(_auditService.Suppressions, Is.EqualTo(2),
                "Le Boss a fait 2 suppressions");
            Assert.That(_auditService.Annulations, Is.EqualTo(1),
                "La Secrétaire a fait 1 annulation");
        }

        // ── T2ter_12 : MarquerRecuImprime — double appel idempotent ───────

        [Test]
        public void T2ter_12_MarquerRecuImprime_Idempotent()
        {
            int idCommande = CreerCommandeParSecretaire();

            _commandeService.MarquerRecuImprime(idCommande);
            DateTime? datePremiereImpression;
            using (var ctx = _factory.CreateDbContext())
                datePremiereImpression = ctx.Commandes.Find(idCommande)!.DateImpressionRecu;

            Assert.That(datePremiereImpression, Is.Not.Null,
                "La date d'impression doit être définie après le premier appel");

            // Avancer le temps et rappeler
            _clock.AvancerUtc(TimeSpan.FromHours(1));
            _commandeService.MarquerRecuImprime(idCommande);

            using (var ctx = _factory.CreateDbContext())
            {
                var dateDeux = ctx.Commandes.Find(idCommande)!.DateImpressionRecu;
                Assert.That(dateDeux, Is.EqualTo(datePremiereImpression),
                    "La date ne doit pas changer lors d'une réimpression");
            }
        }

        // ── Helpers internes ──────────────────────────────────────────────

        private class E2terDbContextFactory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _options;
            public E2terDbContextFactory(DbContextOptions<ApplicationDbContext> options)
                => _options = options;
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Horloge factice pour contrôler le temps dans les tests
    // ─────────────────────────────────────────────────────────────────────
    public class FakeClock : IClock
    {
        private DateTime _utcNow;

        public FakeClock(DateTime utcNow)
        {
            _utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        }

        public DateTime Now    => _utcNow.ToLocalTime();
        public DateTime UtcNow => _utcNow;

        /// <summary>Avance l'horloge d'une durée.</summary>
        public void AvancerUtc(TimeSpan duree) => _utcNow = _utcNow.Add(duree);

        /// <summary>Réinitialise l'heure UTC à une valeur donnée.</summary>
        public void ReinitialiserA(DateTime utcNow) =>
            _utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Service d'audit factice pour compter les actions sans toucher la base
    // ─────────────────────────────────────────────────────────────────────
    public class FakeAuditService : IAuditService
    {
        public int Suppressions { get; private set; }
        public int Annulations  { get; private set; }

        public Task EnregistrerActionAsync(
            int idOperateur, string nomOperateur, string roleOperateur,
            string typeAction, string entite, int idEntite,
            object? valeursAvant = null, object? valeursApres = null,
            string? motif = null, bool envoyerNotification = false)
        {
            if (typeAction == ActionsAntiFraude.Suppression) Suppressions++;
            if (typeAction == ActionsAntiFraude.Annulation)  Annulations++;
            return Task.CompletedTask;
        }

        public List<JournalAudit> ObtenirToutesLesEntrees() => new();

        public List<JournalAudit> ObtenirParPeriode(DateTime debut, DateTime fin) => new();
        public List<JournalAudit> ObtenirParOperateur(int idOperateur) => new();
        public List<JournalAudit> ObtenirParTypeAction(string typeAction) => new();
        public List<JournalAudit> ObtenirParEntite(string entite, int idEntite) => new();

        public (bool Integre, string Message) VerifierIntegriteChaine()
            => (true, "Intégrité OK (fake)");

        public StatistiquesAudit ObtenirStatistiques(DateTime debut, DateTime fin)
            => new StatistiquesAudit();

        public List<IndicateurAntiFraude> ObtenirIndicateurAntiFraude(DateTime depuisUtc)
        {
            var liste = new List<IndicateurAntiFraude>();
            if (Suppressions > 0)
                liste.Add(new IndicateurAntiFraude
                {
                    IdOperateur   = 1,
                    NomOperateur  = "Boss Test",
                    RoleOperateur = "Boss",
                    Suppressions  = Suppressions
                });
            if (Annulations > 0)
                liste.Add(new IndicateurAntiFraude
                {
                    IdOperateur   = 2,
                    NomOperateur  = "Secrétaire Test",
                    RoleOperateur = "Secretaire",
                    Annulations   = Annulations
                });
            return liste;
        }
    }
}
