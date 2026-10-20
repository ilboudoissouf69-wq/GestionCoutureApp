using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

// ============================================================================
// ÉTAPE 8 — Grand test d'intégration : trésorerie et commissions sur scénario complet
// ============================================================================
//
// RÈGLES MÉTIER EXTRAITES DU CODE DE PRODUCTION
// ──────────────────────────────────────────────────────────────────────────
//
// 1. BASE DE CALCUL DES COMMISSIONS
//    - Commission calculée sur PieceCommande.MontantCouture UNIQUEMENT
//      (jamais sur les matériaux)
//    - Filtre : pièces avec (Statut == "Terminee" || "Livree")
//               ET DateTerminee != null
//               ET DateTerminee dans la période
//               ET IdCommission == null (non déjà commissionnée)
//               ET aucun retour actif (Signalé/En reprise)
//    - DateTerminee est posée par le noyau via IClock.UtcNow au premier
//      passage vers Terminee ou Livree ; effacée si retour arrière.
//
// 2. FORMULE COMMISSION
//    Commission = Round(BaseMontant × (pourcentage / 100), 0, AwayFromZero)
//
// 3. FORMULE BÉNÉFICE (DepenseService.ObtenirStats / TresorerieService)
//    BénéficeNet = CA encaissé − commissions − charges d'exploitation
//    Charges     = TotalDépensesValidées + SalaireSecrétaire proratisé
//    Les matériaux ne sont JAMAIS déduits du bénéfice.
//    Formule bilan (TresorerieService) : BilanNet = CA − dépenses − commissions − primes
//
// 4. IDEMPOTENCE
//    - TransitionnerStatutPieceCoeur : si ancien statut == nouveau statut → no-op (return null)
//    - EnregistrerCommissions : IdCommission != null ⟹ conflit détecté ⟹ exception
//
// 5. RETOURS
//    - Un retour n'affecte pas le CA ni les montants de commission déjà enregistrés
//    - RetoursImputes++ sur IdCouturier (responsable du défaut)
//    - ReprisesEffectuees++ sur IdCouturierReprise (ou IdCouturier si même personne)
//    - Une pièce avec retour actif (Signalé/En reprise) est EXCLUE du calcul d'aperçu
//
// SCÉNARIO — calcul des valeurs attendues
// ──────────────────────────────────────────────────────────────────────────
//
// Taux commission : 30% (paramètre mock), sur MontantCouture TOTAL (pas encaissé)
// SalaireMensuelSecrétaire mock : 0 FCFA (pour simplifier le prorata, isoler les charges)
//   → On utilise 0 pour éviter toute ambiguïté de prorata dans les assertions chiffrées.
//   Les tests qui vérifient le bénéfice calculent explicitement le prorata attendu.
//
// C1 : 3 pantalons × 5 000 = 15 000 FCFA, couturier A
//   Paiements : 5 000 + 10 000 = 15 000  → solde = 0
//   Terminée → Livrée
//   Commission A (C1) = Round(15 000 × 0,30) = 4 500
//
// C2 : 1 robe 20 000, couturier B
//   Acompte 8 000. Tentative livraison : refusée (reste 12 000). Paiement 12 000 → Livrée.
//   Commission B (C2) = Round(20 000 × 0,30) = 6 000
//
// C3 : 2 chemises × 4 000 = 8 000, couturier A
//   Acompte 4 000. En cours → aucune commission attendue pour C3.
//
// C4 : 1 costume 30 000, couturier B. Payé intégralement. Terminée.
//   Tentative rétrogradation Boss : refusée (IdCommission != null sans motif).
//   Boss annule commission + rétrogue → repasse Terminée → 1 seule commission.
//   Commission B (C4) = Round(30 000 × 0,30) = 9 000
//
// C5 : Paiement 6 000 puis annulé (net = 0). Commande créée puis annulée < 10 min, aucun paiement.
//   Aucun effet sur CA ni commissions.
//
// Retour : 1 pantalon de C1 retourné. Responsable A, reprise par B. Reprise gratuite.
//   → A : RetoursImputes = 1, B : ReprisesEffectuees = 1
//   → Aucun effet sur CA ni commissions.
//
// Dépenses : 3 000 + 2 000 = 5 000 (toutes validées)
//
// TOTAUX ATTENDUS :
//   CA encaissé de la période :
//     C1 = 5 000 + 10 000 = 15 000
//     C2 = 8 000 + 12 000 = 20 000
//     C3 = 4 000
//     C4 = 30 000
//     C5 = 6 000 puis annulé = 0
//     ─────────────────────────────
//     Total = 15 000 + 20 000 + 4 000 + 30 000 = 69 000
//
//   Restes à payer par commande :
//     C1 = 0 (solde)
//     C2 = 0 (solde après 2e paiement)
//     C3 = 8 000 − 4 000 = 4 000
//     C4 = 0 (payé intégralement)
//
//   Commissions versées :
//     Couturier A (C1)   = 4 500  (pièces terminées dans la période, versées)
//     Couturier B (C2)   = 6 000  (pièce terminée dans la période, versée)
//     Couturier B (C4)   = 9 000  (après annulation et re-versement, 1 seule commission)
//     Total commissions  = 4 500 + 6 000 + 9 000 = 19 500
//
//   Dépenses = 3 000 + 2 000 = 5 000
//
//   BilanNet (TresorerieService) = CA − dépenses − commissions − primes
//                                = 69 000 − 5 000 − 19 500 − 0 = 44 500
//
// ============================================================================

namespace GestionCoutureApp.Tests
{
    [TestFixture]
    public class Etape8_ScenarioCompletTests
    {
        // ── Constantes employés ──────────────────────────────────────────────────
        private const int IdBoss        = 1;
        private const int IdSecretaire  = 2;
        private const int IdCouturierA  = 3;
        private const int IdCouturierB  = 4;
        private const int IdClient1     = 10;
        private const int IdClient2     = 11;
        private const int IdClient3     = 12;
        private const int IdClient4     = 13;
        private const int IdClient5     = 14;

        // Taux commission et seuil prime (mockés à 0 prime pour simplifier)
        private const decimal TauxCommission = 30m; // 30%
        // Seuil prime = 100 commandes → jamais atteint → prime = 0

        // ── Infrastructure SQLite ────────────────────────────────────────────────
        private SqliteConnection _connexion = null!;
        private IDbContextFactory<ApplicationDbContext> _factory = null!;

        // ── Services ─────────────────────────────────────────────────────────────
        private CommandeService   _commandeService   = null!;
        private PaiementService   _paiementService   = null!;
        private CommissionService _commissionService = null!;
        private TresorerieService _tresorerieService = null!;
        private DepenseService    _depenseService    = null!;
        private RetourService     _retourService     = null!;

        // ── Horloge fictive ──────────────────────────────────────────────────────
        private FakeClock8 _clock = null!;

        // Période = mois courant (octobre 2026 d'après le contexte de session)
        private DateTime _debut;
        private DateTime _fin;

        // ─────────────────────────────────────────────────────────────────────────
        // SETUP — SQLite en mémoire RÉEL, connexion gardée ouverte, schéma EnsureCreated
        // ─────────────────────────────────────────────────────────────────────────
        [SetUp]
        public void SetUp()
        {
            CommandeService.ViderCacheDoublonPourTests();

            _connexion = new SqliteConnection("DataSource=:memory:");
            _connexion.Open();

            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connexion)
                .Options;

            _factory = new E8DbContextFactory(opts);

            // Créer le schéma (migrations/EnsureCreated)
            using var ctx = _factory.CreateDbContext();
            ctx.Database.EnsureCreated();

            // Horloge fictive fixée en milieu du mois courant
            DateTime now = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 15, 10, 0, 0);
            _clock = new FakeClock8(now);

            // Période = premier → dernier jour du mois courant
            _debut = new DateTime(now.Year, now.Month, 1);
            _fin   = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));

            // Services (NullLogger pour garder les tests silencieux)
            var logFactory = NullLoggerFactory.Instance;
            var paramsMock = new E8ParametresServiceMock(
                tauxCommissionDefaut: TauxCommission,
                seuilPrimeNbCommandes: 100,     // seuil très élevé → prime = 0
                montantPrimeQualite: 5000m,
                salaireSecretaire: 0m);         // 0 pour simplifier le prorata dans les assertions

            _commandeService   = new CommandeService(_factory,
                new Microsoft.Extensions.Logging.Logger<CommandeService>(logFactory),
                auditService: TestDbHelper.NoOpAudit, clock: _clock);
            _paiementService   = new PaiementService(_factory,
                new Microsoft.Extensions.Logging.Logger<PaiementService>(logFactory),
                clock: _clock);
            _commissionService = new CommissionService(_factory,
                new Microsoft.Extensions.Logging.Logger<CommissionService>(logFactory),
                paramsMock);
            _tresorerieService = new TresorerieService(_factory,
                new Microsoft.Extensions.Logging.Logger<TresorerieService>(logFactory));
            _depenseService    = new DepenseService(_factory);
            _retourService     = new RetourService(_factory, new AuditServiceStub());

            // Peupler les référentiels
            using var seed = _factory.CreateDbContext();
            seed.Employes.AddRange(
                new Employe { IdEmploye = IdBoss,       Nom = "DIALLO",   Prenom = "Boss",
                    Identifiant = "boss",  MotDePasse = "x", Role = "Boss",       Statut = "Actif" },
                new Employe { IdEmploye = IdSecretaire, Nom = "FALL",     Prenom = "Secrétaire",
                    Identifiant = "sec",   MotDePasse = "x", Role = "Secretaire", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturierA, Nom = "KONE",     Prenom = "Awa",
                    Identifiant = "couta", MotDePasse = "x", Role = "Couturier",  Statut = "Actif" },
                new Employe { IdEmploye = IdCouturierB, Nom = "TRAORE",   Prenom = "Bakary",
                    Identifiant = "coutb", MotDePasse = "x", Role = "Couturier",  Statut = "Actif" }
            );
            seed.Clients.AddRange(
                new Client { IdClient = IdClient1, Nom = "CLIENT1", Prenom = "C1", Telephone = "771000001" },
                new Client { IdClient = IdClient2, Nom = "CLIENT2", Prenom = "C2", Telephone = "771000002" },
                new Client { IdClient = IdClient3, Nom = "CLIENT3", Prenom = "C3", Telephone = "771000003" },
                new Client { IdClient = IdClient4, Nom = "CLIENT4", Prenom = "C4", Telephone = "771000004" },
                new Client { IdClient = IdClient5, Nom = "CLIENT5", Prenom = "C5", Telephone = "771000005" }
            );
            seed.SaveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _connexion.Close();
            _connexion.Dispose();
        }

        // =====================================================================
        // TEST PRINCIPAL — Scénario complet
        // =====================================================================

        [Test]
        public void E8_01_Scenario_Complet_Toutes_Verifications()
        {
            // ── C1 : 3 pantalons × 5 000 = 15 000, couturier A ───────────────────
            // Paiements : 5 000 + 10 000. Terminée → Livrée.

            var cmdC1 = new Commande { IdClient = IdClient1, DateFin = _fin };
            var p1_C1 = new PieceCommande { TypeVetement = "Pantalon 1", MontantCouture = 5000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmdC1, p1_C1, new(), IdBoss, "Boss DIALLO");

            // Ajouter les 2 autres pièces via AjouterPiece (simulation de l'UI)
            // On les insère directement dans la DB pour contourner les règles d'ajout après paiement
            // (les pièces sont créées avant tout paiement, comme dans la vraie UI de création)
            // En réalité, le scénario dit "3 pantalons" créés ensemble dans une seule commande.
            // On utilise piecesSupplementaires de CommandeService.Ajouter.
            // → On recrée la commande avec toutes les pièces d'un coup.
            CommandeService.ViderCacheDoublonPourTests();
            cmdC1 = new Commande { IdClient = IdClient1, DateFin = _fin };
            var p1a = new PieceCommande { TypeVetement = "Pantalon 1", MontantCouture = 5000m, IdCouturier = IdCouturierA };
            var p1b = new PieceCommande { TypeVetement = "Pantalon 2", MontantCouture = 5000m, IdCouturier = IdCouturierA };
            var p1c = new PieceCommande { TypeVetement = "Pantalon 3", MontantCouture = 5000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmdC1, p1a,
                new List<Mesure>(), IdBoss, "Boss DIALLO",
                materiaux: null,
                piecesSupplementaires: new List<(PieceCommande, List<Mesure>, List<MaterielSupplement>?)>
                {
                    (p1b, new List<Mesure>(), null),
                    (p1c, new List<Mesure>(), null),
                });

            // Paiement 1 : 5 000
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmdC1.IdCommande,
                MontantPaye = 5000m,
                DatePaiement = _clock.Now,
                ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");

            // Passer En cours puis Terminée (les 3 pièces)
            _commandeService.TransitionnerStatutPiece(p1a.IdPieceCommande, "En cours",  IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p1b.IdPieceCommande, "En cours",  IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p1c.IdPieceCommande, "En cours",  IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p1a.IdPieceCommande, "Terminee",  IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p1b.IdPieceCommande, "Terminee",  IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p1c.IdPieceCommande, "Terminee",  IdBoss, "Boss DIALLO", null);

            // Paiement 2 : 10 000 → solde = 0
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmdC1.IdCommande,
                MontantPaye = 10000m,
                DatePaiement = _clock.Now,
                ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");

            // Livraison des 3 pièces (soldée → OK)
            _commandeService.TransitionnerStatutPiece(p1a.IdPieceCommande, "Livree", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p1b.IdPieceCommande, "Livree", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p1c.IdPieceCommande, "Livree", IdBoss, "Boss DIALLO", null);

            // ── C2 : 1 robe 20 000, couturier B ──────────────────────────────────
            CommandeService.ViderCacheDoublonPourTests();
            var cmdC2 = new Commande { IdClient = IdClient2, DateFin = _fin };
            var pC2   = new PieceCommande { TypeVetement = "Robe", MontantCouture = 20000m, IdCouturier = IdCouturierB };
            _commandeService.Ajouter(cmdC2, pC2, new(), IdBoss, "Boss DIALLO");

            // Acompte 8 000
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmdC2.IdCommande,
                MontantPaye = 8000m,
                DatePaiement = _clock.Now,
                ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");

            // Passage En cours → Terminée
            _commandeService.TransitionnerStatutPiece(pC2.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(pC2.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);

            // Tentative de livraison refusée (reste 12 000, pas Boss avec motif)
            var exLivraisonC2 = Assert.Throws<LivraisonNonSoldeeException>(() =>
                _commandeService.TransitionnerStatutPiece(pC2.IdPieceCommande, "Livree",
                    IdSecretaire, "Secrétaire FALL", null));
            Assert.That(exLivraisonC2, Is.Not.Null, "La livraison non soldée doit être refusée à la Secrétaire");

            // Paiement du solde : 12 000
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmdC2.IdCommande,
                MontantPaye = 12000m,
                DatePaiement = _clock.Now,
                ModePaiement = "Mobile Money"
            }, IdBoss, "Boss DIALLO");

            // Livraison OK maintenant
            _commandeService.TransitionnerStatutPiece(pC2.IdPieceCommande, "Livree", IdBoss, "Boss DIALLO", null);

            // ── C3 : 2 chemises × 4 000 = 8 000, couturier A, en cours ───────────
            CommandeService.ViderCacheDoublonPourTests();
            var cmdC3 = new Commande { IdClient = IdClient3, DateFin = _fin };
            var p3a   = new PieceCommande { TypeVetement = "Chemise 1", MontantCouture = 4000m, IdCouturier = IdCouturierA };
            var p3b   = new PieceCommande { TypeVetement = "Chemise 2", MontantCouture = 4000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmdC3, p3a, new(), IdBoss, "Boss DIALLO", null,
                new List<(PieceCommande, List<Mesure>, List<MaterielSupplement>?)>
                {
                    (p3b, new List<Mesure>(), null)
                });

            // Acompte 4 000
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmdC3.IdCommande,
                MontantPaye = 4000m,
                DatePaiement = _clock.Now,
                ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");

            // Reste En cours — pièces non terminées
            _commandeService.TransitionnerStatutPiece(p3a.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p3b.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);

            // ── C4 : 1 costume 30 000, couturier B ───────────────────────────────
            CommandeService.ViderCacheDoublonPourTests();
            var cmdC4 = new Commande { IdClient = IdClient4, DateFin = _fin };
            var pC4   = new PieceCommande { TypeVetement = "Costume", MontantCouture = 30000m, IdCouturier = IdCouturierB };
            _commandeService.Ajouter(cmdC4, pC4, new(), IdBoss, "Boss DIALLO");

            // Paiement intégral 30 000
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmdC4.IdCommande,
                MontantPaye = 30000m,
                DatePaiement = _clock.Now,
                ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");

            // Passage En cours → Terminée
            _commandeService.TransitionnerStatutPiece(pC4.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(pC4.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);

            // ── C4 : Versement de commission (Boss) ──────────────────────────────
            var apercuC4 = _commissionService.CalculerApercu(_debut, _fin, TauxCommission,
                surMontantEncaisse: false, idCouturierFiltre: IdCouturierB);
            // Doit contenir couturier B pour le costume de C4
            Assert.That(apercuC4.Any(a => a.IdEmploye == IdCouturierB), Is.True,
                "Couturier B doit apparaître dans l'aperçu après C4 terminée");
            _commissionService.EnregistrerCommissions(apercuC4, _debut, _fin,
                TauxCommission, surMontantEncaisse: false,
                idOperateur: IdBoss, nomOperateur: "Boss DIALLO");

            // Récupérer l'id de la commission de B
            List<Commission> commissionsB;
            using (var ctx = _factory.CreateDbContext())
                commissionsB = ctx.Commissions.Where(c => c.IdEmploye == IdCouturierB && !c.EstAnnulee).ToList();
            int idCommissionB_C4 = commissionsB.Single().IdCommission;

            // ── C4 : Tentative de rétrogradation SANS annuler la commission ──────
            // Doit échouer car pièce commissionnée et pas de CommissionService fourni
            var exRetrogradeC4 = Assert.Throws<CommandeService.PieceCommissionneeException>(() =>
                _commandeService.TransitionnerStatutPiece(
                    pC4.IdPieceCommande, "En cours",
                    IdBoss, "Boss DIALLO", "test sans commissionService"));
            Assert.That(exRetrogradeC4, Is.Not.Null,
                "La rétrogradation sans annuler la commission doit être refusée");

            // ── C4 : Annulation de la commission + rétrogradation atomique ───────
            // On passe commissionService → annulation atomique dans la même transaction
            _commandeService.TransitionnerStatutPiece(
                pC4.IdPieceCommande, "En cours",
                IdBoss, "Boss DIALLO", "correction scénario",
                _commissionService);

            // Vérifier que la commission est annulée
            using (var ctx = _factory.CreateDbContext())
            {
                var comm = ctx.Commissions.Find(idCommissionB_C4);
                Assert.That(comm!.EstAnnulee, Is.True, "La commission B doit être annulée après la rétrogradation");
                var pieceDb = ctx.PiecesCommande.Find(pC4.IdPieceCommande);
                Assert.That(pieceDb!.IdCommission, Is.Null, "IdCommission doit être null après annulation");
                Assert.That(pieceDb.Statut, Is.EqualTo("En cours"), "Statut doit être En cours après rétrogradation");
            }

            // ── C4 : Repasse en Terminée → commission re-versée UNE SEULE FOIS ──
            _commandeService.TransitionnerStatutPiece(pC4.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);

            // Re-calculer et re-verser la commission
            var apercuC4b = _commissionService.CalculerApercu(_debut, _fin, TauxCommission,
                surMontantEncaisse: false, idCouturierFiltre: IdCouturierB);
            Assert.That(apercuC4b.Any(a => a.IdEmploye == IdCouturierB), Is.True,
                "Couturier B doit réapparaître dans l'aperçu après re-terminaison de C4");
            _commissionService.EnregistrerCommissions(apercuC4b, _debut, _fin,
                TauxCommission, surMontantEncaisse: false,
                idOperateur: IdBoss, nomOperateur: "Boss DIALLO");

            // ── C5 : Paiement 6 000 puis annulé, commande annulée < 10 min ───────
            CommandeService.ViderCacheDoublonPourTests();
            var cmdC5 = new Commande { IdClient = IdClient5, DateFin = _fin };
            var pC5   = new PieceCommande { TypeVetement = "Boubou", MontantCouture = 10000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmdC5, pC5, new(), IdSecretaire, "Secrétaire FALL");

            // Paiement 6 000
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmdC5.IdCommande,
                MontantPaye = 6000m,
                DatePaiement = _clock.Now,
                ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");

            // Annuler le paiement (Boss)
            using (var ctx = _factory.CreateDbContext())
            {
                var paiC5 = ctx.Paiements
                    .Where(p => p.IdCommande == cmdC5.IdCommande && !p.EstAnnule)
                    .Single();
                _paiementService.Annuler(paiC5.IdPaiement, "annulation test C5",
                    IdBoss, "Boss DIALLO");
            }

            // Commande annulée par la Secrétaire (< 10 min, aucun paiement actif)
            // La commande a un paiement ANNULÉ → annulParSecretaire doit vérifier
            // les paiements VALIDES seulement (selon la règle AnnulerParSecretaireAsync)
            // Note : AnnulerParSecretaireAsync vérifie "aucun paiement (même annulé)"
            // → avec un paiement annulé, l'annulation rapide est interdite.
            // La suppression Boss est AUSSI refusée : une commande ayant un paiement
            // (même annulé) doit rester pour la traçabilité comptable (cohérent avec
            // E8_CL7). C5 reste donc en base, mais son paiement annulé (net = 0) et sa
            // pièce « A faire » n'ont AUCUN effet sur le CA ni les commissions
            // (cf. assertions a/d/f plus bas).
            Assert.Throws<InvalidOperationException>(() =>
                _commandeService.SupprimerAsync(cmdC5.IdCommande, IdBoss, "Boss DIALLO", "scénario C5 annulé")
                    .GetAwaiter().GetResult(),
                "C5 : suppression refusée car un paiement (même annulé) est rattaché (traçabilité, cf. E8_CL7)");

            // ── Versement des commissions de A (C1) et B (C2) ───────────────────
            // C3 reste En cours → aucune commission pour A sur C3
            var apercuAB = _commissionService.CalculerApercu(_debut, _fin, TauxCommission,
                surMontantEncaisse: false, idCouturierFiltre: null);

            // Vérifier que C3 ne génère PAS de commission pour A
            var ligneA = apercuAB.FirstOrDefault(a => a.IdEmploye == IdCouturierA);
            if (ligneA != null)
            {
                // Les pièces dans l'aperçu de A ne doivent PAS inclure les pièces de C3
                bool c3DansApercu = ligneA.IdsCommandes.Contains(cmdC3.IdCommande);
                Assert.That(c3DansApercu, Is.False,
                    "i) C3 est En cours → aucune commission pour A sur C3");
            }
            // A a des pièces terminées (C1), donc doit apparaître
            Assert.That(ligneA, Is.Not.Null, "Couturier A doit avoir des pièces commissionables (C1)");

            _commissionService.EnregistrerCommissions(apercuAB, _debut, _fin,
                TauxCommission, surMontantEncaisse: false,
                idOperateur: IdBoss, nomOperateur: "Boss DIALLO");

            // ── Retour : un pantalon de C1 retourné ──────────────────────────────
            // Responsable A, reprise par B, reprise gratuite, passage à Terminé
            var retour = new Retour
            {
                IdCommande              = cmdC1.IdCommande,
                IdPieceCommande         = p1a.IdPieceCommande,
                IdCouturier             = IdCouturierA,    // responsable du défaut
                IdCouturierReprise      = IdCouturierB,    // fait la reprise
                DescriptionProbleme     = "Couture de la jambe",
                IdOperateurEnregistrement = IdBoss,
                NomOperateurEnregistrement = "Boss DIALLO"
            };
            _retourService.Ajouter(retour);
            _retourService.DemarrerReprise(retour.IdRetour, IdBoss, "Boss DIALLO");
            _retourService.Resoudre(retour.IdRetour, IdBoss, "Boss DIALLO");

            // ── Dépenses : 3 000 et 2 000 ────────────────────────────────────────
            _depenseService.Ajouter(new Depense
            {
                TypeDepense = "Matériaux", Montant = 3000m,
                DateDepense = _clock.Now, Categorie = "Matériaux",
                StatutValidation = "En attente", IdOperateur = IdBoss, NomOperateur = "Boss DIALLO"
            });
            _depenseService.Ajouter(new Depense
            {
                TypeDepense = "Loyer", Montant = 2000m,
                DateDepense = _clock.Now, Categorie = "Charges",
                StatutValidation = "En attente", IdOperateur = IdBoss, NomOperateur = "Boss DIALLO"
            });
            // Validation des dépenses par le Boss
            _depenseService.Valider(1, "Boss DIALLO");
            _depenseService.Valider(2, "Boss DIALLO");

            // ─────────────────────────────────────────────────────────────────────
            // VÉRIFICATIONS
            // ─────────────────────────────────────────────────────────────────────

            // a) CA encaissé de la période
            //    C1=15 000 + C2=20 000 + C3=4 000 + C4=30 000 + C5=6000 annulé=0
            //    = 15 000 + 20 000 + 4 000 + 30 000 = 69 000
            decimal caAttendu = 15_000m + 20_000m + 4_000m + 30_000m; // = 69 000
            decimal caObtenu  = _tresorerieService.CalculerChiffreAffaires(_debut, _fin);
            Assert.That(caObtenu, Is.EqualTo(caAttendu),
                $"a) CA encaissé attendu = {caAttendu:N0}, obtenu = {caObtenu:N0}");

            // b) Restes à payer par commande
            //    C1 = 0 (solde : 15 000 payés sur 15 000)
            //    C2 = 0 (solde : 20 000 payés sur 20 000)
            //    C3 = 4 000 (8 000 dus − 4 000 encaissé)
            //    C4 = 0 (30 000 payés sur 30 000)
            RechargerCommande(cmdC1.IdCommande, out decimal resteC1);
            RechargerCommande(cmdC2.IdCommande, out decimal resteC2);
            RechargerCommande(cmdC3.IdCommande, out decimal resteC3);
            RechargerCommande(cmdC4.IdCommande, out decimal resteC4);

            Assert.That(resteC1, Is.EqualTo(0m),    "b) C1 reste à payer = 0 (solde)");
            Assert.That(resteC2, Is.EqualTo(0m),    "b) C2 reste à payer = 0 (solde)");
            Assert.That(resteC3, Is.EqualTo(4000m), "b) C3 reste à payer = 8 000 − 4 000 = 4 000");
            Assert.That(resteC4, Is.EqualTo(0m),    "b) C4 reste à payer = 0 (solde)");

            // c) Commissions de A et B (versées)
            //    Couturier A (3 pantalons C1) = Round(15 000 × 30%) = 4 500
            //    Couturier B (robe C2)        = Round(20 000 × 30%) = 6 000
            //    Couturier B (costume C4)     = Round(30 000 × 30%) = 9 000
            //    Total B                      = 6 000 + 9 000 = 15 000
            decimal commAAttendue = 4_500m;   // Round(15 000 × 0,30) = 4 500
            decimal commBAttendue = 15_000m;  // Round(20 000 × 0,30) + Round(30 000 × 0,30) = 6 000 + 9 000

            using (var ctx = _factory.CreateDbContext())
            {
                decimal commAObtenue = ctx.Commissions
                    .Where(c => c.IdEmploye == IdCouturierA && !c.EstAnnulee)
                    .AsEnumerable()
                    .Sum(c => c.MontantCommission);
                decimal commBObtenue = ctx.Commissions
                    .Where(c => c.IdEmploye == IdCouturierB && !c.EstAnnulee)
                    .AsEnumerable()
                    .Sum(c => c.MontantCommission);

                Assert.That(commAObtenue, Is.EqualTo(commAAttendue),
                    $"c) Commission A attendue = {commAAttendue:N0}, obtenue = {commAObtenue:N0}");
                Assert.That(commBObtenue, Is.EqualTo(commBAttendue),
                    $"c) Commission B attendue = {commBAttendue:N0}, obtenue = {commBObtenue:N0}");
            }

            // d) Total commissions = 4 500 + 15 000 = 19 500
            // Note : la commission de B sur C4 a été annulée puis re-versée → 1 seule non annulée
            decimal totalCommAttendu = 4_500m + 6_000m + 9_000m; // = 19 500
            decimal totalCommObtenu  = _tresorerieService.CalculerTotalCommissions(_debut, _fin);
            Assert.That(totalCommObtenu, Is.EqualTo(totalCommAttendu),
                $"d) Total commissions attendu = {totalCommAttendu:N0}, obtenu = {totalCommObtenu:N0}");

            // e) Total dépenses = 3 000 + 2 000 = 5 000
            decimal depAttendu = 3_000m + 2_000m; // = 5 000
            decimal depObtenu  = _tresorerieService.CalculerTotalDepenses(_debut, _fin);
            Assert.That(depObtenu, Is.EqualTo(depAttendu),
                $"e) Total dépenses attendu = {depAttendu:N0}, obtenu = {depObtenu:N0}");

            // f) Bénéfice (BilanNet)
            //    Aucune commande n'a de matériaux dans ce scénario
            //    → CaEncaisseCouture = CA encaissé = 69 000
            //    BilanNet = CaEncaisseCouture − commissions − primes − dépenses
            //             = 69 000 − 19 500 − 0 − 5 000 = 44 500
            //    (SalaireSecrétaire = 0 dans notre mock, primes = 0 car seuil=100)
            decimal bilanAttendu = caAttendu - totalCommAttendu - 0m - depAttendu; // = 44 500
            var bilan = _tresorerieService.CalculerBilan(_debut, _fin);
            Assert.That(bilan.BilanNet, Is.EqualTo(bilanAttendu),
                $"f) BilanNet attendu = {bilanAttendu:N0}, obtenu = {bilan.BilanNet:N0}");
            Assert.That(bilan.ChiffreAffaires,   Is.EqualTo(caAttendu),        "f) CA dans bilan");
            Assert.That(bilan.TotalDepenses,     Is.EqualTo(depAttendu),       "f) Dépenses dans bilan");
            Assert.That(bilan.TotalCommissions,  Is.EqualTo(totalCommAttendu), "f) Commissions dans bilan");
            Assert.That(bilan.TotalPrimesQualite, Is.EqualTo(0m),              "f) Primes = 0 (seuil non atteint)");

            // g) VerifierCoherenceArgentTravail : écart acceptable (non nul mais cohérent)
            var coherence = _tresorerieService.VerifierCoherenceArgentTravail(_debut, _fin);
            // Les paiements valides = 69 000
            // Travail effectué (pièces Terminée/Livrée) : C1=15 000, C2=20 000, C4=30 000 = 65 000
            // (C3 est "En cours" donc non comptée, C5 paiement annulé donc non comptée)
            // Écart = paiements valides − total facturé pièces terminées/livrées
            // = 69 000 − 65 000 = 4 000 (acompte C3 sans pièces terminées)
            // CoherenceOk = écart < 15% de totalFacture
            Assert.That(coherence.TotalPaiementsValides, Is.EqualTo(69_000m),
                "g) Paiements valides dans rapport de cohérence");
            // On vérifie simplement que le rapport s'exécute sans erreur
            Assert.That(coherence.Diagnostic, Is.Not.Null.And.Not.Empty,
                "g) VerifierCoherenceArgentTravail doit retourner un diagnostic");

            // h) Compteurs retours de A et B
            //    A : 1 retour imputé (responsable du défaut)
            //    B : 1 reprise effectuée
            var statsRetours = _retourService.StatistiquesParCouturier(_debut, _fin);
            var statsA = statsRetours.FirstOrDefault(s => s.IdCouturier == IdCouturierA);
            var statsB = statsRetours.FirstOrDefault(s => s.IdCouturier == IdCouturierB);
            Assert.That(statsA?.RetoursImputes ?? 0, Is.EqualTo(1),
                "h) Couturier A doit avoir 1 retour imputé");
            Assert.That(statsB?.ReprisesEffectuees ?? 0, Is.EqualTo(1),
                "h) Couturier B doit avoir 1 reprise effectuée");

            // i) Aucune commission pour C3 (pièces restées En cours)
            //    Déjà vérifié ci-dessus (IdsCommandes ne contient pas cmdC3.IdCommande)
            using (var ctx = _factory.CreateDbContext())
            {
                bool c3ACommission = ctx.PiecesCommande
                    .Where(p => p.IdCommande == cmdC3.IdCommande)
                    .Any(p => p.IdCommission != null);
                Assert.That(c3ACommission, Is.False,
                    "i) Aucune pièce de C3 ne doit avoir une commission (En cours)");
            }

            // j) Somme des commissions versées = somme des lignes Commission en base
            //    Le total TresorerieService.CalculerTotalCommissions doit correspondre
            //    à la somme directe des lignes en base
            using (var ctx = _factory.CreateDbContext())
            {
                decimal sommeLignesBase = ctx.Commissions
                    .Where(c => !c.EstAnnulee
                             && c.DateCalcul.Date >= _debut.Date
                             && c.DateCalcul.Date <= _fin.Date)
                    .AsEnumerable()
                    .Sum(c => c.MontantCommission + c.PrimeQualite);
                Assert.That(totalCommObtenu, Is.EqualTo(sommeLignesBase),
                    "j) TresorerieService.CalculerTotalCommissions == somme des lignes Commission en base");
            }

            Console.WriteLine("=== RÉCAPITULATIF SCÉNARIO COMPLET ===");
            Console.WriteLine($"a) CA encaissé         : attendu={caAttendu:N0}, obtenu={caObtenu:N0}");
            Console.WriteLine($"b) Reste C1/C2/C3/C4   : {resteC1:N0}/{resteC2:N0}/{resteC3:N0}/{resteC4:N0}");
            Console.WriteLine($"c) Commission A        : attendu={commAAttendue:N0}");
            Console.WriteLine($"c) Commission B        : attendu={commBAttendue:N0}");
            Console.WriteLine($"d) Total commissions   : attendu={totalCommAttendu:N0}, obtenu={totalCommObtenu:N0}");
            Console.WriteLine($"e) Total dépenses      : attendu={depAttendu:N0}, obtenu={depObtenu:N0}");
            Console.WriteLine($"f) Bilan net           : attendu={bilanAttendu:N0}, obtenu={bilan.BilanNet:N0}");
            Console.WriteLine($"g) CoherenceOk         : {coherence.CoherenceOk}");
            Console.WriteLine($"h) Retours A           : {statsA?.RetoursImputes ?? 0}, Reprises B : {statsB?.ReprisesEffectuees ?? 0}");
            Console.WriteLine($"i) C3 commission       : aucune ✓");
            Console.WriteLine($"j) Cohérence base      : {sommeLignesBase:N0} == {totalCommObtenu:N0}");
        }

        // =====================================================================
        // CAS LIMITE 1 — Double clic : livrer deux fois, verser deux fois,
        //                passer à Terminée deux fois → effet unique
        // =====================================================================
        [Test]
        public void E8_CL1_DoubleClic_Idempotent()
        {
            CommandeService.ViderCacheDoublonPourTests();
            var cmd = new Commande { IdClient = IdClient1, DateFin = _fin };
            var piece = new PieceCommande { TypeVetement = "Chemise", MontantCouture = 10000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmd, piece, new(), IdBoss, "Boss DIALLO");
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmd.IdCommande, MontantPaye = 10000m,
                DatePaiement = _clock.Now, ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");

            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "En cours",  IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);

            // Double Terminée : idempotent (pas d'exception, DateTerminee conservée)
            DateTime? dtPremiere;
            using (var ctx = _factory.CreateDbContext())
                dtPremiere = ctx.PiecesCommande.Find(piece.IdPieceCommande)!.DateTerminee;
            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);
            using (var ctx = _factory.CreateDbContext())
            {
                var p = ctx.PiecesCommande.Find(piece.IdPieceCommande)!;
                Assert.That(p.DateTerminee, Is.EqualTo(dtPremiere),
                    "CL1 Double Terminée : DateTerminee ne doit pas changer");
            }

            // Double Livrée : idempotent
            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "Livree", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "Livree", IdBoss, "Boss DIALLO", null);

            // Double versement commission : le second doit échouer (conflit)
            var apercu1 = _commissionService.CalculerApercu(_debut, _fin, TauxCommission, false, null);
            _commissionService.EnregistrerCommissions(apercu1, _debut, _fin,
                TauxCommission, false, IdBoss, "Boss DIALLO");

            // Re-calculer : pièces déjà commissionnées → aperçu vide pour ce couturier
            var apercu2 = _commissionService.CalculerApercu(_debut, _fin, TauxCommission, false, IdCouturierA);
            Assert.That(apercu2.Count, Is.EqualTo(0),
                "CL1 Double versement : aperçu vide car pièce déjà commissionnée");

            // Une seule commission en base (non annulée)
            using (var ctx = _factory.CreateDbContext())
            {
                int nb = ctx.Commissions.Count(c => c.IdEmploye == IdCouturierA && !c.EstAnnulee);
                Assert.That(nb, Is.EqualTo(1), "CL1 Une seule commission non annulée en base");
            }
        }

        // =====================================================================
        // CAS LIMITE 2 — Paiement supérieur au reste à payer : refusé
        // =====================================================================
        [Test]
        public void E8_CL2_Paiement_Superieur_ResteAPayer_Refuse()
        {
            CommandeService.ViderCacheDoublonPourTests();
            var cmd = new Commande { IdClient = IdClient1, DateFin = _fin };
            var piece = new PieceCommande { TypeVetement = "Robe", MontantCouture = 10000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmd, piece, new(), IdBoss, "Boss DIALLO");
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmd.IdCommande, MontantPaye = 8000m,
                DatePaiement = _clock.Now, ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");

            // Reste = 2 000. Tentative de paiement de 3 000 → refusé
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _paiementService.Ajouter(new Paiement
                {
                    IdCommande = cmd.IdCommande, MontantPaye = 3000m,
                    DatePaiement = _clock.Now, ModePaiement = "Espèces"
                }, IdBoss, "Boss DIALLO"));
            Assert.That(ex!.Message, Does.Contain("dépasse"),
                "CL2 Le message d'erreur doit mentionner le dépassement");

            // CA encaissé = 8 000 (pas 11 000)
            decimal ca = _tresorerieService.CalculerChiffreAffaires(_debut, _fin);
            Assert.That(ca, Is.EqualTo(8000m), "CL2 CA encaissé = 8 000 (paiement trop élevé refusé)");
        }

        // =====================================================================
        // CAS LIMITE 3 — Annulation d'un paiement sur une commande déjà livrée
        //               Règle : bloquée si commission non annulée
        // =====================================================================
        [Test]
        public void E8_CL3_AnnulationPaiement_CommandeLivree_CommissionActive_Refuse()
        {
            CommandeService.ViderCacheDoublonPourTests();
            var cmd = new Commande { IdClient = IdClient1, DateFin = _fin };
            var piece = new PieceCommande { TypeVetement = "Chemise", MontantCouture = 10000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmd, piece, new(), IdBoss, "Boss DIALLO");

            int idPaiement;
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmd.IdCommande, MontantPaye = 10000m,
                DatePaiement = _clock.Now, ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");
            using (var ctx = _factory.CreateDbContext())
                idPaiement = ctx.Paiements.Where(p => p.IdCommande == cmd.IdCommande).Single().IdPaiement;

            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "Livree",   IdBoss, "Boss DIALLO", null);

            // Enregistrer commission
            var apercu = _commissionService.CalculerApercu(_debut, _fin, TauxCommission, false, null);
            _commissionService.EnregistrerCommissions(apercu, _debut, _fin,
                TauxCommission, false, IdBoss, "Boss DIALLO");

            // Tentative d'annulation du paiement → refusée (commission active)
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _paiementService.Annuler(idPaiement, "test annulation livree",
                    IdBoss, "Boss DIALLO"));
            Assert.That(ex!.Message, Does.Contain("commission").IgnoreCase,
                "CL3 L'annulation de paiement avec commission active doit mentionner la commission");

            // CA toujours cohérent (paiement non annulé)
            decimal ca = _tresorerieService.CalculerChiffreAffaires(_debut, _fin);
            Assert.That(ca, Is.EqualTo(10000m), "CL3 CA = 10 000 (paiement non annulé)");
        }

        // =====================================================================
        // CAS LIMITE 4 — Frontière de période : pièce terminée à 23:59 et 00:01
        //               et fin de mois → compte dans la bonne période
        // =====================================================================
        [Test]
        public void E8_CL4_Frontiere_Periode_DateTerminee()
        {
            // Pièce 1 : terminée à 23:59 le DERNIER jour du mois courant (UTC)
            // → doit être dans la période du mois courant
            DateTime lastDayUtc = new DateTime(_fin.Year, _fin.Month, _fin.Day, 23, 59, 0, DateTimeKind.Utc);
            _clock.SetUtcNow(lastDayUtc);

            CommandeService.ViderCacheDoublonPourTests();
            var cmd1 = new Commande { IdClient = IdClient1, DateFin = _fin };
            var p1   = new PieceCommande { TypeVetement = "Robe", MontantCouture = 5000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmd1, p1, new(), IdBoss, "Boss DIALLO");
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmd1.IdCommande, MontantPaye = 5000m,
                DatePaiement = _clock.Now, ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");
            _commandeService.TransitionnerStatutPiece(p1.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p1.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);

            // Vérifier DateTerminee
            using (var ctx = _factory.CreateDbContext())
            {
                var pDb = ctx.PiecesCommande.Find(p1.IdPieceCommande)!;
                Assert.That(pDb.DateTerminee, Is.Not.Null, "CL4 DateTerminee doit être renseignée");
                Assert.That(pDb.DateTerminee!.Value.Date, Is.EqualTo(lastDayUtc.Date),
                    "CL4 DateTerminee doit être le dernier jour du mois (UTC)");
            }

            // Pièce 2 : terminée à 00:01 le PREMIER jour du mois SUIVANT (UTC)
            // → ne doit PAS être dans la période du mois courant
            DateTime firstDayNextMonthUtc = new DateTime(_fin.Year, _fin.Month, _fin.Day, 23, 59, 0, DateTimeKind.Utc)
                .AddMinutes(2); // = 00:01 le mois suivant si fin de mois
            // Pour un test plus robuste, on calcule explicitement le 1er du mois suivant
            int nextYear  = _fin.Month == 12 ? _fin.Year + 1 : _fin.Year;
            int nextMonth = _fin.Month == 12 ? 1 : _fin.Month + 1;
            DateTime debutMoisSuivantUtc = new DateTime(nextYear, nextMonth, 1, 0, 1, 0, DateTimeKind.Utc);
            _clock.SetUtcNow(debutMoisSuivantUtc);

            CommandeService.ViderCacheDoublonPourTests();
            var cmd2 = new Commande { IdClient = IdClient2, DateFin = _fin.AddMonths(1) };
            var p2   = new PieceCommande { TypeVetement = "Pantalon", MontantCouture = 8000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmd2, p2, new(), IdBoss, "Boss DIALLO");
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmd2.IdCommande, MontantPaye = 8000m,
                DatePaiement = _clock.Now, ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");
            _commandeService.TransitionnerStatutPiece(p2.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(p2.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);

            using (var ctx = _factory.CreateDbContext())
            {
                var pDb2 = ctx.PiecesCommande.Find(p2.IdPieceCommande)!;
                Assert.That(pDb2.DateTerminee, Is.Not.Null, "CL4 Pièce2 DateTerminee renseignée");
                Assert.That(pDb2.DateTerminee!.Value.Date, Is.Not.EqualTo(_fin.Date),
                    "CL4 Pièce2 terminée le mois suivant ne doit pas tomber sur la date fin du mois courant");
            }

            // L'aperçu commission pour la période du mois courant NE DOIT PAS inclure p2
            var apercu = _commissionService.CalculerApercu(_debut, _fin, TauxCommission, false, IdCouturierA);
            if (apercu.Count > 0)
            {
                bool p2DansApercu = apercu.First().IdsPieces.Contains(p2.IdPieceCommande);
                Assert.That(p2DansApercu, Is.False,
                    "CL4 La pièce terminée le mois suivant ne doit pas figurer dans la période courante");
            }
        }

        // =====================================================================
        // CAS LIMITE 5 — Atomicité (Étape 10) : si l'écriture d'audit échoue
        //               lors d'une rétrogradation avec annulation de commission,
        //               TOUTE l'opération est annulée (pièce et commission inchangées).
        // Ancienne règle : "audit non bloquant" → remplacée par "audit obligatoire et atomique".
        // =====================================================================
        [Test]
        public void E8_CL5_Atomicite_AuditEchoue_Rollback_PieceEtCommissionInchangees()
        {
            // Préparer une pièce Terminée commissionnée
            CommandeService.ViderCacheDoublonPourTests();
            var cmd = new Commande { IdClient = IdClient1, DateFin = _fin };
            var piece = new PieceCommande { TypeVetement = "Robe", MontantCouture = 10000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmd, piece, new(), IdBoss, "Boss DIALLO");
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmd.IdCommande, MontantPaye = 10000m,
                DatePaiement = _clock.Now, ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");
            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(piece.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);

            // Enregistrer la commission
            var apercu = _commissionService.CalculerApercu(_debut, _fin, TauxCommission, false, null);
            _commissionService.EnregistrerCommissions(apercu, _debut, _fin,
                TauxCommission, false, IdBoss, "Boss DIALLO");

            int idCommission;
            using (var ctx = _factory.CreateDbContext())
            {
                var pc = ctx.PiecesCommande.Find(piece.IdPieceCommande)!;
                idCommission = pc.IdCommission!.Value;
                Assert.That(pc.Statut, Is.EqualTo("Terminee"), "CL5 Pré-condition : pièce Terminée");
                Assert.That(pc.IdCommission, Is.Not.Null,       "CL5 Pré-condition : commission liée");
            }

            // Étape 10 — Nouvelle règle : un audit qui échoue doit annuler TOUTE l'opération.
            // On crée un CommandeService avec un AuditService qui lève toujours une exception.
            // TransitionnerStatutPiece avec rétrogradation doit appeler AnnulerDansTransaction
            // + audit dans la même transaction. Si l'audit échoue : rollback complet.
            var auditQuiEchoue = new AuditServiceStubLancantException();
            var cmdSvcAuditFaulty = new CommandeService(
                _factory,
                new Microsoft.Extensions.Logging.Logger<CommandeService>(NullLoggerFactory.Instance),
                auditService: auditQuiEchoue,
                clock: _clock);

            // L'opération doit lever une exception
            var exCl5 = Assert.Catch<Exception>(() =>
                cmdSvcAuditFaulty.TransitionnerStatutPiece(
                    piece.IdPieceCommande, "En cours",
                    IdBoss, "Boss DIALLO", "test échec audit",
                    _commissionService));
            Assert.That(exCl5, Is.Not.Null,
                "CL5 (Étape 10) — Si l'audit échoue, l'exception doit se propager");

            // Vérification : rollback total → pièce et commission inchangées
            using (var ctx = _factory.CreateDbContext())
            {
                var pc   = ctx.PiecesCommande.Find(piece.IdPieceCommande)!;
                var comm = ctx.Commissions.Find(idCommission)!;
                Assert.That(pc.Statut,       Is.EqualTo("Terminee"),
                    "CL5 (Étape 10) — Rollback : la pièce doit rester Terminée (pas rétrogradée)");
                Assert.That(comm.EstAnnulee, Is.False,
                    "CL5 (Étape 10) — Rollback : la commission ne doit PAS être annulée");
                Assert.That(pc.IdCommission, Is.EqualTo(idCommission),
                    "CL5 (Étape 10) — Rollback : IdCommission doit rester lié");
            }
        }

        // =====================================================================
        // CAS LIMITE 6 — Unicité : deux retours créés dans la même seconde ont
        //               des NumeroRetour différents ; doublon forcé rejeté par UNIQUE
        // =====================================================================
        [Test]
        public void E8_CL6_Unicite_NumeroRetour()
        {
            // Préparer 2 pièces livrées pour pouvoir créer 2 retours
            CommandeService.ViderCacheDoublonPourTests();
            var cmd = new Commande { IdClient = IdClient1, DateFin = _fin };
            var pA  = new PieceCommande { TypeVetement = "Robe 1", MontantCouture = 5000m, IdCouturier = IdCouturierA };
            var pB  = new PieceCommande { TypeVetement = "Robe 2", MontantCouture = 5000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmd, pA, new(), IdBoss, "Boss DIALLO", null,
                new List<(PieceCommande, List<Mesure>, List<MaterielSupplement>?)>
                {
                    (pB, new List<Mesure>(), null)
                });
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmd.IdCommande, MontantPaye = 10000m,
                DatePaiement = _clock.Now, ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");
            _commandeService.TransitionnerStatutPiece(pA.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(pA.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(pA.IdPieceCommande, "Livree",   IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(pB.IdPieceCommande, "En cours", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(pB.IdPieceCommande, "Terminee", IdBoss, "Boss DIALLO", null);
            _commandeService.TransitionnerStatutPiece(pB.IdPieceCommande, "Livree",   IdBoss, "Boss DIALLO", null);

            // Créer 2 retours
            var r1 = new Retour
            {
                IdCommande = cmd.IdCommande, IdPieceCommande = pA.IdPieceCommande,
                IdCouturier = IdCouturierA, DescriptionProbleme = "Défaut 1",
                IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "Boss DIALLO"
            };
            var r2 = new Retour
            {
                IdCommande = cmd.IdCommande, IdPieceCommande = pB.IdPieceCommande,
                IdCouturier = IdCouturierA, DescriptionProbleme = "Défaut 2",
                IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "Boss DIALLO"
            };
            _retourService.Ajouter(r1);
            _retourService.Ajouter(r2);

            r1 = _retourService.ObtenirParId(r1.IdRetour)!;
            r2 = _retourService.ObtenirParId(r2.IdRetour)!;

            // Les numéros doivent être différents
            Assert.That(r1.NumeroRetour, Is.Not.EqualTo(r2.NumeroRetour),
                "CL6 Deux retours créés au même moment doivent avoir des numéros différents");

            // Les numéros doivent suivre le format RET-AAAA-NNNNN
            Assert.That(r1.NumeroRetour, Does.Match(@"RET-\d{4}-\d{5}"), "CL6 Format r1");
            Assert.That(r2.NumeroRetour, Does.Match(@"RET-\d{4}-\d{5}"), "CL6 Format r2");

            // Tentative de doublon forcé : insérer un retour avec le même NumeroRetour
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.Retours.Add(new Retour
                {
                    NumeroRetour = r1.NumeroRetour,   // doublon forcé
                    IdCommande = cmd.IdCommande, IdPieceCommande = pA.IdPieceCommande,
                    IdCouturier = IdCouturierA, Statut = "Signale",
                    DateSignalement = DateTime.Now, DescriptionProbleme = "dup",
                    IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "Boss DIALLO"
                });
                // SQLite avec index UNIQUE doit rejeter ce SaveChanges
                Assert.Throws<Microsoft.EntityFrameworkCore.DbUpdateException>(() => ctx.SaveChanges(),
                    "CL6 L'index UNIQUE sur NumeroRetour doit rejeter un doublon forcé");
            }
        }

        // =====================================================================
        // CAS LIMITE 7 — Suppression d'une commande avec paiement : refusée
        //               Avec paiement annulé uniquement : encore refusée
        //               (règle SupprimerAsync : bloqué si paiements, même annulés)
        // =====================================================================
        [Test]
        public void E8_CL7_Suppression_Commande_Avec_Paiement_Refuse()
        {
            CommandeService.ViderCacheDoublonPourTests();
            var cmd = new Commande { IdClient = IdClient1, DateFin = _fin };
            var piece = new PieceCommande { TypeVetement = "Robe", MontantCouture = 10000m, IdCouturier = IdCouturierA };
            _commandeService.Ajouter(cmd, piece, new(), IdBoss, "Boss DIALLO");

            // Paiement enregistré
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmd.IdCommande, MontantPaye = 5000m,
                DatePaiement = _clock.Now, ModePaiement = "Espèces"
            }, IdBoss, "Boss DIALLO");

            // Tentative de suppression avec paiement actif → refusée
            var ex1 = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.SupprimerAsync(cmd.IdCommande, IdBoss, "Boss DIALLO", "test suppression").GetAwaiter().GetResult());
            Assert.That(ex1, Is.Not.Null, "CL7 Suppression avec paiement actif doit être refusée");

            // Annuler le paiement
            int idPaiement;
            using (var ctx = _factory.CreateDbContext())
                idPaiement = ctx.Paiements.Where(p => p.IdCommande == cmd.IdCommande).Single().IdPaiement;
            _paiementService.Annuler(idPaiement, "annulation pour test", IdBoss, "Boss DIALLO");

            // Tentative de suppression avec paiement annulé → aussi refusée (règle du code)
            var ex2 = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.SupprimerAsync(cmd.IdCommande, IdBoss, "Boss DIALLO", "test suppression 2").GetAwaiter().GetResult());
            Assert.That(ex2, Is.Not.Null,
                "CL7 Suppression avec paiement annulé doit également être refusée (traçabilité)");
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private void RechargerCommande(int idCommande, out decimal resteAPayer)
        {
            using var ctx = _factory.CreateDbContext();
            var cmd = ctx.Commandes
                .Include(c => c.Pieces)
                .Include(c => c.Paiements)
                .Include(c => c.MaterielSupplements)
                .First(c => c.IdCommande == idCommande);
            resteAPayer = cmd.ResteAPayer;
        }

        // Champ pour l'assertion j) déclarée dans une portée de using
        private decimal sommeLignesBase = 0m;
    }

    // =========================================================================
    // INFRASTRUCTURE DE TEST
    // =========================================================================

    /// <summary>Factory SQLite (connexion partagée, schéma EnsureCreated).</summary>
    internal class E8DbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _opts;
        public E8DbContextFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
        public ApplicationDbContext CreateDbContext() => new(_opts);
    }

    /// <summary>
    /// Horloge fictive pour les tests Étape 8.
    /// Permet de contrôler Now (local) et UtcNow indépendamment.
    /// </summary>
    internal class FakeClock8 : IClock
    {
        private DateTime _utcNow;
        public FakeClock8(DateTime localNow)
        {
            _utcNow = localNow.ToUniversalTime();
        }

        public DateTime Now    => _utcNow.ToLocalTime();
        public DateTime UtcNow => _utcNow;

        public void SetUtcNow(DateTime utcNow) => _utcNow = utcNow;
        public void Avancer(TimeSpan duree)    => _utcNow += duree;
    }

    /// <summary>
    /// Mock IParametresService pour les tests Étape 8.
    /// Retourne des valeurs contrôlées pour taux de commission, seuil prime, salaire.
    /// </summary>
    internal class E8ParametresServiceMock : IParametresService
    {
        private readonly decimal _taux;
        private readonly int     _seuilPrime;
        private readonly decimal _montantPrime;
        private readonly decimal _salaireSecretaire;

        public E8ParametresServiceMock(decimal tauxCommissionDefaut, int seuilPrimeNbCommandes,
            decimal montantPrimeQualite, decimal salaireSecretaire)
        {
            _taux              = tauxCommissionDefaut;
            _seuilPrime        = seuilPrimeNbCommandes;
            _montantPrime      = montantPrimeQualite;
            _salaireSecretaire = salaireSecretaire;
        }

        public Task<string?> ObtenirValeur(string cle) => Task.FromResult<string?>(null);
        public Task DefinirValeur(string cle, string valeur) => Task.CompletedTask;
        public Task<int>     ObtenirDelaiAlerteRendezVousHeures()           => Task.FromResult(3);
        public Task          DefinirDelaiAlerteRendezVousHeures(int h)      => Task.CompletedTask;
        public Task<int>     ObtenirSeuiRetardHeures()                      => Task.FromResult(24);
        public Task          DefinirSeuiRetardHeures(int h)                 => Task.CompletedTask;
        public Task<int>     ObtenirSeuilPrimeNbCommandes()                 => Task.FromResult(_seuilPrime);
        public Task          DefinirSeuilPrimeNbCommandes(int n)            => Task.CompletedTask;
        public Task<decimal> ObtenirSalaireMensuelSecretaire()              => Task.FromResult(_salaireSecretaire);
        public Task          DefinirSalaireMensuelSecretaire(decimal v)     => Task.CompletedTask;
        public Task<string>  ObtenirNomAtelier()                            => Task.FromResult("Atelier Test");
        public Task          DefinirNomAtelier(string v)                    => Task.CompletedTask;
        public Task<string>  ObtenirAdresseAtelier()                        => Task.FromResult("");
        public Task          DefinirAdresseAtelier(string v)                => Task.CompletedTask;
        public Task<string>  ObtenirTelAtelier()                            => Task.FromResult("");
        public Task          DefinirTelAtelier(string v)                    => Task.CompletedTask;
        public Task<string>  ObtenirPiedRecu()                              => Task.FromResult("");
        public Task          DefinirPiedRecu(string v)                      => Task.CompletedTask;
        public Task<decimal> ObtenirTauxCommissionDefaut()                  => Task.FromResult(_taux);
        public Task          DefinirTauxCommissionDefaut(decimal v)         => Task.CompletedTask;
        public Task<decimal> ObtenirPrimeZeroDefaut()                       => Task.FromResult(_montantPrime);
        public Task          DefinirPrimeZeroDefaut(decimal v)              => Task.CompletedTask;
        public Task<decimal> ObtenirMontantPrimeQualite()                   => Task.FromResult(_montantPrime);
        public Task          DefinirMontantPrimeQualite(decimal v)          => Task.CompletedTask;
        public Task<int>     ObtenirDelaiAnnulationSecretaireMinutes()      => Task.FromResult(10);
        public Task          DefinirDelaiAnnulationSecretaireMinutes(int v) => Task.CompletedTask;
        public Task<string>  ObtenirModeCA()                                => Task.FromResult("Total");
        public Task          DefinirModeCA(string v)                        => Task.CompletedTask;
        public Task<decimal> ObtenirSeuilAlerteDépenses()                   => Task.FromResult(500000m);
        public Task          DefinirSeuilAlerteDépenses(decimal v)          => Task.CompletedTask;
        public Task<bool>    ObtenirApprobationDepenses()                   => Task.FromResult(true);
        public Task          DefinirApprobationDepenses(bool v)             => Task.CompletedTask;
        public Task<decimal> ObtenirBudgetLoyer()                           => Task.FromResult(50000m);
        public Task          DefinirBudgetLoyer(decimal v)                  => Task.CompletedTask;
        public Task<string>  ObtenirMsgCommandePrete()                      => Task.FromResult("");
        public Task          DefinirMsgCommandePrete(string v)              => Task.CompletedTask;
        public Task<string>  ObtenirMsgRappelRdv()                          => Task.FromResult("");
        public Task          DefinirMsgRappelRdv(string v)                  => Task.CompletedTask;
        public Task<string>  ObtenirMsgRetouchePrete()                      => Task.FromResult("");
        public Task          DefinirMsgRetouchePrete(string v)              => Task.CompletedTask;
        public Task<int>     ObtenirFrequenceSyncHeures()                   => Task.FromResult(24);
        public Task          DefinirFrequenceSyncHeures(int v)              => Task.CompletedTask;
        public Task<bool>    ObtenirCompresserPhotos()                       => Task.FromResult(true);
        public Task          DefinirCompresserPhotos(bool v)                => Task.CompletedTask;
        public Task<string>  ObtenirCouleurAccent()                         => Task.FromResult("#CC0000");
        public Task          DefinirCouleurAccent(string v)                 => Task.CompletedTask;
        public Task<string>  ObtenirLangue()                                => Task.FromResult("fr");
        public Task          DefinirLangue(string v)                        => Task.CompletedTask;
        public Task<int>     ObtenirRetardAlerteAvantMinutes()              => Task.FromResult(120);
        public Task          DefinirRetardAlerteAvantMinutes(int v)         => Task.CompletedTask;
        public Task<int>     ObtenirRetardReportJours()                     => Task.FromResult(1);
        public Task          DefinirRetardReportJours(int v)                => Task.CompletedTask;
        public Task<string>  ObtenirRetardMessageModele()                   =>
            Task.FromResult(ParametresService.ModeleExcuseRetardDefaut);
        public Task          DefinirRetardMessageModele(string v)           => Task.CompletedTask;
    }

    /// <summary>AuditService qui lève une exception (pour tester la robustesse).</summary>
    internal class AuditServiceStubLancantException : IAuditService
    {
        public Task EnregistrerActionAsync(int a, string b, string c, string d, string e, int f,
            object? g = null, object? h = null, string? i = null, bool j = false)
            => Task.FromException(new InvalidOperationException("Audit simulé échoué"));

        public List<JournalAudit> ObtenirToutesLesEntrees() => new();
        public PagedResult<JournalAudit> ObtenirPage(int page, int pageSize, int? idOperateur = null, string? typeAction = null, string? recherche = null, DateTime? debut = null, DateTime? fin = null)
            => new PagedResult<JournalAudit> { Items = new(), TotalCount = 0, Page = page, PageSize = pageSize };
        public List<JournalAudit> ObtenirParPeriode(DateTime debut, DateTime fin) => new();
        public List<JournalAudit> ObtenirParOperateur(int id) => new();
        public List<JournalAudit> ObtenirParTypeAction(string type) => new();
        public List<JournalAudit> ObtenirParEntite(string entite, int id) => new();
        public (bool Integre, string Message) VerifierIntegriteChaine() => (true, "OK");
        public StatistiquesAudit ObtenirStatistiques(DateTime debut, DateTime fin) => new();
        public List<IndicateurAntiFraude> ObtenirIndicateurAntiFraude(DateTime depuis) => new();
    }
}
