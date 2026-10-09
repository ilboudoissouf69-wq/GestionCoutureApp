using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // TÂCHE 7 — ObtenirRetards (AlerteService)
    //
    //   TR_01 : pièce non terminée avec RDV passé → incluse
    //   TR_02 : pièce Terminee avec RDV passé     → exclue
    //   TR_03 : pièce Livree   avec RDV passé     → exclue
    //   TR_04 : commande supprimée (EstSupprimee)  → exclue
    //   TR_05 : pièce non terminée avec RDV futur  → exclue
    //   TR_06 : RendezVousException respectée
    //           (exception passée → incluse ; exception future → exclue)
    //   TR_07 : tri par DateRendezVous ascendant
    //   TR_08 : TypeAlerte == "Retard"
    //   TR_09 : ObtenirAlertesActuelles inchangée — n'inclut PAS les retards
    //   TR_10 : ObtenirRendezVousSemaine inchangée — exclut les non-terminées
    //           dont le RDV est passé
    // ====================================================================

    [TestFixture]
    public class Tache7_ObtenirRetardsTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private AlerteService _alerteService = null!;

        private const int IdClient     = 1;
        private const int IdCouturier  = 2;

        // Date de base : un mois dans le passé pour les RDV dépassés,
        // et une semaine dans le futur pour les RDV futurs.
        private static readonly DateTime RdvPasse  = DateTime.Now.AddDays(-10);
        private static readonly DateTime RdvFutur  = DateTime.Now.AddDays(3);

        [SetUp]
        public void SetUp()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"TR_{Guid.NewGuid()}")
                .Options;
            _factory = new TRDbContextFactory(opts);
            _alerteService = new AlerteService(_factory, new MockParametresService());

            using var ctx = _factory.CreateDbContext();
            ctx.Clients.Add(new Client
            {
                IdClient   = IdClient,
                Nom        = "DIALLO",
                Prenom     = "Fatou",
                Telephone  = "771234567"
            });
            ctx.Employes.Add(new Employe
            {
                IdEmploye   = IdCouturier,
                Nom         = "CISSE",
                Prenom      = "Issa",
                Identifiant = "cout",
                MotDePasse  = "x",
                Role        = "Couturier",
                Statut      = "Actif"
            });
            ctx.SaveChanges();
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>Crée une commande + une pièce et retourne leurs ids.</summary>
        private (int idCommande, int idPiece) CreerPieceAvecRdv(
            string statut,
            DateTime dateFin,
            bool estSupprimee              = false,
            DateTime? rendezVousException  = null)
        {
            using var ctx = _factory.CreateDbContext();

            var commande = new Commande
            {
                IdClient              = IdClient,
                DateDebut             = DateTime.Now.AddMonths(-1).Date,
                DateFin               = dateFin.Date,
                HeureFin              = dateFin.TimeOfDay == TimeSpan.Zero
                                          ? new TimeSpan(17, 0, 0)
                                          : dateFin.TimeOfDay,
                HeureDebut            = new TimeSpan(9, 0, 0),
                EstSupprimee          = estSupprimee,
                IdOperateurCreation   = IdCouturier,
                NomOperateurCreation  = "Issa CISSE"
            };
            ctx.Commandes.Add(commande);
            ctx.SaveChanges();

            var piece = new PieceCommande
            {
                IdCommande          = commande.IdCommande,
                TypeVetement        = "Chemise",
                MontantCouture      = 5000m,
                Statut              = statut,
                IdCouturier         = IdCouturier,
                RendezVousException = rendezVousException
            };
            ctx.PiecesCommande.Add(piece);
            ctx.SaveChanges();

            return (commande.IdCommande, piece.IdPieceCommande);
        }

        // ── TR_01 : pièce "En cours" avec RDV passé → incluse ────────────
        [Test]
        public async Task TR_01_PieceEnCours_RdvPasse_Incluse()
        {
            CreerPieceAvecRdv("En cours", RdvPasse);

            var retards = await _alerteService.ObtenirRetards();

            Assert.That(retards, Has.Count.EqualTo(1));
        }

        // ── TR_02 : pièce "Terminee" avec RDV passé → exclue ─────────────
        [Test]
        public async Task TR_02_PieceTerminee_RdvPasse_Exclue()
        {
            CreerPieceAvecRdv("Terminee", RdvPasse);

            var retards = await _alerteService.ObtenirRetards();

            Assert.That(retards, Is.Empty,
                "Une pièce Terminee ne doit jamais apparaître en retard.");
        }

        // ── TR_03 : pièce "Livree" avec RDV passé → exclue ───────────────
        [Test]
        public async Task TR_03_PieceLivree_RdvPasse_Exclue()
        {
            CreerPieceAvecRdv("Livree", RdvPasse);

            var retards = await _alerteService.ObtenirRetards();

            Assert.That(retards, Is.Empty,
                "Une pièce Livree ne doit jamais apparaître en retard.");
        }

        // ── TR_04 : commande supprimée → exclue ──────────────────────────
        [Test]
        public async Task TR_04_CommandeSupprimee_Exclue()
        {
            CreerPieceAvecRdv("En cours", RdvPasse, estSupprimee: true);

            var retards = await _alerteService.ObtenirRetards();

            Assert.That(retards, Is.Empty,
                "Une commande supprimée ne doit jamais remonter, même en retard.");
        }

        // ── TR_05 : pièce non terminée avec RDV futur → exclue ───────────
        [Test]
        public async Task TR_05_PieceEnCours_RdvFutur_Exclue()
        {
            CreerPieceAvecRdv("En cours", RdvFutur);

            var retards = await _alerteService.ObtenirRetards();

            Assert.That(retards, Is.Empty,
                "Un RDV futur ne doit pas être un retard.");
        }

        // ── TR_06 : RendezVousException respectée ────────────────────────
        [Test]
        public async Task TR_06_RendezVousException_Passee_Incluse()
        {
            // Commande avec RDV futur MAIS exception passée sur la pièce
            CreerPieceAvecRdv(
                "A faire",
                RdvFutur,                         // DateFin commande = futur
                rendezVousException: RdvPasse);   // mais exception pièce = passé

            var retards = await _alerteService.ObtenirRetards();

            Assert.That(retards, Has.Count.EqualTo(1),
                "L'exception de RDV passée doit primer sur le RDV global.");
        }

        [Test]
        public async Task TR_06b_RendezVousException_Future_Exclue()
        {
            // Commande avec RDV passé MAIS exception future sur la pièce
            CreerPieceAvecRdv(
                "A faire",
                RdvPasse,                         // DateFin commande = passé
                rendezVousException: RdvFutur);   // exception pièce = futur

            var retards = await _alerteService.ObtenirRetards();

            Assert.That(retards, Is.Empty,
                "L'exception de RDV future doit primer sur le RDV global passé.");
        }

        // ── TR_07 : tri ascendant par DateRendezVous ─────────────────────
        [Test]
        public async Task TR_07_TriAscendant()
        {
            var plusRecent  = DateTime.Now.AddDays(-2);
            var plusAncien  = DateTime.Now.AddDays(-8);
            var intermediaire = DateTime.Now.AddDays(-5);

            CreerPieceAvecRdv("En cours", plusRecent);
            CreerPieceAvecRdv("A faire",  plusAncien);
            CreerPieceAvecRdv("En cours", intermediaire);

            var retards = await _alerteService.ObtenirRetards();

            Assert.That(retards, Has.Count.EqualTo(3));
            Assert.That(retards[0].DateRendezVous, Is.LessThanOrEqualTo(retards[1].DateRendezVous));
            Assert.That(retards[1].DateRendezVous, Is.LessThanOrEqualTo(retards[2].DateRendezVous));
        }

        // ── TR_08 : TypeAlerte == "Retard" ───────────────────────────────
        [Test]
        public async Task TR_08_TypeAlerte_EstRetard()
        {
            CreerPieceAvecRdv("A faire", RdvPasse);

            var retards = await _alerteService.ObtenirRetards();

            Assert.That(retards, Has.Count.EqualTo(1));
            Assert.That(retards[0].TypeAlerte, Is.EqualTo("Retard"),
                "TypeAlerte doit être 'Retard'.");
        }

        // ── TR_09 : ObtenirAlertesActuelles n'inclut PAS les retards ─────
        [Test]
        public async Task TR_09_ObtenirAlertesActuelles_ExclutRetards()
        {
            // Pièce en retard (RDV dépassé, non terminée)
            CreerPieceAvecRdv("En cours", RdvPasse);

            var alertesActuelles = await _alerteService.ObtenirAlertesActuelles();

            bool contientRetard = alertesActuelles.Any(a => a.DateRendezVous < DateTime.Now
                                                          && a.Statut != "Terminée"
                                                          && a.Statut != "Livrée");
            Assert.That(contientRetard, Is.False,
                "ObtenirAlertesActuelles ne doit pas remonter les pièces en retard.");
        }

        // ── TR_10 : ObtenirRendezVousSemaine exclut les retards non terminés
        [Test]
        public async Task TR_10_ObtenirRendezVousSemaine_ExclutRetardsNonTermines()
        {
            // Pièce En cours avec RDV passé → retard, pas dans ObtenirRendezVousSemaine
            CreerPieceAvecRdv("En cours", RdvPasse);
            // Pièce Terminee avec RDV passé → doit apparaître dans ObtenirRendezVousSemaine
            CreerPieceAvecRdv("Terminee", RdvPasse);

            var semaine = await _alerteService.ObtenirRendezVousSemaine();

            // La pièce "En cours" passée ne doit PAS y être
            bool contientEnCoursRetard = semaine.Any(a =>
                a.DateRendezVous < DateTime.Now
                && a.Statut != "Terminée"
                && a.Statut != "Livrée");

            Assert.That(contientEnCoursRetard, Is.False,
                "ObtenirRendezVousSemaine ne doit pas contenir les pièces en retard non terminées.");

            // La pièce Terminee DOIT y être (retrait à venir)
            bool contientTerminee = semaine.Any(a => a.Statut == "Terminée");
            Assert.That(contientTerminee, Is.True,
                "ObtenirRendezVousSemaine doit inclure les pièces Terminee même si le RDV est passé.");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Factory EF Core InMemory pour les tests AlerteService
    // ────────────────────────────────────────────────────────────────────
    internal class TRDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public TRDbContextFactory(DbContextOptions<ApplicationDbContext> options)
            => _options = options;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
    }
}
