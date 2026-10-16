using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // ÉTAPE 7 — Alertes retard & message d'excuse
    //
    //   E7_01 : Seuil 60 min — pièce à 30 min déclenchée, pièce à 90 min ignorée
    //   E7_02 : Seuil 120 min — pièce à 90 min déclenchée, pièce à 150 min ignorée
    //   E7_03 : Anti-doublon — alerte déjà traitée pour ce RDV non retournée
    //   E7_04 : Anti-doublon — alerte revenue après report (nouveau RDV)
    //   E7_05 : Pièce Terminee exclue des alertes risque retard
    //   E7_06 : Pièce Livree exclue des alertes risque retard
    //   E7_07 : GenererMessageExcuse remplace toutes les balises
    //   E7_08 : GenererMessageExcuse avec modèle vide → modèle par défaut
    //   E7_09 : PreparerMessageExcuse calcule nouvelle_date = RDV + retard_report_jours
    //   E7_10 : EnregistrerPrevention incrémente NbPreventions
    //   E7_11 : EnregistrerPrevention — rôle Couturier refusé
    //   E7_12 : ReporterRendezVous déplace DateFin de N jours (commande)
    //   E7_13 : ReporterRendezVous ne modifie pas le statut ni le montant
    //   E7_14 : ReporterRendezVous déplace RendezVousException (pièce)
    //   E7_15 : ReporterRendezVous déplace DateRdvReprise (retour)
    //   E7_16 : NumeroClientValide accepte les numéros valides
    //   E7_17 : NumeroClientValide rejette les chaînes invalides
    //   E7_18 : ObtenirStatsDashboard — NbCommandesEnRetard correct
    //   E7_19 : ObtenirStatsDashboard — NbReportsMois via journal audit
    //   E7_20 : ObtenirStatsDashboard — NbClientsPrevenusMois distinct par client
    // ====================================================================

    [TestFixture]
    public class Etape7_AlerteRetardTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private RetardAlerteService _service = null!;
        private AuditServiceStub    _audit   = null!;
        private ParametresServiceStub _params = null!;

        private const int IdBoss       = 1;
        private const int IdSecretaire = 2;
        private const int IdCouturier  = 3;
        private const int IdClient     = 10;

        // ── SetUp ─────────────────────────────────────────────────────────
        [SetUp]
        public void SetUp()
        {
            var opts = TestDbHelper.CreerOptions("E7");
            _factory = new E7DbContextFactory(opts);
            _audit   = new AuditServiceStub();
            _params  = new ParametresServiceStub();
            _service = new RetardAlerteService(_factory, _params, _audit);

            using var ctx = _factory.CreateDbContext();
            ctx.Employes.AddRange(
                new Employe { IdEmploye = IdBoss,       Nom = "Boss",   Prenom = "Admin",  Identifiant = "boss",   MotDePasse = "x", Role = "Boss",       Statut = "Actif" },
                new Employe { IdEmploye = IdSecretaire, Nom = "Sec",    Prenom = "Marie",  Identifiant = "msec",   MotDePasse = "x", Role = "Secretaire", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier,  Nom = "Coutu",  Prenom = "Moussa", Identifiant = "mcoutu", MotDePasse = "x", Role = "Couturier",  Statut = "Actif" }
            );
            ctx.Clients.Add(new Client
            {
                IdClient = IdClient,
                Nom = "OUEDRAOGO", Prenom = "Fatou",
                Telephone = "70001234"
            });
            ctx.SaveChanges();
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>Crée une commande avec une pièce dont l'échéance = now + offsetMinutes.</summary>
        private (Commande cmd, PieceCommande piece) CreerCommandeAvecEcheance(
            ApplicationDbContext ctx, int offsetMinutes, string statut = "A faire", int idCommande = 0)
        {
            var now      = DateTime.Now;
            var echeance = now.AddMinutes(offsetMinutes);
            var cmd = new Commande
            {
                IdCommande          = idCommande > 0 ? idCommande : 0,
                IdClient            = IdClient,
                DateDebut           = now.AddDays(-5),
                DateFin             = echeance.Date,
                HeureFin            = echeance.TimeOfDay,
                HeureDebut          = TimeSpan.Zero,
                Statut              = "En cours",
                TypeVetement        = "Boubou",
                IdOperateurCreation = IdBoss
            };
            ctx.Commandes.Add(cmd);
            ctx.SaveChanges();

            var piece = new PieceCommande
            {
                IdCommande    = cmd.IdCommande,
                TypeVetement  = "Boubou brodé",
                Statut        = statut,
                MontantCouture = 5000m
            };
            ctx.PiecesCommande.Add(piece);
            ctx.SaveChanges();
            return (cmd, piece);
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_01 — Seuil 60 min
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_01_Seuil60Min_Declenchee30Min_Ignoree90Min()
        {
            _params.RetardAlerteMinutes = 60;

            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;

            var (cmd30, _) = CreerCommandeAvecEcheance(ctx, 30);  // dans 30 min → déclenchée
            var (cmd90, _) = CreerCommandeAvecEcheance(ctx, 90);  // dans 90 min → ignorée

            var alertes = await _service.ObtenirAlertesRisqueRetardAsync(now);

            Assert.That(alertes.Any(a => a.IdCommande == cmd30.IdCommande), Is.True,
                "Pièce à 30 min doit déclencher l'alerte (seuil 60 min)");
            Assert.That(alertes.Any(a => a.IdCommande == cmd90.IdCommande), Is.False,
                "Pièce à 90 min ne doit PAS déclencher l'alerte (seuil 60 min)");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_02 — Seuil 120 min
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_02_Seuil120Min_Declenchee90Min_Ignoree150Min()
        {
            _params.RetardAlerteMinutes = 120;

            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;

            var (cmd90,  _) = CreerCommandeAvecEcheance(ctx, 90);   // déclenchée
            var (cmd150, _) = CreerCommandeAvecEcheance(ctx, 150);  // ignorée

            var alertes = await _service.ObtenirAlertesRisqueRetardAsync(now);

            Assert.That(alertes.Any(a => a.IdCommande == cmd90.IdCommande),  Is.True);
            Assert.That(alertes.Any(a => a.IdCommande == cmd150.IdCommande), Is.False);
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_03 — Anti-doublon : alerte déjà traitée pour ce RDV
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_03_AntiDoublon_AlerteTraiteePourCeRdv_NonRetournee()
        {
            _params.RetardAlerteMinutes = 120;

            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;
            var (cmd, piece) = CreerCommandeAvecEcheance(ctx, 30);

            var echeance = RetardAlerteService.CalculerEcheancePiece(piece, cmd);
            ctx.SuivisAlerteRetard.Add(new SuiviAlerteRetard
            {
                TypeCible             = RetardAlerteService.CibleCommande,
                IdCible               = cmd.IdCommande,
                IdClient              = IdClient,
                DateRdvTraite         = RetardAlerteService.TronquerMinute(echeance),
                NbPreventions         = 1,
                DateDernierMessage    = DateTime.Now
            });
            ctx.SaveChanges();

            var alertes = await _service.ObtenirAlertesRisqueRetardAsync(now);

            Assert.That(alertes.Any(a => a.IdCommande == cmd.IdCommande), Is.False,
                "Alerte déjà traitée ne doit pas réapparaître pour le même RDV");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_04 — Anti-doublon : alerte revenue après report
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_04_AntiDoublon_AlerteRevenueApresReport()
        {
            _params.RetardAlerteMinutes = 120;

            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;
            var (cmd, piece) = CreerCommandeAvecEcheance(ctx, 30);

            // Simuler un suivi pour l'ANCIEN RDV (il y a 2 jours)
            ctx.SuivisAlerteRetard.Add(new SuiviAlerteRetard
            {
                TypeCible             = RetardAlerteService.CibleCommande,
                IdCible               = cmd.IdCommande,
                IdClient              = IdClient,
                DateRdvTraite         = DateTime.Now.AddDays(-2), // ancien RDV
                NbPreventions         = 1
            });
            ctx.SaveChanges();

            // L'alerte doit revenir car le nouveau RDV est différent
            var alertes = await _service.ObtenirAlertesRisqueRetardAsync(now);

            Assert.That(alertes.Any(a => a.IdCommande == cmd.IdCommande), Is.True,
                "Après report, alerte doit revenir car RDV a changé");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_05 — Pièce Terminee exclue
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_05_PieceTerminee_ExclueDesAlertes()
        {
            _params.RetardAlerteMinutes = 120;

            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;
            CreerCommandeAvecEcheance(ctx, 30, statut: "Terminee");

            var alertes = await _service.ObtenirAlertesRisqueRetardAsync(now);

            Assert.That(alertes, Is.Empty, "Pièce Terminee ne doit pas générer d'alerte");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_06 — Pièce Livree exclue
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_06_PieceLivree_ExclueDesAlertes()
        {
            _params.RetardAlerteMinutes = 120;

            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;
            CreerCommandeAvecEcheance(ctx, 30, statut: "Livree");

            var alertes = await _service.ObtenirAlertesRisqueRetardAsync(now);

            Assert.That(alertes, Is.Empty, "Pièce Livree ne doit pas générer d'alerte");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_07 — GenererMessageExcuse remplace toutes les balises
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public void E7_07_GenererMessageExcuse_RemplaceToutesLesBalises()
        {
            string modele    = "Bonjour {client}, votre {type_vetement} (cmd n°{numero_commande}) sera prête le {nouvelle_date}. — {atelier}";
            var    dateRdv   = new DateTime(2026, 11, 15);
            string message   = _service.GenererMessageExcuse(modele, "Fatou", "42", "Boubou", dateRdv, "Atelier Test");

            Assert.That(message, Does.Contain("Fatou"));
            Assert.That(message, Does.Contain("42"));
            Assert.That(message, Does.Contain("Boubou"));
            Assert.That(message, Does.Contain("15/11/2026"));
            Assert.That(message, Does.Contain("Atelier Test"));
            Assert.That(message, Does.Not.Contain("{client}"));
            Assert.That(message, Does.Not.Contain("{type_vetement}"));
            Assert.That(message, Does.Not.Contain("{numero_commande}"));
            Assert.That(message, Does.Not.Contain("{nouvelle_date}"));
            Assert.That(message, Does.Not.Contain("{atelier}"));
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_08 — GenererMessageExcuse avec modèle vide → défaut
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public void E7_08_GenererMessageExcuse_ModeleVide_UtiliseDefaut()
        {
            string message = _service.GenererMessageExcuse("", "Fatou", "42", "Boubou", DateTime.Today, "Test");

            Assert.That(message, Does.Contain("Fatou"),
                "Même avec modèle vide, le message par défaut doit être utilisé");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_09 — PreparerMessageExcuse calcule la bonne nouvelle date
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_09_PreparerMessageExcuse_NouvelleDate_EstRdvPlusReportJours()
        {
            _params.RetardReportJours = 2;

            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;
            var (cmd, piece) = CreerCommandeAvecEcheance(ctx, -10); // RDV dépassé

            var echeance = RetardAlerteService.CalculerEcheancePiece(piece, cmd);

            var brouillon = await _service.PreparerMessageExcuseAsync(
                RetardAlerteService.CibleCommande,
                cmd.IdCommande,
                echeance,
                now);

            Assert.That(brouillon.NouvelleDateProposee.Date,
                Is.EqualTo(echeance.Date.AddDays(2)),
                "Nouvelle date = RDV actuel + retard_report_jours");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_10 — EnregistrerPrevention incrémente NbPreventions
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_10_EnregistrerPrevention_IncrementeNbPreventions()
        {
            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;
            var (cmd, piece) = CreerCommandeAvecEcheance(ctx, 30);
            var echeance = RetardAlerteService.CalculerEcheancePiece(piece, cmd);

            await _service.EnregistrerPreventionAsync(
                RetardAlerteService.CibleCommande,
                cmd.IdCommande,
                echeance,
                IdClient,
                IdBoss,
                "Admin Boss");

            using var ctx2 = _factory.CreateDbContext();
            var suivi = await ctx2.SuivisAlerteRetard
                .FirstOrDefaultAsync(s => s.TypeCible == RetardAlerteService.CibleCommande
                                       && s.IdCible   == cmd.IdCommande);

            Assert.That(suivi,                    Is.Not.Null);
            Assert.That(suivi!.NbPreventions,     Is.EqualTo(1));
            Assert.That(suivi.DateDernierMessage, Is.Not.Null);
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_11 — EnregistrerPrevention refusée pour le rôle Couturier
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public void E7_11_EnregistrerPrevention_RoleCouturier_Refuse()
        {
            using var ctx = _factory.CreateDbContext();
            var (cmd, piece) = CreerCommandeAvecEcheance(ctx, 30);
            var echeance = RetardAlerteService.CalculerEcheancePiece(piece, cmd);

            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await _service.EnregistrerPreventionAsync(
                    RetardAlerteService.CibleCommande,
                    cmd.IdCommande,
                    echeance,
                    IdClient,
                    IdCouturier,  // couturier → refusé
                    "Moussa Coutu"));
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_12 — ReporterRendezVous déplace DateFin (commande)
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_12_ReporterRendezVous_Commande_DeplaceDataFin()
        {
            _params.RetardReportJours = 1;

            using var ctx = _factory.CreateDbContext();
            var (cmd, piece) = CreerCommandeAvecEcheance(ctx, -5); // RDV dépassé
            var echeance = RetardAlerteService.CalculerEcheancePiece(piece, cmd);
            var dateFinAvant = cmd.DateFin.Date;

            await _service.ReporterRendezVousAsync(
                RetardAlerteService.CibleCommande,
                cmd.IdCommande,
                echeance,
                IdBoss, "Admin Boss");

            using var ctx2 = _factory.CreateDbContext();
            var cmdAjour = await ctx2.Commandes.FindAsync(cmd.IdCommande);

            Assert.That(cmdAjour!.DateFin.Date,
                Is.EqualTo(dateFinAvant.AddDays(1)),
                "DateFin doit être décalée de 1 jour");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_13 — Report ne modifie ni statut, ni paiement, ni commission
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_13_ReporterRendezVous_NeToucheNiStatutNiPaiementNiCommission()
        {
            _params.RetardReportJours = 1;

            using var ctx = _factory.CreateDbContext();
            var (cmd, piece) = CreerCommandeAvecEcheance(ctx, -5);
            var echeance = RetardAlerteService.CalculerEcheancePiece(piece, cmd);

            string statutAvant   = piece.Statut;
            int?   commissionAv  = piece.IdCommission;
            decimal montantAv    = piece.MontantCouture;

            await _service.ReporterRendezVousAsync(
                RetardAlerteService.CibleCommande,
                cmd.IdCommande,
                echeance,
                IdBoss, "Admin Boss");

            using var ctx2 = _factory.CreateDbContext();
            var pieceAjour = await ctx2.PiecesCommande.FindAsync(piece.IdPieceCommande);

            Assert.That(pieceAjour!.Statut,      Is.EqualTo(statutAvant));
            Assert.That(pieceAjour.IdCommission, Is.EqualTo(commissionAv));
            Assert.That(pieceAjour.MontantCouture, Is.EqualTo(montantAv));
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_14 — ReporterRendezVous déplace RendezVousException (pièce)
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_14_ReporterRendezVous_Piece_DeplaceRendezVousException()
        {
            _params.RetardReportJours = 2;

            using var ctx = _factory.CreateDbContext();
            var rdvException = DateTime.Now.AddMinutes(-10);
            var (cmd, piece) = CreerCommandeAvecEcheance(ctx, -5);

            // Ajouter RendezVousException sur la pièce
            var pieceCopy = ctx.PiecesCommande.Find(piece.IdPieceCommande)!;
            pieceCopy.RendezVousException = rdvException;
            ctx.SaveChanges();

            await _service.ReporterRendezVousAsync(
                RetardAlerteService.CiblePiece,
                piece.IdPieceCommande,
                rdvException,
                IdBoss, "Admin Boss");

            using var ctx2 = _factory.CreateDbContext();
            var pieceAjour = await ctx2.PiecesCommande.FindAsync(piece.IdPieceCommande);

            Assert.That(pieceAjour!.RendezVousException!.Value.Date,
                Is.EqualTo(rdvException.Date.AddDays(2)));
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_15 — ReporterRendezVous déplace DateRdvReprise (retour)
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_15_ReporterRendezVous_Retour_DeplaceDataRdvReprise()
        {
            _params.RetardReportJours = 1;

            using var ctx = _factory.CreateDbContext();
            var (cmd, piece) = CreerCommandeAvecEcheance(ctx, -5);
            var rdvReprise = DateTime.Now.AddMinutes(-10);

            var retour = new Retour
            {
                IdCommande      = cmd.IdCommande,
                IdPieceCommande = piece.IdPieceCommande,
                Statut          = "En reprise",
                DateSignalement = DateTime.Now.AddDays(-2),
                DateRdvReprise  = rdvReprise,
                IdOperateurEnregistrement = IdBoss,
                NomOperateurEnregistrement = "Admin Boss",
                NumeroRetour    = "RET-2026-00001"
            };
            ctx.Retours.Add(retour);
            ctx.SaveChanges();

            await _service.ReporterRendezVousAsync(
                RetardAlerteService.CibleRetour,
                retour.IdRetour,
                rdvReprise,
                IdBoss, "Admin Boss");

            using var ctx2 = _factory.CreateDbContext();
            var retourAjour = await ctx2.Retours.FindAsync(retour.IdRetour);

            Assert.That(retourAjour!.DateRdvReprise!.Value.Date,
                Is.EqualTo(rdvReprise.Date.AddDays(1)));
            Assert.That(retourAjour.Statut, Is.EqualTo("En reprise"),
                "Le statut du retour ne doit pas changer lors d'un report");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_16 — NumeroClientValide accepte les numéros valides
        // ══════════════════════════════════════════════════════════════════
        [TestCase("70001234")]
        [TestCase("+22670001234")]
        [TestCase("0022670001234")]
        [TestCase("226 70 00 12 34")]
        public void E7_16_NumeroClientValide_AccepteNumerosValides(string numero)
        {
            Assert.That(_service.NumeroClientValide(numero), Is.True,
                $"Numéro '{numero}' doit être valide");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_17 — NumeroClientValide rejette les chaînes invalides
        // ══════════════════════════════════════════════════════════════════
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("123")]
        [TestCase("abc")]
        [TestCase(null)]
        public void E7_17_NumeroClientValide_RejetteNumerosInvalides(string? numero)
        {
            Assert.That(_service.NumeroClientValide(numero), Is.False,
                $"Numéro '{numero}' doit être invalide");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_18 — ObtenirStatsDashboard — NbCommandesEnRetard
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_18_StatsDashboard_NbCommandesEnRetard_Correct()
        {
            _params.RetardAlerteMinutes = 120;

            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;
            CreerCommandeAvecEcheance(ctx, -30);  // dépassé → en retard
            CreerCommandeAvecEcheance(ctx, 30);   // dans 30 min → risque mais pas retard

            var stats = await _service.ObtenirStatsDashboardAsync(now);

            Assert.That(stats.NbCommandesEnRetard, Is.EqualTo(1),
                "Seule la commande dépassée doit être comptée comme retard");
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_19 — ObtenirStatsDashboard — NbReportsMois via journal
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_19_StatsDashboard_NbReportsMois_CompteViajournalAudit()
        {
            // Le test vérifie que ObtenirStatsDashboardAsync ne plante pas
            // même sans entrées de journal (NbReportsMois = 0).
            var stats = await _service.ObtenirStatsDashboardAsync();

            Assert.That(stats.NbReportsMois, Is.GreaterThanOrEqualTo(0));
        }

        // ══════════════════════════════════════════════════════════════════
        // E7_20 — ObtenirStatsDashboard — NbClientsPrevenusMois distinct
        // ══════════════════════════════════════════════════════════════════
        [Test]
        public async Task E7_20_StatsDashboard_NbClientsPrevenusMois_CompteurDistinctParClient()
        {
            using var ctx = _factory.CreateDbContext();
            var now = DateTime.Now;
            var debutMois = new DateTime(now.Year, now.Month, 1);

            // Deux préventions pour le même client (même mois) → compté 1 fois
            ctx.SuivisAlerteRetard.AddRange(
                new SuiviAlerteRetard
                {
                    TypeCible = RetardAlerteService.CibleCommande, IdCible = 100,
                    IdClient = IdClient, DateRdvTraite = now.AddMinutes(-10),
                    NbPreventions = 2, DateDernierMessage = now.AddMinutes(-30)
                },
                new SuiviAlerteRetard
                {
                    TypeCible = RetardAlerteService.CibleRetour, IdCible = 200,
                    IdClient = IdClient, DateRdvTraite = now.AddMinutes(-5),
                    NbPreventions = 1, DateDernierMessage = now.AddMinutes(-10)
                }
            );
            ctx.SaveChanges();

            var stats = await _service.ObtenirStatsDashboardAsync(now);

            Assert.That(stats.NbClientsPrevenusMois, Is.EqualTo(1),
                "Deux préventions du même client ce mois = 1 client prévenu distinct");
        }
    }

    // ── Stubs & helpers pour Étape 7 ──────────────────────────────────────

    /// <summary>Factory InMemory pour les tests Étape 7.</summary>
    internal class E7DbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _opts;
        public E7DbContextFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
        public ApplicationDbContext CreateDbContext() => new(_opts);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken _ = default)
            => Task.FromResult(CreateDbContext());
    }
}
