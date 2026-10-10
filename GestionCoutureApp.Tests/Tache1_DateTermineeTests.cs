using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // TÂCHE 1 — Commissions basées sur DateTerminee (et non DateFin RDV)
    //
    // Règles testées :
    //   T1_01 : pièce terminée avant la date de RDV → commissionnée dans
    //           la période de sa DateTerminee, PAS de celle de DateFin.
    //   T1_02 : pièce non terminée (A faire) → non commissionnée.
    //   T1_03 : retour non résolu → exclue des commissions.
    //   T1_04 : pièce déjà commissionnée (IdCommission != null) → pas de doublon.
    //   T1_05 : passage à Terminee → DateTerminee renseignée.
    //   T1_06 : passage à Livree depuis Terminee → DateTerminee conservée.
    //   T1_07 : retour en arrière (reprise) → DateTerminee remise à null.
    //   T1_08 : ChangerStatutPiece → met à jour DateTerminee.
    //   T1_09 : ForcerStatutToutesPieces → met à jour DateTerminee de toutes les pièces.
    //   T1_10 : Modifier (chemin via CommandeService.Modifier) → DateTerminee.
    // ====================================================================

    [TestFixture]
    public class Tache1_DateTermineeTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService  _commandeService  = null!;
        private CommissionService _commissionService = null!;
        private PaiementService  _paiementService  = null!;

        private const int IdBoss       = 1;
        private const int IdSecretaire = 2;
        private const int IdCouturier  = 3;
        private const int IdClient     = 1;

        [SetUp]
        public void SetUp()
        {
            CommandeService.ViderCacheDoublonPourTests();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"Tache1_{Guid.NewGuid()}")
                .Options;
            _factory = new T1DbContextFactory(options);

            var logFactory = NullLoggerFactory.Instance;
            _commandeService   = new CommandeService(_factory,  new Logger<CommandeService>(logFactory));
            _commissionService = new CommissionService(_factory, new Logger<CommissionService>(logFactory), new MockParametresService());
            _paiementService   = new PaiementService(_factory,  new Logger<PaiementService>(logFactory));

            using var ctx = _factory.CreateDbContext();
            ctx.Employes.AddRange(
                new Employe { IdEmploye = IdBoss,       Nom = "DIALLO", Prenom = "Mamadou",
                              Identifiant = "boss",  MotDePasse = "x", Role = "Boss",       Statut = "Actif" },
                new Employe { IdEmploye = IdSecretaire, Nom = "FALL",   Prenom = "Marie",
                              Identifiant = "sec",   MotDePasse = "x", Role = "Secretaire", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier,  Nom = "CISSE",  Prenom = "Issa",
                              Identifiant = "cout",  MotDePasse = "x", Role = "Couturier",  Statut = "Actif" }
            );
            ctx.Clients.Add(new Client { IdClient = IdClient, Nom = "SARR", Prenom = "Fatou", Telephone = "771234567" });
            ctx.SaveChanges();
        }

        // ── T1_01 : pièce terminée AVANT le RDV → commissionnée sur DateTerminee ──

        [Test]
        public void T1_01_Piece_Terminee_Avant_RDV_Est_Commissionnee_Sur_DateTerminee()
        {
            // Commande avec RDV le 10/10 — pièce terminée le 05/10
            DateTime dateRdv = new DateTime(2026, 10, 10);
            var (idCommande, idPiece) = CreerCommandePiece(dateRdv, 10000m);

            // On passe la pièce en Terminee — DateTerminee sera ~aujourd'hui (05/10 dans le test)
            DateTime avantTerminee = DateTime.UtcNow.AddSeconds(-2);
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");
            DateTime apresTerminee = DateTime.UtcNow.AddSeconds(2);

            // Vérifier que DateTerminee est renseignée et dans la fenêtre
            var piece = PieceEnBase(idPiece);
            Assert.That(piece.DateTerminee, Is.Not.Null, "DateTerminee doit être renseignée");
            Assert.That(piece.DateTerminee!.Value, Is.GreaterThanOrEqualTo(avantTerminee));
            Assert.That(piece.DateTerminee!.Value, Is.LessThanOrEqualTo(apresTerminee));

            // Calculer aperçu sur la période de AUJOURD'HUI (pas celle du RDV)
            DateTime aujourd_hui = DateTime.UtcNow.Date;
            var apercu = _commissionService.CalculerApercu(
                aujourd_hui, aujourd_hui, 10m, false, IdCouturier);

            Assert.That(apercu, Has.Count.EqualTo(1), "Le couturier doit apparaître dans l'aperçu");
            Assert.That(apercu[0].NbCommandes, Is.EqualTo(1));

            // Vérifier que la même pièce N'apparaît PAS dans la période du RDV (futur)
            var apercuRdv = _commissionService.CalculerApercu(
                dateRdv.Date, dateRdv.Date, 10m, false, IdCouturier);
            Assert.That(apercuRdv, Is.Empty, "La pièce ne doit PAS être dans la période du RDV");
        }

        // ── T1_02 : pièce non terminée → non commissionnée ───────────────

        [Test]
        public void T1_02_Piece_Non_Terminee_Non_Commissionnee()
        {
            DateTime dateRdv = DateTime.Now.AddDays(3);
            var (_, idPiece) = CreerCommandePiece(dateRdv, 8000m);
            // Statut reste "A faire"

            var apercu = _commissionService.CalculerApercu(
                DateTime.Today, DateTime.Today.AddDays(30), 10m, false, IdCouturier);

            Assert.That(apercu, Is.Empty, "Aucune commission pour une pièce non terminée");
        }

        // ── T1_03 : retour non résolu → exclue ───────────────────────────

        [Test]
        public void T1_03_Piece_Avec_Retour_Non_Resolu_Exclue()
        {
            var (_, idPiece) = CreerCommandePiece(DateTime.Now.AddDays(2), 12000m);
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // Créer un retour non résolu pour cette pièce
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.Retours.Add(new Retour
                {
                    IdPieceCommande = idPiece,
                    Statut          = "Signale",
                    DateSignalement = DateTime.Now.AddDays(-5), // antérieur à la période
                    EstAnnule       = false
                });
                ctx.SaveChanges();
            }

            // La pièce est terminée mais a un retour non résolu → exclue
            var apercu = _commissionService.CalculerApercu(
                DateTime.Today.AddDays(-7), DateTime.Today.AddDays(7), 10m, false, IdCouturier);

            Assert.That(apercu, Is.Empty,
                "La pièce avec retour non résolu doit être exclue de l'aperçu");
        }

        // ── T1_04 : pièce déjà commissionnée → pas de doublon ────────────

        [Test]
        public void T1_04_Piece_Deja_Commissionnee_Exclue()
        {
            var (_, idPiece) = CreerCommandePiece(DateTime.Now.AddDays(2), 10000m);
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // Verrouiller la pièce avec une commission fictive
            using (var ctx = _factory.CreateDbContext())
            {
                var comm = new Commission
                {
                    IdEmploye = IdCouturier, NomEmployeSnapshot = "Issa CISSE",
                    DateDebutPeriode = DateTime.Today.AddMonths(-1), DateFinPeriode = DateTime.Today,
                    BaseCalcul = "Total", Pourcentage = 10m,
                    BaseMontant = 10000m, MontantCommission = 1000m,
                    NbCommandes = 1, DateCalcul = DateTime.Now,
                    IdOperateur = IdBoss, NomOperateur = "Mamadou DIALLO", EstAnnulee = false
                };
                ctx.Commissions.Add(comm);
                ctx.SaveChanges();

                var pieceDb = ctx.PiecesCommande.Find(idPiece)!;
                pieceDb.IdCommission = comm.IdCommission;
                ctx.SaveChanges();
            }

            var apercu = _commissionService.CalculerApercu(
                DateTime.Today.AddDays(-7), DateTime.Today.AddDays(7), 10m, false, IdCouturier);

            Assert.That(apercu, Is.Empty,
                "Une pièce déjà commissionnée (IdCommission != null) ne doit pas apparaître");
        }

        // ── T1_05 : passage à Terminee → DateTerminee renseignée ─────────

        [Test]
        public void T1_05_ChangerStatut_Terminee_Renseigne_DateTerminee()
        {
            var (_, idPiece) = CreerCommandePiece(DateTime.Now.AddDays(5), 10000m);

            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.Null, "Avant Terminee, DateTerminee doit être null");
            Assert.That(PieceEnBase(idPiece).IdOperateurTerminee, Is.Null);

            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            var piece = PieceEnBase(idPiece);
            Assert.That(piece.DateTerminee,        Is.Not.Null);
            Assert.That(piece.IdOperateurTerminee, Is.EqualTo(IdBoss));
        }

        // ── T1_06 : Terminee → Livree → DateTerminee conservée ───────────

        [Test]
        public void T1_06_Terminee_Vers_Livree_Conserve_DateTerminee()
        {
            var (idCommande, idPiece) = CreerCommandePiece(DateTime.Now.AddDays(2), 5000m);
            // Solder la commande pour pouvoir livrer
            Payer(idCommande, 5000m);

            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");
            DateTime dateTermineeInitiale = PieceEnBase(idPiece).DateTerminee!.Value;

            // Petit délai pour être sûr que la date ne change pas
            System.Threading.Thread.Sleep(10);
            _commandeService.ChangerStatutPiece(idPiece, "Livree", IdBoss, "Mamadou DIALLO");

            var piece = PieceEnBase(idPiece);
            Assert.That(piece.DateTerminee, Is.EqualTo(dateTermineeInitiale),
                "Passer de Terminee à Livree ne doit pas changer DateTerminee");
        }

        // ── T1_07 : retour en arrière → DateTerminee remise à null ───────

        [Test]
        public void T1_07_Retour_En_Arriere_Remet_DateTerminee_Null()
        {
            var (_, idPiece) = CreerCommandePiece(DateTime.Now.AddDays(5), 10000m);
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.Not.Null, "Précondition : DateTerminee renseignée");

            // Boss remet en arrière avec motif obligatoire (TÂCHE 5 flux strict)
            _commandeService.ChangerStatutPiece(idPiece, "En cours", IdBoss, "Mamadou DIALLO",
                motifLivraisonNonSoldee: "Retouche nécessaire");

            var piece = PieceEnBase(idPiece);
            Assert.That(piece.DateTerminee,        Is.Null, "Retour en arrière doit remettre DateTerminee à null");
            Assert.That(piece.IdOperateurTerminee, Is.Null);

            // Et si on re-termine, DateTerminee est renseignée à nouveau
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");
            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.Not.Null, "Re-terminer doit rerenseigner DateTerminee");
        }

        // ── T1_08 : Modifier (chemin Modifier) met à jour DateTerminee ───

        [Test]
        public void T1_08_Modifier_Statut_Terminee_Renseigne_DateTerminee()
        {
            var (idCommande, piece) = CreerCommandeFormulaire(DateTime.Now.AddDays(5), 10000m);

            var commandeModif = new Commande
            {
                IdCommande = idCommande, IdClient = IdClient,
                DateFin = DateTime.Now.AddDays(5), HeureDebut = TimeSpan.Zero
            };
            var pieceModif = new PieceCommande
            {
                IdPieceCommande = piece.IdPieceCommande,
                TypeVetement    = piece.TypeVetement,
                MontantCouture  = piece.MontantCouture,
                IdCouturier     = piece.IdCouturier,
                Statut          = "Terminee"
            };

            _commandeService.Modifier(commandeModif, pieceModif, new List<Mesure>(),
                IdBoss, "Mamadou DIALLO");

            var enBase = PieceEnBase(piece.IdPieceCommande);
            Assert.That(enBase.Statut,        Is.EqualTo("Terminee"));
            Assert.That(enBase.DateTerminee,  Is.Not.Null, "Modifier doit renseigner DateTerminee");
        }

        // ── T1_09 : ForcerStatutToutesPieces ─────────────────────────────

        [Test]
        public void T1_09_ForcerStatutToutesPieces_Terminee_Renseigne_DateTerminee()
        {
            var (idCommande, idPiece1) = CreerCommandePiece(DateTime.Now.AddDays(3), 8000m);
            // Ajouter une 2e pièce
            var p2 = new PieceCommande { TypeVetement = "Pantalon", MontantCouture = 6000m, IdCouturier = IdCouturier };
            _commandeService.AjouterPiece(idCommande, p2, new List<Mesure>(), roleBoss: true);
            int idPiece2 = p2.IdPieceCommande;

            _commandeService.ForcerStatutToutesPieces(idCommande, "Terminee", IdBoss, "Mamadou DIALLO");

            Assert.That(PieceEnBase(idPiece1).DateTerminee, Is.Not.Null, "Pièce 1 : DateTerminee");
            Assert.That(PieceEnBase(idPiece2).DateTerminee, Is.Not.Null, "Pièce 2 : DateTerminee");
        }

        // ── T1_10 : pièce Terminee A hors période N'apparaît PAS dans période B ─

        [Test]
        public void T1_10_Piece_Terminee_Dans_Mauvaise_Periode_Non_Commissionnee()
        {
            // Pièce terminée il y a 10 jours
            var (_, idPiece) = CreerCommandePiece(DateTime.Now.AddDays(5), 10000m);
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // Forcer DateTerminee à il y a 10 jours en base pour simuler un cas passé
            using (var ctx = _factory.CreateDbContext())
            {
                var p = ctx.PiecesCommande.Find(idPiece)!;
                p.DateTerminee = DateTime.UtcNow.AddDays(-10);
                ctx.SaveChanges();
            }

            // Calcul sur la période d'aujourd'hui → la pièce ne doit PAS y être
            var apercuAujourd_hui = _commissionService.CalculerApercu(
                DateTime.Today, DateTime.Today, 10m, false, IdCouturier);
            Assert.That(apercuAujourd_hui, Is.Empty,
                "Pièce terminée il y a 10 jours ne doit pas être dans la période d'aujourd'hui");

            // Calcul sur la période d'il y a 10 jours → doit y être
            DateTime il_y_a_10 = DateTime.Today.AddDays(-10);
            var apercuPassé = _commissionService.CalculerApercu(
                il_y_a_10, il_y_a_10, 10m, false, IdCouturier);
            Assert.That(apercuPassé, Has.Count.EqualTo(1),
                "Pièce terminée il y a 10 jours doit être dans la période d'il y a 10 jours");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private (int idCommande, int idPiece) CreerCommandePiece(DateTime dateRdv, decimal montant)
        {
            var commande = new Commande { IdClient = IdClient, DateFin = dateRdv };
            var piece    = new PieceCommande
            {
                TypeVetement  = "Chemise",
                MontantCouture = montant,
                IdCouturier   = IdCouturier
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO");
            return (commande.IdCommande, piece.IdPieceCommande);
        }

        /// <summary>
        /// Crée une commande ET retourne l'objet "formulaire" détaché (comme le fait l'écran).
        /// </summary>
        private (int idCommande, PieceCommande formulaire) CreerCommandeFormulaire(DateTime dateRdv, decimal montant)
        {
            var commande = new Commande { IdClient = IdClient, DateFin = dateRdv };
            var piece    = new PieceCommande
            {
                TypeVetement  = "Robe",
                MontantCouture = montant,
                IdCouturier   = IdCouturier
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO");
            var formulaire = new PieceCommande
            {
                IdPieceCommande = piece.IdPieceCommande,
                TypeVetement    = piece.TypeVetement,
                MontantCouture  = piece.MontantCouture,
                IdCouturier     = piece.IdCouturier,
                Statut          = piece.Statut
            };
            return (commande.IdCommande, formulaire);
        }

        private void Payer(int idCommande, decimal montant)
        {
            _paiementService.Ajouter(new Paiement
            {
                IdCommande    = idCommande,
                MontantPaye   = montant,
                DatePaiement  = DateTime.Now,
                ModePaiement  = "Espèces"
            }, idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO");
        }

        private PieceCommande PieceEnBase(int idPiece)
        {
            using var ctx = _factory.CreateDbContext();
            return ctx.PiecesCommande.Single(p => p.IdPieceCommande == idPiece);
        }
    }

    internal class T1DbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public T1DbContextFactory(DbContextOptions<ApplicationDbContext> options)
            => _options = options;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
    }
}
