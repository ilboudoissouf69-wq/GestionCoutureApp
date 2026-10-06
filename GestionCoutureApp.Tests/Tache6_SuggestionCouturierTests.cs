using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // TÂCHE 6 — Suggestion couturier automatique + état Indisponible
    //
    //   T6_01 : SuggererCouturier retourne le moins chargé
    //   T6_02 : Égalité → celui qui a terminé le moins récemment
    //   T6_03 : Couturier Indisponible exclu de la suggestion
    //   T6_04 : Aucun couturier disponible → retourne null
    //   T6_05 : Couturier sans pièce actives est prioritaire
    //   T6_06 : Suggestion seulement, jamais d'affectation forcée silencieuse
    // ====================================================================

    [TestFixture]
    public class Tache6_SuggestionCouturierTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService _commandeService = null!;

        private const int IdBoss       = 1;
        private const int IdCouturier1 = 3;
        private const int IdCouturier2 = 4;
        private const int IdCouturier3 = 5;
        private const int IdClient     = 1;

        [SetUp]
        public void SetUp()
        {
            CommandeService.ViderCacheDoublonPourTests();
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"T6_{Guid.NewGuid()}").Options;
            _factory = new T6DbContextFactory(opts);
            var log  = NullLoggerFactory.Instance;
            _commandeService = new CommandeService(_factory, new Logger<CommandeService>(log));

            using var ctx = _factory.CreateDbContext();
            ctx.Employes.AddRange(
                new Employe { IdEmploye = IdBoss,       Nom = "DIALLO",  Prenom = "Mamadou",
                              Identifiant = "boss",   MotDePasse = "x", Role = "Boss",      Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier1, Nom = "CISSE",   Prenom = "Issa",
                              Identifiant = "cout1",  MotDePasse = "x", Role = "Couturier", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier2, Nom = "BARRY",   Prenom = "Mamadou",
                              Identifiant = "cout2",  MotDePasse = "x", Role = "Couturier", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier3, Nom = "TRAORE",  Prenom = "Seydou",
                              Identifiant = "cout3",  MotDePasse = "x", Role = "Couturier", Statut = "Indisponible" }
            );
            ctx.Clients.Add(new Client { IdClient = IdClient, Nom = "SARR", Prenom = "Fatou", Telephone = "771234567" });
            ctx.SaveChanges();
        }

        // ── T6_01 : retourne le moins chargé ─────────────────────────────

        [Test]
        public void T6_01_Suggere_Le_Moins_Charge()
        {
            // Couturier1 a 2 pièces actives, Couturier2 en a 0
            AjouterPieceActive(IdCouturier1);
            AjouterPieceActive(IdCouturier1);

            var suggere = _commandeService.SuggererCouturier();

            Assert.That(suggere, Is.Not.Null);
            Assert.That(suggere!.IdEmploye, Is.EqualTo(IdCouturier2),
                "Couturier2 (0 pièce active) doit être suggéré avant Couturier1 (2 pièces)");
        }

        // ── T6_02 : égalité → celui qui a terminé le moins récemment ─────

        [Test]
        public void T6_02_Egalite_Departage_Par_DateTerminee()
        {
            // Les deux couturiers ont 1 pièce active chacun
            AjouterPieceActive(IdCouturier1);
            AjouterPieceActive(IdCouturier2);

            // Couturier1 a terminé une pièce il y a 2 jours (plus récent)
            // Couturier2 a terminé une pièce il y a 10 jours (moins récent → suggéré)
            AjouterPieceTerminee(IdCouturier1, DateTime.UtcNow.AddDays(-2));
            AjouterPieceTerminee(IdCouturier2, DateTime.UtcNow.AddDays(-10));

            var suggere = _commandeService.SuggererCouturier();

            Assert.That(suggere, Is.Not.Null);
            Assert.That(suggere!.IdEmploye, Is.EqualTo(IdCouturier2),
                "Couturier2 a terminé le moins récemment → prioritaire à charge égale");
        }

        // ── T6_03 : Indisponible exclu ────────────────────────────────────

        [Test]
        public void T6_03_Couturier_Indisponible_Exclu()
        {
            // Couturier3 est Indisponible et a 0 pièce active (le moins chargé)
            // Mais il doit être exclu
            AjouterPieceActive(IdCouturier1);
            AjouterPieceActive(IdCouturier2);

            var suggere = _commandeService.SuggererCouturier();

            Assert.That(suggere, Is.Not.Null);
            Assert.That(suggere!.IdEmploye, Is.Not.EqualTo(IdCouturier3),
                "Un couturier Indisponible ne doit jamais être suggéré");
        }

        // ── T6_04 : tous Indisponibles → null ─────────────────────────────

        [Test]
        public void T6_04_Aucun_Disponible_Retourne_Null()
        {
            // Rendre tous les couturiers Indisponibles
            using var ctx = _factory.CreateDbContext();
            foreach (var e in ctx.Employes.Where(e => e.Role == "Couturier" || e.Role == "Boss"))
                e.Statut = "Indisponible";
            ctx.SaveChanges();

            var suggere = _commandeService.SuggererCouturier();
            Assert.That(suggere, Is.Null, "Aucun couturier disponible → null");
        }

        // ── T6_05 : couturier sans pièce active prioritaire ───────────────

        [Test]
        public void T6_05_Couturier_Sans_Piece_Active_Prioritaire()
        {
            // Couturier2 n'a aucune pièce active
            AjouterPieceActive(IdCouturier1);

            var suggere = _commandeService.SuggererCouturier();

            Assert.That(suggere!.IdEmploye, Is.EqualTo(IdCouturier2));
        }

        // ── T6_06 : suggestion ≠ affectation forcée ───────────────────────

        [Test]
        public void T6_06_Suggestion_Seulement_Pas_Affectation_Forcee()
        {
            // La suggestion ne modifie aucune donnée en base
            var avant = CountPiecesAvecCouturier();
            _commandeService.SuggererCouturier();
            var apres = CountPiecesAvecCouturier();

            Assert.That(apres, Is.EqualTo(avant),
                "SuggererCouturier ne doit pas modifier les pièces en base");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private int _counter = 0;

        private void AjouterPieceActive(int idCouturier)
        {
            _counter++;
            var cmd = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            var pce = new PieceCommande
            {
                TypeVetement   = "Chemise",
                MontantCouture = 5000m + _counter,
                IdCouturier    = idCouturier
            };
            _commandeService.Ajouter(cmd, pce, new List<Mesure>(), IdBoss, "Mamadou DIALLO");
        }

        private void AjouterPieceTerminee(int idCouturier, DateTime dateTerminee)
        {
            _counter++;
            using var ctx = _factory.CreateDbContext();
            // Ajouter directement pour éviter la fenêtre anti-doublon
            var cmd = new Commande
            {
                IdClient = IdClient, DateFin = DateTime.Now.AddDays(7),
                IdOperateurCreation = IdBoss, NomOperateurCreation = "Boss"
            };
            ctx.Commandes.Add(cmd);
            ctx.SaveChanges();

            var pce = new PieceCommande
            {
                IdCommande = cmd.IdCommande, TypeVetement = "Robe",
                MontantCouture = 7000m + _counter, IdCouturier = idCouturier,
                Statut = "Terminee", DateTerminee = dateTerminee
            };
            ctx.PiecesCommande.Add(pce);
            ctx.SaveChanges();
        }

        private int CountPiecesAvecCouturier()
        {
            using var ctx = _factory.CreateDbContext();
            return ctx.PiecesCommande.Count(p => p.IdCouturier.HasValue);
        }
    }

    internal class T6DbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public T6DbContextFactory(DbContextOptions<ApplicationDbContext> options) => _options = options;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
    }
}
