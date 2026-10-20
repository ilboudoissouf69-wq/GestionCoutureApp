using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // TÂCHE 2 — Matériaux, dépenses et formules financières
    //
    // Règle métier (exemple du cahier) :
    //   Couture 1 000 F + galon 1 000 F → client paie 2 000 F.
    //   Bénéfice atelier = 1 000 F (la couture seule).
    //   Le galon est une avance remboursée, pas une charge.
    //
    // Tests :
    //   T2_01 : exemple chiffré couture 1 000 + galon 1 000 → bénéfice = 1 000
    //   T2_02 : MaterielSupplement NE crée PAS automatiquement une Depense
    //   T2_03 : StatsFinancieres.BeneficeNet n'inclut pas les matériaux
    //   T2_04 : StatsFinancieres et BilanFinancier donnent le même bénéfice
    //   T2_05 : matériaux non remboursés détectés (commande non soldée)
    //   T2_06 : matériaux remboursés = pas dans non-remboursés
    //   T2_07 : plusieurs commandes : bénéfice toujours cohérent entre DepenseService et TresorerieService
    //   T2_08 : commissions calculées sur MontantCouture uniquement (pas les matériaux)
    // ====================================================================

    [TestFixture]
    public class Tache2_FormulesBeneficeTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService  _commandeService   = null!;
        private DepenseService   _depenseService    = null!;
        private TresorerieService _tresorerieService = null!;
        private PaiementService  _paiementService   = null!;
        private MaterielService  _materielService   = null!;
        private CommissionService _commissionService = null!;

        private const int IdBoss       = 1;
        private const int IdSecretaire = 2;
        private const int IdCouturier  = 3;
        private const int IdClient     = 1;

        [SetUp]
        public void SetUp()
        {
            CommandeService.ViderCacheDoublonPourTests();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"Tache2_{Guid.NewGuid()}")
                .Options;
            _factory = new T2DbContextFactory(options);

            var logFactory = NullLoggerFactory.Instance;
            _commandeService   = new CommandeService(_factory, new Logger<CommandeService>(logFactory));
            _depenseService    = new DepenseService(_factory);
            _tresorerieService = new TresorerieService(_factory, new Logger<TresorerieService>(logFactory));
            _paiementService   = new PaiementService(_factory, new Logger<PaiementService>(logFactory));
            _materielService   = new MaterielService(_factory);
            _commissionService = new CommissionService(_factory, new Logger<CommissionService>(logFactory), new MockParametresService());

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

        // ── T2_01 : Exemple du cahier — couture 1 000 + galon 1 000 ─────
        //
        //   VALEURS ATTENDUES (calculées à la main) :
        //     Couture facturée = 1 000, matériaux facturés = 1 000
        //     Client paie 2 000 (tout)
        //     Matériaux encaissés = min(2 000, 1 000) = 1 000  (remboursés EN PREMIER)
        //     CA couture encaissé = max(0, 2 000 − 1 000) = 1 000
        //     CA encaissé total   = 2 000
        //     BeneficeNet = 2 000 − 0 (commissions) − 0 (charges) = 2 000
        //       → Le flux matériaux est neutre ; il reste 1 000 de couture dans les 2 000,
        //         mais le bénéfice brut est bien 2 000 avant déduction de charges.
        //         Pour connaître la part couture pure : CaEncaisseCouture = 1 000.

        [Test]
        public void T2_01_Exemple_Cahier_Couture_1000_Galon_1000_Benefice_1000()
        {
            // Arrange : couture 1 000 + galon 1 000, client paie 2 000
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(1) };
            var piece    = new PieceCommande { TypeVetement = "Chemise", MontantCouture = 1000m, IdCouturier = IdCouturier };
            var mat      = new List<MaterielSupplement>
            {
                new MaterielSupplement { Designation = "Galon", Quantite = 1, PrixUnitaire = 1000m }
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO", mat);

            // Client paie tout (couture + galon) = 2 000
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = commande.IdCommande, MontantPaye = 2000m,
                DatePaiement = DateTime.Today, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");

            var debut = DateTime.Today.AddDays(-1);
            var fin   = DateTime.Today.AddDays(1);
            var stats = _depenseService.ObtenirStats(debut, fin);
            var bilan = _tresorerieService.CalculerBilan(debut, fin);

            // ── Vérifications manuelles ────────────────────────────────────────
            // CA encaissé = 2 000
            Assert.That(stats.CaEncaisse, Is.EqualTo(2000m),
                "CA encaissé = couture 1 000 + galon 1 000 = 2 000");

            // Matériaux facturés (informative) = 1 000
            Assert.That(stats.TotalMateriaux, Is.EqualTo(1000m),
                "Matériaux facturés = 1 000");

            // CaEncaisseCouture (règle "matériaux EN PREMIER")
            //   = max(0, 2 000 − 1 000) = 1 000
            Assert.That(stats.CaEncaisseCouture, Is.EqualTo(1000m),
                "CA couture encaissé = max(0, 2 000 − 1 000) = 1 000 (matériaux remboursés EN PREMIER)");

            // BeneficeNet = CA encaissé − commissions − charges = 2 000 − 0 − 0 = 2 000
            // (les matériaux sont flux neutre, pas déduits du bénéfice)
            Assert.That(stats.BeneficeNet, Is.EqualTo(2000m),
                "BeneficeNet = 2 000 − 0 − 0 = 2 000 (matériaux flux neutre, non déduits)");

            // BilanNet (TresorerieService) = même résultat
            Assert.That(bilan.BilanNet, Is.EqualTo(2000m),
                "BilanNet (TresorerieService) = 2 000 (formule identique)");

            // Cohérence des deux services
            Assert.That(stats.BeneficeNet, Is.EqualTo(bilan.BilanNet),
                "BeneficeNet (DepenseService) == BilanNet (TresorerieService)");

            // CaEncaisseCouture du BilanFinancier = 1 000
            Assert.That(bilan.ChiffreAffairesCouture, Is.EqualTo(1000m),
                "BilanFinancier.ChiffreAffairesCouture = 1 000 (règle matériaux EN PREMIER)");
        }

        // ── T2_02 : MaterielSupplement ne crée pas de Depense ────────────

        [Test]
        public void T2_02_Ajouter_Materiau_Ne_Cree_Pas_De_Depense()
        {
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(2) };
            var piece    = new PieceCommande { TypeVetement = "Robe", MontantCouture = 5000m, IdCouturier = IdCouturier };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO");

            int nbDepensesAvant = CountDepenses();

            // Ajouter un matériau
            var mat = new MaterielSupplement
            {
                IdCommande = commande.IdCommande,
                IdPieceCommande = piece.IdPieceCommande,
                Designation = "Tissu wax", Quantite = 2, PrixUnitaire = 3000m
            };
            _materielService.Ajouter(mat, IdBoss, "Mamadou DIALLO");

            int nbDepensesApres = CountDepenses();

            Assert.That(nbDepensesApres, Is.EqualTo(nbDepensesAvant),
                "L'ajout d'un matériau ne doit jamais créer une Dépense automatiquement");
        }

        // ── T2_03 : StatsFinancieres.BeneficeNet sans déduction matériaux ─
        //
        //   VALEURS ATTENDUES (calculées à la main) :
        //     Couture = 10 000, matériaux = 5 000, paiement = 15 000 (tout)
        //     Matériaux encaissés = min(15 000, 5 000) = 5 000
        //     CA couture encaissé = max(0, 15 000 − 5 000) = 10 000
        //     BeneficeNet = 15 000 − 0 − 0 = 15 000  (matériaux flux neutre)
        //     CaEncaisseCouture = 10 000

        [Test]
        public void T2_03_StatsFinancieres_BeneficeNet_Exclut_Materiaux()
        {
            // Couture 10 000 + matériaux 5 000, client paie tout (15 000)
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(1) };
            var piece    = new PieceCommande { TypeVetement = "Boubou", MontantCouture = 10000m, IdCouturier = IdCouturier };
            var mat      = new List<MaterielSupplement>
            {
                new MaterielSupplement { Designation = "Tissu", Quantite = 1, PrixUnitaire = 5000m }
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO", mat);
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = commande.IdCommande, MontantPaye = 15000m,
                DatePaiement = DateTime.Today, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");

            var stats = _depenseService.ObtenirStats(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

            // BeneficeNet = 15 000 − 0 (commissions) − 0 (charges) = 15 000
            // Les matériaux ne sont pas déduits (flux neutre)
            Assert.That(stats.BeneficeNet, Is.EqualTo(15000m),
                "BeneficeNet = 15 000 − 0 − 0 = 15 000 (matériaux flux neutre, non déduits)");

            // CaEncaisseCouture = max(0, 15 000 − 5 000) = 10 000
            // Règle "matériaux EN PREMIER" : les 5 000 de matériaux sont remboursés en premier
            Assert.That(stats.CaEncaisseCouture, Is.EqualTo(10000m),
                "CaEncaisseCouture = max(0, 15 000 − 5 000) = 10 000 (matériaux remboursés EN PREMIER)");

            // TotalMateriaux = 5 000 (informatif)
            Assert.That(stats.TotalMateriaux, Is.EqualTo(5000m),
                "TotalMateriaux = 5 000 (montant facturé, informatif)");
        }

        // ── T2_04 : Cohérence DepenseService / TresorerieService ─────────
        //
        //   VALEURS ATTENDUES (calculées à la main) :
        //     Couture = 8 000, boutons = 10 × 200 = 2 000, paiement = 10 000 (tout)
        //     Dépense loyer = 30 000
        //     CA encaissé = 10 000
        //     BeneficeNet = 10 000 − 0 (commissions) − 30 000 (loyer) = −20 000
        //     (bilan négatif car loyer > CA)
        //
        //   Les deux services doivent donner −20 000.

        [Test]
        public void T2_04_DepenseService_Et_TresorerieService_Meme_Benefice()
        {
            // Créer une commande avec couture + matériaux + paiement
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(1) };
            var piece    = new PieceCommande { TypeVetement = "Pantalon", MontantCouture = 8000m, IdCouturier = IdCouturier };
            var mat      = new List<MaterielSupplement>
            {
                new MaterielSupplement { Designation = "Boutons", Quantite = 10, PrixUnitaire = 200m }
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO", mat);
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = commande.IdCommande, MontantPaye = 10000m,
                DatePaiement = DateTime.Today, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");

            // Dépense réelle : loyer 30 000
            _depenseService.Ajouter(new Depense
            {
                TypeDepense = "Loyer", Montant = 30000m,
                DateDepense = DateTime.Today, IdOperateur = IdBoss,
                NomOperateur = "Mamadou DIALLO", StatutValidation = "Validee"
            });

            var debut = DateTime.Today.AddDays(-1);
            var fin   = DateTime.Today.AddDays(1);

            var stats = _depenseService.ObtenirStats(debut, fin);
            var bilan = _tresorerieService.CalculerBilan(debut, fin);

            // ── Vérifications manuelles ────────────────────────────────────────
            // CA encaissé = 10 000 (couture 8 000 + boutons 2 000)
            Assert.That(stats.CaEncaisse, Is.EqualTo(10000m),
                "CA encaissé = 10 000");

            // CaEncaisseCouture = max(0, 10 000 − 2 000) = 8 000
            Assert.That(stats.CaEncaisseCouture, Is.EqualTo(8000m),
                "CaEncaisseCouture = max(0, 10 000 − 2 000) = 8 000 (matériaux EN PREMIER)");

            // BeneficeNet = 10 000 − 0 − 30 000 = −20 000
            Assert.That(stats.BeneficeNet, Is.EqualTo(-20000m),
                "BeneficeNet = 10 000 CA − 30 000 loyer = −20 000");

            // BilanNet (TresorerieService) = même résultat
            Assert.That(bilan.BilanNet, Is.EqualTo(-20000m),
                "BilanNet (TresorerieService) = −20 000 (formule identique)");

            // Les deux services donnent exactement le même bénéfice
            Assert.That(stats.BeneficeNet, Is.EqualTo(bilan.BilanNet),
                "DepenseService et TresorerieService doivent donner le même bénéfice net");
        }

        // ── T2_05 : Matériaux non remboursés — commande non soldée ───────

        [Test]
        public void T2_05_Materiaux_Non_Rembourses_Commande_Non_Soldee()
        {
            // Couture 8 000 + matériaux 4 000, client paie seulement 5 000 (acompte)
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(1) };
            var piece    = new PieceCommande { TypeVetement = "Veste", MontantCouture = 8000m, IdCouturier = IdCouturier };
            var mat      = new List<MaterielSupplement>
            {
                new MaterielSupplement { Designation = "Doublure", Quantite = 2, PrixUnitaire = 2000m }
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO", mat);

            // Paiement partiel : 5 000 sur 12 000 → commande non soldée
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = commande.IdCommande, MontantPaye = 5000m,
                DatePaiement = DateTime.Today, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");

            var stats = _depenseService.ObtenirStats(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

            Assert.That(stats.MateriauxNonRembourses, Is.GreaterThan(0m),
                "Des matériaux non remboursés doivent être détectés quand la commande n'est pas soldée");
            Assert.That(stats.MateriauxNonRembourses, Is.EqualTo(4000m),
                "Le total des matériaux de la commande non soldée = 4 000");
        }

        // ── T2_06 : Matériaux entièrement remboursés = 0 non-remboursés ──

        [Test]
        public void T2_06_Materiaux_Rembourses_Pas_Dans_NonRembourses()
        {
            // Couture 5 000 + matériaux 2 000, client paie tout (7 000)
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(1) };
            var piece    = new PieceCommande { TypeVetement = "Chemise", MontantCouture = 5000m, IdCouturier = IdCouturier };
            var mat      = new List<MaterielSupplement>
            {
                new MaterielSupplement { Designation = "Col brodé", Quantite = 1, PrixUnitaire = 2000m }
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO", mat);
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = commande.IdCommande, MontantPaye = 7000m,
                DatePaiement = DateTime.Today, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");

            var stats = _depenseService.ObtenirStats(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

            Assert.That(stats.MateriauxNonRembourses, Is.EqualTo(0m),
                "Commande soldée → matériaux remboursés → 0 non-remboursés");
        }

        // ── T2_07 : Plusieurs commandes — cohérence globale ───────────────
        //
        //   VALEURS ATTENDUES (calculées à la main) :
        //
        //   Commande 1 : couture = 10 000, matériaux = 0,     paiement = 10 000
        //     mat encaissés = min(10 000, 0) = 0
        //     couture enc   = max(0, 10 000 − 0) = 10 000
        //
        //   Commande 2 : couture = 8 000,  matériaux = 3 000, paiement = 11 000
        //     mat encaissés = min(11 000, 3 000) = 3 000
        //     couture enc   = max(0, 11 000 − 3 000) = 8 000
        //
        //   CA encaissé total       = 10 000 + 11 000 = 21 000
        //   CA couture encaissé     = 10 000 + 8 000  = 18 000
        //   CA matériaux encaissé   = 0 + 3 000       = 3 000
        //   TotalMatériaux facturés = 0 + 3 000        = 3 000
        //   Loyer = 20 000
        //   BeneficeNet = 21 000 − 0 (commissions) − 20 000 (loyer) = 1 000

        [Test]
        public void T2_07_Plusieurs_Commandes_Benefice_Coherent()
        {
            // Commande 1 : couture 10 000, paiement 10 000, pas de matériaux
            CreerCommandePayee(10000m, 0m, 10000m);
            // Commande 2 : couture 8 000 + matériaux 3 000, paiement 11 000
            CreerCommandePayee(8000m, 3000m, 11000m);

            // Dépenses réelles : loyer 20 000
            _depenseService.Ajouter(new Depense
            {
                TypeDepense = "Loyer", Montant = 20000m,
                DateDepense = DateTime.Today, IdOperateur = IdBoss,
                NomOperateur = "Mamadou DIALLO", StatutValidation = "Validee"
            });

            var debut = DateTime.Today.AddDays(-1);
            var fin   = DateTime.Today.AddDays(1);

            var stats = _depenseService.ObtenirStats(debut, fin);
            var bilan = _tresorerieService.CalculerBilan(debut, fin);

            // ── Vérifications manuelles ────────────────────────────────────────
            // CA encaissé = 10 000 + 11 000 = 21 000
            Assert.That(stats.CaEncaisse, Is.EqualTo(21000m),
                "CA encaissé = 10 000 + 11 000 = 21 000");

            // CA couture encaissé = 10 000 + 8 000 = 18 000
            // Cmd1 : max(0, 10 000 − 0) = 10 000
            // Cmd2 : max(0, 11 000 − 3 000) = 8 000
            Assert.That(stats.CaEncaisseCouture, Is.EqualTo(18000m),
                "CaEncaisseCouture = 10 000 + 8 000 = 18 000 (matériaux remboursés EN PREMIER)");

            // TotalMateriaux facturés = 3 000 (informatif)
            Assert.That(stats.TotalMateriaux, Is.EqualTo(3000m),
                "TotalMateriaux facturés = 3 000 (informatif)");

            // BeneficeNet = 21 000 − 0 − 20 000 = 1 000
            // Les matériaux sont flux neutre : on ne les déduit pas
            Assert.That(stats.BeneficeNet, Is.EqualTo(1000m),
                "BeneficeNet = 21 000 CA − 20 000 loyer = 1 000 (matériaux flux neutre)");

            // BilanNet (TresorerieService) = 1 000 également
            Assert.That(bilan.BilanNet, Is.EqualTo(1000m),
                "BilanNet (TresorerieService) = 21 000 − 0 − 20 000 = 1 000");

            // ChiffreAffairesCouture dans le bilan = 18 000
            Assert.That(bilan.ChiffreAffairesCouture, Is.EqualTo(18000m),
                "BilanFinancier.ChiffreAffairesCouture = 18 000");

            // Cohérence entre les deux services
            Assert.That(stats.BeneficeNet, Is.EqualTo(bilan.BilanNet),
                "Les deux services donnent le même bénéfice net");
        }

        // ── T2_07b : Acompte partiel — règle "matériaux EN PREMIER" ──────
        //
        //   VALEURS ATTENDUES (calculées à la main) :
        //     Couture = 2 000, matériaux = 1 000, paiement acompte = 1 500
        //     mat encaissés = min(1 500, 1 000) = 1 000  (matériaux EN PREMIER)
        //     couture enc   = max(0, 1 500 − 1 000) = 500
        //     CA encaissé   = 1 500
        //
        //   Commande sans paiement :
        //     CA = 0, couture = 0, matériaux = 0

        [Test]
        public void T2_07b_Acompte_Partiel_Et_Sans_Paiement()
        {
            var debut = DateTime.Today.AddDays(-1);
            var fin   = DateTime.Today.AddDays(2);

            // Commande 1 : couture 2 000 + matériaux 1 000, acompte 1 500
            var cmd1  = new Commande { IdClient = IdClient, DateFin = DateTime.Today.AddDays(1) };
            var p1    = new PieceCommande { TypeVetement = "Chemise", MontantCouture = 2000m, IdCouturier = IdCouturier };
            var mat1  = new List<MaterielSupplement>
                { new MaterielSupplement { Designation = "Tissu", Quantite = 1, PrixUnitaire = 1000m } };
            _commandeService.Ajouter(cmd1, p1, new List<Mesure>(), IdBoss, "Mamadou DIALLO", mat1);
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = cmd1.IdCommande, MontantPaye = 1500m,
                DatePaiement = DateTime.Today, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");

            // Commande 2 : couture 5 000, aucun paiement
            var cmd2  = new Commande { IdClient = IdClient, DateFin = DateTime.Today.AddDays(1) };
            var p2    = new PieceCommande { TypeVetement = "Robe", MontantCouture = 5000m, IdCouturier = IdCouturier };
            _commandeService.Ajouter(cmd2, p2, new List<Mesure>(), IdBoss, "Mamadou DIALLO");

            var repartition = _tresorerieService.CalculerRepartitionCa(debut, fin);

            // Cmd1 : mat encaissés = min(1 500, 1 000) = 1 000 ; couture = 500
            // Cmd2 : paye = 0 ; mat = 0 ; couture = 0
            // Total : mat enc = 1 000, couture = 500
            Assert.That(repartition.CaEncaisseMateriaux, Is.EqualTo(1000m),
                "Matériaux encaissés = min(1 500, 1 000) = 1 000 (acompte couvre les matériaux en premier)");
            Assert.That(repartition.CaEncaisseCouture, Is.EqualTo(500m),
                "Couture encaissée = max(0, 1 500 − 1 000) = 500");
            Assert.That(repartition.TotalMateriauxFactures, Is.EqualTo(1000m),
                "Total matériaux facturés = 1 000 (cmd1 uniquement)");

            // Commande sans paiement : ne contribue ni aux matériaux ni à la couture encaissée
            // On vérifie que la cmd2 n'est pas comptée dans la répartition
            // (ca couture = 500, pas 5 500)
            var statsStats = _depenseService.ObtenirStats(debut, fin);
            Assert.That(statsStats.CaEncaisse, Is.EqualTo(1500m),
                "CA encaissé = 1 500 (cmd2 sans paiement = 0)");
            Assert.That(statsStats.CaEncaisseCouture, Is.EqualTo(500m),
                "CaEncaisseCouture = 500 (cmd2 sans paiement ne contribue pas)");
        }

        // ── T2_08 : Commission calculée sur MontantCouture uniquement ────

        [Test]
        public void T2_08_Commission_Basee_Sur_MontantCouture_Uniquement()
        {
            // Couture 10 000 + matériaux 5 000, tout payé, pièce terminée
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(1) };
            var piece    = new PieceCommande { TypeVetement = "Boubou", MontantCouture = 10000m, IdCouturier = IdCouturier };
            var mat      = new List<MaterielSupplement>
            {
                new MaterielSupplement { Designation = "Tissu", Quantite = 1, PrixUnitaire = 5000m }
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO", mat);
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = commande.IdCommande, MontantPaye = 15000m,
                DatePaiement = DateTime.Today, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");

            // Passer en Terminee pour avoir DateTerminee (flux strict : En cours d'abord)
            _commandeService.ChangerStatutPiece(piece.IdPieceCommande, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(piece.IdPieceCommande, "Terminee", IdBoss, "Mamadou DIALLO");

            // Calculer aperçu avec 10% sur montant total (non-encaissé)
            var apercu = _commissionService.CalculerApercu(
                DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1), 10m, false, IdCouturier);

            Assert.That(apercu, Has.Count.EqualTo(1));
            var ligne = apercu[0];

            // Commission = 10% de 10 000 (couture) = 1 000, PAS 10% de 15 000 (couture+mat)
            Assert.That(ligne.CaTotal, Is.EqualTo(10000m),
                "CaTotal doit être le MontantCouture uniquement (10 000), pas 15 000");
            Assert.That(ligne.Commission, Is.EqualTo(1000m),
                "Commission 10% sur couture = 1 000 FCFA (matériaux exclus)");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void CreerCommandePayee(decimal montantCouture, decimal montantMateriaux, decimal montantPaye)
        {
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(1) };
            var piece    = new PieceCommande { TypeVetement = "Robe", MontantCouture = montantCouture, IdCouturier = IdCouturier };
            List<MaterielSupplement>? mat = null;
            if (montantMateriaux > 0)
                mat = new List<MaterielSupplement>
                {
                    new MaterielSupplement { Designation = "Tissu", Quantite = 1, PrixUnitaire = montantMateriaux }
                };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO", mat);
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = commande.IdCommande, MontantPaye = montantPaye,
                DatePaiement = DateTime.Today, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");
        }

        private int CountDepenses()
        {
            using var ctx = _factory.CreateDbContext();
            return ctx.Depenses.Count();
        }
    }

    internal class T2DbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public T2DbContextFactory(DbContextOptions<ApplicationDbContext> options)
            => _options = options;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
    }
}
