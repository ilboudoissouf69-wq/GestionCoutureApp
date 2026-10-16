using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // =======================================================================
    // ÉTAPE 3 — Dashboard : rafraîchissement automatique via événements domaine
    //
    // Tests :
    //   E3_01 : un événement publié est bien reçu par un abonné
    //   E3_02 : un abonné désabonné ne reçoit plus d'événement
    //   E3_03 : PaiementService.Ajouter publie DomainChangedType.Paiement
    //   E3_04 : PaiementService.Annuler publie DomainChangedType.Paiement
    //   E3_05 : CommissionService.EnregistrerCommissions publie DomainChangedType.Commission
    //   E3_06 : DepenseService.Ajouter publie DomainChangedType.Depense
    //   E3_07 : DepenseService.Valider publie DomainChangedType.Depense
    //   E3_08 : DepenseService.Annuler publie DomainChangedType.Depense
    //   E3_09 : RetourService.Ajouter publie DomainChangedType.Retour
    //   E3_10 : MaterielService.Ajouter publie DomainChangedType.Materiel
    //   E3_11 : CommandeService.AjouterPiece publie DomainChangedType.Commande
    //   E3_12 : pas de fuite — abonnement/désabonnement multiple propre
    // =======================================================================

    [TestFixture]
    public class Etape3_DashboardRefreshTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private EventAggregator _ea = null!;

        // ── Helpers ──────────────────────────────────────────────────────

        private static IDbContextFactory<ApplicationDbContext> CreerFactory(string dbName)
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            return new TestDbContextFactory(opts);
        }

        private class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _opts;
            public TestDbContextFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_opts);
        }

        private static Employe CreerBoss(ApplicationDbContext ctx)
        {
            var boss = new Employe
            {
                Prenom = "Boss",
                Nom = "Test",
                Identifiant = "bosstest",
                Role = "Boss",
                Statut = "Actif",
                MotDePasse = "hash"
            };
            ctx.Employes.Add(boss);
            ctx.SaveChanges();
            return boss;
        }

        private static Client CreerClient(ApplicationDbContext ctx)
        {
            var c = new Client { Prenom = "Jean", Nom = "Dupont", Telephone = "0600000000" };
            ctx.Clients.Add(c);
            ctx.SaveChanges();
            return c;
        }

        [SetUp]
        public void Init()
        {
            _factory = CreerFactory("dash_test_" + Guid.NewGuid());
            _ea = new EventAggregator();
        }

        // ── E3_01 : abonné reçoit l'événement ────────────────────────────

        [Test]
        public void E3_01_AbonneRecoit_Evenement()
        {
            bool recu = false;
            _ea.Subscribe(DomainChangedType.Paiement, _ => recu = true);

            _ea.Publish(new DomainChangedEvent { Type = DomainChangedType.Paiement });

            Assert.That(recu, Is.True, "L'abonné devrait avoir reçu l'événement");
        }

        // ── E3_02 : abonné désabonné ne reçoit plus ──────────────────────

        [Test]
        public void E3_02_Desabonne_NePlusRecevoir()
        {
            int compteur = 0;
            Action<DomainChangedEvent> handler = _ => compteur++;

            _ea.Subscribe(DomainChangedType.Depense, handler);
            _ea.Publish(new DomainChangedEvent { Type = DomainChangedType.Depense });
            Assert.That(compteur, Is.EqualTo(1), "Devrait avoir reçu le 1er événement");

            _ea.Unsubscribe(DomainChangedType.Depense, handler);
            _ea.Publish(new DomainChangedEvent { Type = DomainChangedType.Depense });
            Assert.That(compteur, Is.EqualTo(1), "Ne devrait plus recevoir après désabonnement");
        }

        // ── E3_03 : PaiementService.Ajouter publie Paiement ──────────────

        [Test]
        public void E3_03_PaiementService_Ajouter_PubliePaiement()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var commande = new Commande
            {
                IdClient = client.IdClient,
                DateDebut = DateTime.Today,
                DateFin = DateTime.Today.AddDays(7),
                IdOperateurCreation = boss.IdEmploye
            };
            ctx.Commandes.Add(commande);
            ctx.SaveChanges();

            // PaiementService exige au moins une pièce sur la commande
            ctx.PiecesCommande.Add(new PieceCommande
            {
                IdCommande = commande.IdCommande,
                TypeVetement = "Robe",
                MontantCouture = 10000,
                Statut = "A faire"
            });
            ctx.SaveChanges();

            var svc = new PaiementService(_factory, NullLogger<PaiementService>.Instance,
                eventAggregator: _ea);

            int recu = 0;
            _ea.Subscribe(DomainChangedType.Paiement, _ => recu++);

            svc.Ajouter(new Paiement
            {
                IdCommande = commande.IdCommande,
                MontantPaye = 5000,
                DatePaiement = DateTime.Today,
                ModePaiement = "Espèces",
                IdOperateur = boss.IdEmploye,
                NomOperateur = "Boss Test"
            }, boss.IdEmploye, "Boss Test");

            Assert.That(recu, Is.EqualTo(1), "PaiementService.Ajouter doit publier DomainChangedType.Paiement");
        }

        // ── E3_04 : PaiementService.Annuler publie Paiement ──────────────

        [Test]
        public void E3_04_PaiementService_Annuler_PubliePaiement()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var commande = new Commande
            {
                IdClient = client.IdClient,
                DateDebut = DateTime.Today,
                DateFin = DateTime.Today.AddDays(7),
                IdOperateurCreation = boss.IdEmploye
            };
            ctx.Commandes.Add(commande);
            var paiement = new Paiement
            {
                IdCommande = commande.IdCommande,
                MontantPaye = 3000,
                DatePaiement = DateTime.Today,
                ModePaiement = "Espèces",
                IdOperateur = boss.IdEmploye,
                NomOperateur = "Boss Test",
                RecuNumero = "REC-TEST-001"
            };
            ctx.Paiements.Add(paiement);
            ctx.SaveChanges();

            var svc = new PaiementService(_factory, NullLogger<PaiementService>.Instance,
                eventAggregator: _ea);

            int recu = 0;
            _ea.Subscribe(DomainChangedType.Paiement, _ => recu++);

            svc.Annuler(paiement.IdPaiement, "Test annulation", boss.IdEmploye, "Boss Test");

            Assert.That(recu, Is.EqualTo(1), "PaiementService.Annuler doit publier DomainChangedType.Paiement");
        }

        // ── E3_05 : DepenseService.Ajouter publie Depense ────────────────

        [Test]
        public void E3_05_DepenseService_Ajouter_PublieDepense()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx);

            var svc = new DepenseService(_factory, _ea);

            int recu = 0;
            _ea.Subscribe(DomainChangedType.Depense, _ => recu++);

            svc.Ajouter(new Depense
            {
                TypeDepense = "Loyer",
                Montant = 50000,
                DateDepense = DateTime.Today,
                Categorie = "Fixe",
                StatutValidation = "EnAttente",
                IdOperateur = boss.IdEmploye,
                NomOperateur = "Boss Test"
            });

            Assert.That(recu, Is.EqualTo(1), "DepenseService.Ajouter doit publier DomainChangedType.Depense");
        }

        // ── E3_06 : DepenseService.Valider publie Depense ────────────────

        [Test]
        public void E3_06_DepenseService_Valider_PublieDepense()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx);
            var dep = new Depense
            {
                TypeDepense = "Électricité",
                Montant = 10000,
                DateDepense = DateTime.Today,
                Categorie = "Fixe",
                StatutValidation = "EnAttente",
                IdOperateur = boss.IdEmploye,
                NomOperateur = "Boss Test"
            };
            ctx.Depenses.Add(dep);
            ctx.SaveChanges();

            var svc = new DepenseService(_factory, _ea);

            int recu = 0;
            _ea.Subscribe(DomainChangedType.Depense, _ => recu++);

            svc.Valider(dep.IdDepense, "Boss Test");

            Assert.That(recu, Is.EqualTo(1), "DepenseService.Valider doit publier DomainChangedType.Depense");
        }

        // ── E3_07 : DepenseService.Annuler publie Depense ────────────────

        [Test]
        public void E3_07_DepenseService_Annuler_PublieDepense()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx);
            var dep = new Depense
            {
                TypeDepense = "Divers",
                Montant = 2000,
                DateDepense = DateTime.Today,
                Categorie = "Variable",
                StatutValidation = "EnAttente",
                IdOperateur = boss.IdEmploye,
                NomOperateur = "Boss Test"
            };
            ctx.Depenses.Add(dep);
            ctx.SaveChanges();

            var svc = new DepenseService(_factory, _ea);

            int recu = 0;
            _ea.Subscribe(DomainChangedType.Depense, _ => recu++);

            svc.Annuler(dep.IdDepense, "Raison test", "Boss Test");

            Assert.That(recu, Is.EqualTo(1), "DepenseService.Annuler doit publier DomainChangedType.Depense");
        }

        // ── E3_08 : MaterielService.Ajouter publie Materiel ──────────────

        [Test]
        public void E3_08_MaterielService_Ajouter_PublieMateriel()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx);
            var client = CreerClient(ctx);
            var commande = new Commande
            {
                IdClient = client.IdClient,
                DateDebut = DateTime.Today,
                DateFin = DateTime.Today.AddDays(7),
                IdOperateurCreation = boss.IdEmploye
            };
            ctx.Commandes.Add(commande);
            ctx.SaveChanges();

            var svc = new MaterielService(_factory, _ea);

            int recu = 0;
            _ea.Subscribe(DomainChangedType.Materiel, _ => recu++);

            svc.Ajouter(new MaterielSupplement
            {
                IdCommande = commande.IdCommande,
                Designation = "Galon",
                Quantite = 2,
                PrixUnitaire = 1000
            }, boss.IdEmploye, "Boss Test");

            Assert.That(recu, Is.EqualTo(1), "MaterielService.Ajouter doit publier DomainChangedType.Materiel");
        }

        // ── E3_09 : CommandeService publie Commande via IEventAggregator ─

        [Test]
        public void E3_09_CommandeService_AjouterPiece_PublieCommande()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx);
            var client = CreerClient(ctx);

            var svc = new CommandeService(_factory, NullLogger<CommandeService>.Instance,
                eventAggregator: _ea);

            // Créer une commande minimale directement en base
            var commande = new Commande
            {
                IdClient = client.IdClient,
                DateDebut = DateTime.Today,
                DateFin = DateTime.Today.AddDays(7),
                IdOperateurCreation = boss.IdEmploye
            };
            ctx.Commandes.Add(commande);
            ctx.SaveChanges();

            int recu = 0;
            _ea.Subscribe(DomainChangedType.Commande, _ => recu++);

            svc.AjouterPiece(
                commande.IdCommande,
                new PieceCommande
                {
                    IdCommande = commande.IdCommande,
                    TypeVetement = "Chemise",
                    MontantCouture = 5000,
                    Statut = "A faire"
                },
                new List<Mesure>(),
                roleBoss: true,
                idOperateur: boss.IdEmploye,
                nomOperateur: "Boss Test");

            Assert.That(recu, Is.GreaterThanOrEqualTo(1),
                "CommandeService.AjouterPiece doit publier DomainChangedType.Commande via IEventAggregator");
        }

        // ── E3_10 : pas de fuite sur abonnement/désabonnement multiple ────

        [Test]
        public void E3_10_PasDeFuite_AbonnementMultiple()
        {
            int compteur = 0;
            Action<DomainChangedEvent> h1 = _ => compteur++;
            Action<DomainChangedEvent> h2 = _ => compteur++;

            _ea.Subscribe(DomainChangedType.Commande, h1);
            _ea.Subscribe(DomainChangedType.Commande, h2);

            _ea.Publish(new DomainChangedEvent { Type = DomainChangedType.Commande });
            Assert.That(compteur, Is.EqualTo(2), "Les deux handlers doivent recevoir l'événement");

            _ea.Unsubscribe(DomainChangedType.Commande, h1);
            _ea.Unsubscribe(DomainChangedType.Commande, h2);

            _ea.Publish(new DomainChangedEvent { Type = DomainChangedType.Commande });
            Assert.That(compteur, Is.EqualTo(2), "Après désabonnement total, aucun handler ne devrait recevoir");
        }

        // ── E3_11 : un même handler ne s'abonne pas en double ─────────────

        [Test]
        public void E3_11_PasDedoublonnement_MemeHandler()
        {
            int compteur = 0;
            Action<DomainChangedEvent> h = _ => compteur++;

            // Abonner deux fois le même handler
            _ea.Subscribe(DomainChangedType.Retour, h);
            _ea.Subscribe(DomainChangedType.Retour, h);

            _ea.Publish(new DomainChangedEvent { Type = DomainChangedType.Retour });

            // Le handler ne doit être appelé qu'une seule fois
            Assert.That(compteur, Is.EqualTo(1),
                "Un même handler inscrit deux fois ne doit être appelé qu'une fois");
        }
    }
}
