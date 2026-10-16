using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // ÉTAPE 1 — Tests de cohérence trésorerie après opérations de statut
    //
    // Formule : Bénéfice = CA encaissé − commissions − charges
    // Le bilan ne doit jamais être faussé par :
    //   - un paiement puis son annulation
    //   - passage Terminee (déclenchement commission)
    //   - rétrogradation depuis Terminee
    //   - annulation d'une commission
    //   - livraison avec reste à payer (Boss avec motif)
    //
    // Tests :
    //   TR_01 : après paiement → bénéfice = montant payé (aucune commission, aucune charge)
    //   TR_02 : après annulation paiement → bénéfice revient à 0
    //   TR_03 : après terminée + commission enregistrée → bénéfice = CA − commission
    //   TR_04 : après rétrogradation depuis Terminee → pièce disparaît de l'aperçu commission
    //   TR_05 : annulation commission → bénéfice revient à CA seul (sans commission)
    //   TR_06 : livraison non soldée forcée par Boss → bilan cohérent avec CA réel encaissé
    //   TR_07 : VerifierCoherenceArgentTravail ne signale aucun écart sur scénario nominal
    // ====================================================================

    [TestFixture]
    public class Etape1_TresorerieCoherenceTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService   _commandeService   = null!;
        private TresorerieService _tresorerieService = null!;
        private PaiementService   _paiementService   = null!;
        private CommissionService _commissionService = null!;

        private const int IdBoss      = 1;
        private const int IdCouturier = 2;
        private const int IdClient    = 1;

        // Période de test fixe (toutes les opérations se font à DateTime.Today)
        private DateTime Debut => DateTime.Today.AddMonths(-1);
        private DateTime Fin   => DateTime.Today.AddMonths(1);

        [SetUp]
        public void SetUp()
        {
            CommandeService.ViderCacheDoublonPourTests();
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"TR_{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            _factory = new TrDbContextFactory(opts);

            var log = NullLoggerFactory.Instance;
            _commandeService   = new CommandeService(_factory, new Logger<CommandeService>(log));
            _tresorerieService = new TresorerieService(_factory, new Logger<TresorerieService>(log));
            _paiementService   = new PaiementService(_factory, new Logger<PaiementService>(log));
            _commissionService = new CommissionService(
                _factory, new Logger<CommissionService>(log), new MockParametresService());

            using var ctx = _factory.CreateDbContext();
            ctx.Employes.AddRange(
                new Employe { IdEmploye = IdBoss,      Nom = "DIALLO", Prenom = "Mamadou",
                              Identifiant = "boss", MotDePasse = "x", Role = "Boss",      Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier, Nom = "CISSE",  Prenom = "Issa",
                              Identifiant = "cout", MotDePasse = "x", Role = "Couturier", Statut = "Actif" }
            );
            ctx.Clients.Add(new Client
            {
                IdClient = IdClient, Nom = "SARR", Prenom = "Fatou", Telephone = "771234567"
            });
            ctx.SaveChanges();
        }

        // ── TR_01 : après paiement, bénéfice = montant payé ──────────────

        [Test]
        public void TR_01_Apres_Paiement_Benefice_Egal_MontantPaye()
        {
            var (idCommande, _) = CreerCommande(montantCouture: 5000m);
            Payer(idCommande, 5000m);

            var bilan = _tresorerieService.CalculerBilan(Debut, Fin);

            Assert.That(bilan.ChiffreAffaires, Is.EqualTo(5000m),
                "CA doit refléter le montant encaissé");
            Assert.That(bilan.TotalCommissions, Is.EqualTo(0m),
                "Aucune commission enregistrée");
            Assert.That(bilan.BilanNet, Is.EqualTo(5000m),
                "Bénéfice = CA − commissions − primes − dépenses = 5000");
        }

        // ── TR_02 : annulation paiement → bénéfice revient à 0 ───────────

        [Test]
        public void TR_02_Apres_Annulation_Paiement_Benefice_Zero()
        {
            var (idCommande, _) = CreerCommande(montantCouture: 5000m);
            int idPaiement = Payer(idCommande, 5000m);

            // Annuler le paiement
            _paiementService.Annuler(idPaiement, "Annulation test TR_02", IdBoss, "Mamadou DIALLO");

            var bilan = _tresorerieService.CalculerBilan(Debut, Fin);

            Assert.That(bilan.ChiffreAffaires, Is.EqualTo(0m),
                "CA doit revenir à 0 après annulation du paiement");
            Assert.That(bilan.BilanNet, Is.EqualTo(0m),
                "Bénéfice = 0 après annulation du seul paiement");
        }

        // ── TR_03 : terminée + commission → bénéfice = CA − commission ───

        [Test]
        public void TR_03_Apres_Terminee_Et_Commission_Benefice_Correct()
        {
            const decimal montantCouture = 10000m;
            const decimal pct = 10m;
            var (idCommande, idPiece) = CreerCommande(montantCouture);
            Payer(idCommande, montantCouture);

            // Flux : A faire → En cours → Terminee
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // Enregistrer la commission
            var apercu = _commissionService.CalculerApercu(Debut, Fin, pct, false, null);
            Assert.That(apercu, Has.Count.EqualTo(1), "Un seul couturier dans l'aperçu");
            _commissionService.EnregistrerCommissions(apercu, Debut, Fin, pct, false, IdBoss, "Mamadou DIALLO");

            var bilan = _tresorerieService.CalculerBilan(Debut, Fin);
            decimal commissionAttendue = Math.Round(montantCouture * pct / 100m, 0, MidpointRounding.AwayFromZero);

            Assert.That(bilan.ChiffreAffaires, Is.EqualTo(montantCouture));
            Assert.That(bilan.TotalCommissions, Is.EqualTo(commissionAttendue));
            Assert.That(bilan.BilanNet, Is.EqualTo(montantCouture - commissionAttendue),
                "Bénéfice = CA − commission");
        }

        // ── TR_04 : rétrogradation → pièce disparaît de l'aperçu ─────────

        [Test]
        public void TR_04_Retrograder_Depuis_Terminee_Disparait_Apercu_Commission()
        {
            var (idCommande, idPiece) = CreerCommande(10000m);
            Payer(idCommande, 10000m);

            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // Rétrograder
            _commandeService.ChangerStatutPiece(idPiece, "En cours", IdBoss, "Mamadou DIALLO",
                motifLivraisonNonSoldee: "Défaut constaté");

            var apercu = _commissionService.CalculerApercu(Debut, Fin, 10m, false, null);

            Assert.That(apercu, Is.Empty,
                "Après rétrogradation, la pièce ne doit plus figurer dans l'aperçu commission");

            // Le bilan doit être cohérent : CA encaissé, commission = 0
            var bilan = _tresorerieService.CalculerBilan(Debut, Fin);
            Assert.That(bilan.TotalCommissions, Is.EqualTo(0m));
            Assert.That(bilan.BilanNet, Is.EqualTo(bilan.ChiffreAffaires),
                "Sans commission ni charge, bénéfice = CA");
        }

        // ── TR_05 : annulation commission → bénéfice revient au CA ───────

        [Test]
        public void TR_05_Annulation_Commission_Benefice_Revient_Au_CA()
        {
            const decimal montantCouture = 8000m;
            const decimal pct = 10m;
            var (idCommande, idPiece) = CreerCommande(montantCouture);
            Payer(idCommande, montantCouture);

            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            var apercu = _commissionService.CalculerApercu(Debut, Fin, pct, false, null);
            _commissionService.EnregistrerCommissions(apercu, Debut, Fin, pct, false, IdBoss, "Mamadou DIALLO");

            // Bilan avant annulation
            var bilanAvant = _tresorerieService.CalculerBilan(Debut, Fin);
            Assert.That(bilanAvant.TotalCommissions, Is.GreaterThan(0m));

            // Annuler la commission
            using (var ctx = _factory.CreateDbContext())
            {
                var comm = ctx.Commissions.First();
                _commissionService.Annuler(comm.IdCommission, "Erreur de calcul", IdBoss, "Mamadou DIALLO");
            }

            // Bilan après annulation
            var bilanApres = _tresorerieService.CalculerBilan(Debut, Fin);
            Assert.That(bilanApres.TotalCommissions, Is.EqualTo(0m),
                "Aucune commission active après annulation");
            Assert.That(bilanApres.BilanNet, Is.EqualTo(montantCouture),
                "Bénéfice revient au CA complet sans commission");

            // La pièce doit être déverrouillée (IdCommission = null)
            using (var ctx = _factory.CreateDbContext())
            {
                var piece = ctx.PiecesCommande.Find(idPiece)!;
                Assert.That(piece.IdCommission, Is.Null,
                    "IdCommission doit être null après annulation de la commission");
            }
        }

        // ── TR_06 : livraison non soldée forcée par Boss → bilan cohérent ─

        [Test]
        public void TR_06_Livraison_NonSoldee_Forcee_Bilan_Coherent()
        {
            const decimal montantCouture = 6000m;
            const decimal acompte = 3000m;
            var (idCommande, idPiece) = CreerCommande(montantCouture);
            Payer(idCommande, acompte);

            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // Boss force la livraison avec acompte seulement (motif obligatoire)
            _commandeService.ChangerStatutPiece(idPiece, "Livree", IdBoss, "Mamadou DIALLO",
                motifLivraisonNonSoldee: "Client voyage — accord verbal pour le solde");

            var bilan = _tresorerieService.CalculerBilan(Debut, Fin);

            // Le CA encaissé = acompte seulement (pas le solde non payé)
            Assert.That(bilan.ChiffreAffaires, Is.EqualTo(acompte),
                "CA = montant réellement encaissé, pas le montant facturé");
            Assert.That(bilan.BilanNet, Is.LessThanOrEqualTo(bilan.ChiffreAffaires),
                "Le bénéfice ne peut pas dépasser le CA réel");
        }

        // ── TR_07 : VerifierCoherenceArgentTravail sans écart sur scénario nominal ──

        [Test]
        public void TR_07_Coherence_ArgentTravail_Scénario_Nominal_Sans_Ecart()
        {
            const decimal montantCouture = 7500m;
            var (idCommande, idPiece) = CreerCommande(montantCouture);

            // Payer le montant RÉEL en base (montantCouture + _counter interne du helper)
            using (var ctx = _factory.CreateDbContext())
            {
                var montantReel = ctx.PiecesCommande
                    .Where(p => p.IdCommande == idCommande)
                    .Sum(p => p.MontantCouture);
                Payer(idCommande, montantReel);
            }

            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Livree",   IdBoss, "Mamadou DIALLO");

            var rapport = _tresorerieService.VerifierCoherenceArgentTravail(Debut, Fin);

            Assert.That(rapport, Is.Not.Null);
            Assert.That(rapport.CommandesNonSoldees, Is.Empty,
                "Commande soldée à 100% : aucune commande non soldée dans le rapport");
            Assert.That(rapport.CoherenceOk, Is.True,
                "Bilan cohérent : aucun écart significatif");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private int _counter = 0;

        private (int idCommande, int idPiece) CreerCommande(decimal montantCouture)
        {
            _counter++;
            var cmd = new Commande { IdClient = IdClient, DateFin = DateTime.Today.AddDays(7) };
            var pce = new PieceCommande
            {
                TypeVetement   = "Robe",
                MontantCouture = montantCouture + _counter, // éviter l'anti-doublon
                IdCouturier    = IdCouturier
            };
            _commandeService.Ajouter(cmd, pce, new List<Mesure>(), IdBoss, "Mamadou DIALLO");
            return (cmd.IdCommande, pce.IdPieceCommande);
        }

        private int Payer(int idCommande, decimal montant)
        {
            var paiement = new Paiement
            {
                IdCommande   = idCommande,
                MontantPaye  = montant,
                DatePaiement = DateTime.Today,
                ModePaiement = "Espèces"
            };
            _paiementService.Ajouter(paiement, IdBoss, "Mamadou DIALLO");
            return paiement.IdPaiement;
        }
    }

    internal class TrDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public TrDbContextFactory(DbContextOptions<ApplicationDbContext> options) => _options = options;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
    }
}
