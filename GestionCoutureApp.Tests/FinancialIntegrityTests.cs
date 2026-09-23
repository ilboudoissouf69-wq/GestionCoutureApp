using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    /// <summary>
    /// Tests d'intégrité financière — lot 2 (migration 20260923000100).
    ///
    /// Couvre chaque correctif apporté :
    ///   0. Traçabilité création commande (IdOperateurCreation, NomOperateurCreation, DateCreation)
    ///   0b. Anti double-soumission commande (fenêtre 60 s)
    ///   1.  Détection doublon client (DuplicatClientException)
    ///   1b. RechercherDoublons rapport Boss
    ///   2.  Traçabilité Depense (IdOperateur obligatoire)
    ///   3.  Traçabilité MaterielSupplement (IdOperateur/NomOperateur)
    /// </summary>
    [TestFixture]
    public class FinancialIntegrityTests
    {
        // ── Infrastructure partagée ──────────────────────────────────────
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService _commandeService = null!;
        private ClientService   _clientService   = null!;
        private DepenseService  _depenseService  = null!;
        private MaterielService _materielService = null!;

        // Employés de référence
        private const int IdBoss       = 1;
        private const int IdSecretaire = 2;
        private const int IdClient1    = 1;
        private const int IdClient2    = 2;

        [SetUp]
        public void SetUp()
        {
            // Vider le cache anti-doublon statique entre tests
            CommandeService.ViderCacheDoublonPourTests();

            // Base en mémoire isolée par test (Guid unique)
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"FinancialIntegrity_{Guid.NewGuid()}")
                .Options;

            _factory = new FinancialTestDbContextFactory(options);

            var loggerFactory = NullLoggerFactory.Instance;
            _commandeService = new CommandeService(
                _factory, loggerFactory.CreateLogger<CommandeService>());
            _clientService = new ClientService(
                _factory, loggerFactory.CreateLogger<ClientService>());
            _depenseService  = new DepenseService(_factory);
            _materielService = new MaterielService(_factory);

            using var ctx = _factory.CreateDbContext();
            ctx.Employes.AddRange(
                new Employe
                {
                    IdEmploye = IdBoss, Nom = "DIALLO", Prenom = "Mamadou",
                    Identifiant = "boss", MotDePasse = "x", Role = "Boss", Statut = "Actif"
                },
                new Employe
                {
                    IdEmploye = IdSecretaire, Nom = "FALL", Prenom = "Marie",
                    Identifiant = "secretaire", MotDePasse = "x", Role = "Secretaire", Statut = "Actif"
                }
            );
            ctx.Clients.AddRange(
                new Client { IdClient = IdClient1, Nom = "SARR",  Prenom = "Fatou",   Telephone = "771234567" },
                new Client { IdClient = IdClient2, Nom = "DIALLO", Prenom = "Awa",    Telephone = "780000001" }
            );
            ctx.SaveChanges();
        }

        // ================================================================
        // 0. TRAÇABILITÉ CRÉATION COMMANDE
        // ================================================================

        [Test]
        public void T01_Ajouter_Commande_Renseigne_IdOperateurCreation()
        {
            // Arrange
            var (commande, piece) = FabriquerCommandePiece(IdClient1, "Robe", 15000m);

            // Act
            _commandeService.Ajouter(commande, piece, new List<Mesure>(),
                idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO");

            // Assert
            using var ctx = _factory.CreateDbContext();
            var stored = ctx.Commandes.First(c => c.IdCommande == commande.IdCommande);
            Assert.That(stored.IdOperateurCreation,  Is.EqualTo(IdBoss));
            Assert.That(stored.NomOperateurCreation, Is.EqualTo("Mamadou DIALLO"));
            Assert.That(stored.DateCreation,         Is.Not.EqualTo(default(DateTime)));
        }

        [Test]
        public void T02_Ajouter_Commande_NomOperateur_Vide_Est_Refusee()
        {
            var (commande, piece) = FabriquerCommandePiece(IdClient1, "Boubou", 12000m);

            Assert.Throws<InvalidOperationException>(() =>
                _commandeService.Ajouter(commande, piece, new List<Mesure>(),
                    idOperateur: IdBoss, nomOperateur: "  "));
        }

        [Test]
        public void T03_Ajouter_Commande_IdOperateur_Zero_Est_Refuse()
        {
            var (commande, piece) = FabriquerCommandePiece(IdClient1, "Pantalon", 8000m);

            Assert.Throws<InvalidOperationException>(() =>
                _commandeService.Ajouter(commande, piece, new List<Mesure>(),
                    idOperateur: 0, nomOperateur: "Quelqu'un"));
        }

        [Test]
        public void T04_Ajouter_Commande_DateCreation_Est_UTC()
        {
            var (commande, piece) = FabriquerCommandePiece(IdClient1, "Robe", 15000m);
            DateTime avant = DateTime.UtcNow.AddSeconds(-2);

            _commandeService.Ajouter(commande, piece, new List<Mesure>(),
                idOperateur: IdSecretaire, nomOperateur: "Marie FALL");

            DateTime apres = DateTime.UtcNow.AddSeconds(2);

            using var ctx = _factory.CreateDbContext();
            var stored = ctx.Commandes.First(c => c.IdCommande == commande.IdCommande);

            // DateCreation doit être dans la fenêtre avant/après l'appel
            Assert.That(stored.DateCreation, Is.GreaterThanOrEqualTo(avant));
            Assert.That(stored.DateCreation, Is.LessThanOrEqualTo(apres));
        }

        // ================================================================
        // 0b. ANTI DOUBLE-SOUMISSION (fenêtre 60 s)
        // ================================================================

        [Test]
        public void T05_Double_Soumission_Dans_Fenetre_Leve_DoublonCommandeException()
        {
            // Arrange : première création
            var (c1, p1) = FabriquerCommandePiece(IdClient1, "Robe", 15000m);
            _commandeService.Ajouter(c1, p1, new List<Mesure>(),
                idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO");

            // Act : deuxième création identique immédiatement
            var (c2, p2) = FabriquerCommandePiece(IdClient1, "Robe", 15000m);

            // Assert
            var ex = Assert.Throws<DoublonCommandeException>(() =>
                _commandeService.Ajouter(c2, p2, new List<Mesure>(),
                    idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO"));

            Assert.That(ex.Message, Does.Contain("Double envoi").Or.Contain("doublon").Or.Contain("Robe").IgnoreCase);
        }

        [Test]
        public void T06_Double_Soumission_Operateurs_Differents_Autorise()
        {
            // Deux opérateurs distincts créent la même commande : autorisé.
            var (c1, p1) = FabriquerCommandePiece(IdClient1, "Robe", 15000m);
            _commandeService.Ajouter(c1, p1, new List<Mesure>(),
                idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO");

            var (c2, p2) = FabriquerCommandePiece(IdClient1, "Robe", 15000m);
            Assert.DoesNotThrow(() =>
                _commandeService.Ajouter(c2, p2, new List<Mesure>(),
                    idOperateur: IdSecretaire, nomOperateur: "Marie FALL"));
        }

        [Test]
        public void T07_Double_Soumission_Montants_Differents_Autorise()
        {
            // Même client + type, mais montant différent → pas un doublon.
            var (c1, p1) = FabriquerCommandePiece(IdClient1, "Robe", 15000m);
            _commandeService.Ajouter(c1, p1, new List<Mesure>(),
                idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO");

            var (c2, p2) = FabriquerCommandePiece(IdClient1, "Robe", 20000m); // montant différent
            Assert.DoesNotThrow(() =>
                _commandeService.Ajouter(c2, p2, new List<Mesure>(),
                    idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO"));
        }

        // ================================================================
        // 1. DÉTECTION DOUBLON CLIENT (DuplicatClientException)
        // ================================================================

        [Test]
        public void T08_Ajouter_Client_Doublon_Fort_Leve_Exception()
        {
            // Client avec même nom normalisé + même téléphone → doublon fort
            var doublon = new Client
            {
                Nom = "sarr",       // casse différente volontaire
                Prenom = "fatou",   // même
                Telephone = "771234567"  // même → doublon fort
            };

            var ex = Assert.Throws<DuplicatClientException>(() =>
                _clientService.Ajouter(doublon));

            Assert.That(ex.ClientExistant.IdClient, Is.EqualTo(IdClient1));
            Assert.That(ex.Message, Does.Contain("similaire").Or.Contain("existe").IgnoreCase);
        }

        [Test]
        public void T09_Ajouter_Client_Doublon_Faible_Sans_Tel_Leve_Exception()
        {
            // Même nom, pas de téléphone côté nouveau et côté existant → doublon faible
            // D'abord on crée un client sans téléphone
            var sanstel = new Client { Nom = "CAMARA", Prenom = "Ibou", Telephone = "" };
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.Clients.Add(sanstel);
                ctx.SaveChanges();
            }

            // Tentative de recréer le même
            var doublon = new Client { Nom = "CAMARA", Prenom = "Ibou", Telephone = "" };
            Assert.Throws<DuplicatClientException>(() =>
                _clientService.Ajouter(doublon));
        }

        [Test]
        public void T10_Ajouter_Client_Meme_Nom_Telephone_Different_Est_Autorise()
        {
            // Même nom normalisé mais téléphone différent → PAS un doublon
            var autreClient = new Client
            {
                Nom = "SARR",
                Prenom = "Fatou",
                Telephone = "761111111"  // téléphone différent → homonyme
            };

            Assert.DoesNotThrow(() => _clientService.Ajouter(autreClient));
        }

        [Test]
        public void T11_Ajouter_Client_Nom_Different_Est_Autorise()
        {
            var autre = new Client
            {
                Nom = "SARR",
                Prenom = "Aminata",  // prénom différent
                Telephone = "771234567"
            };

            Assert.DoesNotThrow(() => _clientService.Ajouter(autre));
        }

        // ================================================================
        // 1b. RechercherDoublons — rapport Boss
        // ================================================================

        [Test]
        public void T12_RechercherDoublons_Retourne_Groupes_Corrects()
        {
            // Ajouter directement en base deux clients homonymes (sans passer par Ajouter
            // pour contourner la détection et simuler des doublons historiques).
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.Clients.AddRange(
                    new Client { Nom = "TRAORE", Prenom = "Seydou", Telephone = "771111111" },
                    new Client { Nom = "TRAORE", Prenom = "Seydou", Telephone = "772222222" }
                );
                ctx.SaveChanges();
            }

            var doublons = _clientService.RechercherDoublons();

            // Le groupe "seydou traore" doit apparaître avec 2 fiches
            var groupe = doublons.FirstOrDefault(g =>
                g.CleNormalise.Contains("traore") || g.CleNormalise.Contains("seydou"));

            Assert.That(groupe,          Is.Not.Null, "Groupe TRAORE/Seydou attendu");
            Assert.That(groupe!.Clients.Count, Is.EqualTo(2));
        }

        [Test]
        public void T13_RechercherDoublons_Aucun_Doublon_Retourne_Liste_Vide()
        {
            // Les clients en base (SARR Fatou et DIALLO Awa) n'ont pas le même nom
            var doublons = _clientService.RechercherDoublons();
            Assert.That(doublons, Is.Empty);
        }

        // ================================================================
        // 2. TRAÇABILITÉ DÉPENSE (IdOperateur obligatoire)
        // ================================================================

        [Test]
        public void T14_Ajouter_Depense_Sans_IdOperateur_Leve_Exception()
        {
            var depense = new Depense
            {
                TypeDepense   = "Loyer",
                Montant       = 50000m,
                DateDepense   = DateTime.Today,
                IdOperateur   = 0,       // manquant → doit être rejeté
                NomOperateur  = "Marie FALL",
                StatutValidation = "Validee"
            };

            Assert.Throws<InvalidOperationException>(() =>
                _depenseService.Ajouter(depense));
        }

        [Test]
        public void T15_Ajouter_Depense_Avec_IdOperateur_Valide_Reussit()
        {
            var depense = new Depense
            {
                TypeDepense   = "Loyer",
                Montant       = 50000m,
                DateDepense   = DateTime.Today,
                IdOperateur   = IdBoss,
                NomOperateur  = "Mamadou DIALLO",
                StatutValidation = "Validee"
            };

            Assert.DoesNotThrow(() => _depenseService.Ajouter(depense));

            using var ctx = _factory.CreateDbContext();
            var stored = ctx.Depenses.First(d => d.IdDepense == depense.IdDepense);
            Assert.That(stored.IdOperateur,  Is.EqualTo(IdBoss));
            Assert.That(stored.NomOperateur, Is.EqualTo("Mamadou DIALLO"));
        }

        // ================================================================
        // 3. TRAÇABILITÉ MATÉRIAU (IdOperateur/NomOperateur)
        // ================================================================

        [Test]
        public void T16_Ajouter_Materiau_Sans_IdOperateur_Leve_Exception()
        {
            // Créer une commande en base pour avoir un IdCommande valide
            int idCommande = CreerCommandeEnBase();

            var materiau = new MaterielSupplement
            {
                IdCommande   = idCommande,
                Designation  = "Tissu wax",
                Quantite     = 2,
                PrixUnitaire = 5000m
            };

            Assert.Throws<InvalidOperationException>(() =>
                _materielService.Ajouter(materiau, idOperateur: 0, nomOperateur: ""));
        }

        [Test]
        public void T17_Ajouter_Materiau_Renseigne_IdOperateur_Et_NomOperateur()
        {
            int idCommande = CreerCommandeEnBase();

            var materiau = new MaterielSupplement
            {
                IdCommande   = idCommande,
                Designation  = "Fil",
                Quantite     = 5,
                PrixUnitaire = 500m
            };

            _materielService.Ajouter(materiau, idOperateur: IdSecretaire, nomOperateur: "Marie FALL");

            using var ctx = _factory.CreateDbContext();
            var stored = ctx.MaterielsSupplements.First(m => m.IdMateriel == materiau.IdMateriel);
            Assert.That(stored.IdOperateur,  Is.EqualTo(IdSecretaire));
            Assert.That(stored.NomOperateur, Is.EqualTo("Marie FALL"));
        }

        // ================================================================
        // 4. COHÉRENCE GLOBALE : une commande créée a toujours un opérateur
        // ================================================================

        [Test]
        public void T18_Commande_Creee_Par_Secretaire_A_Son_IdOperateur()
        {
            var (commande, piece) = FabriquerCommandePiece(IdClient2, "Jupe", 10000m);

            _commandeService.Ajouter(commande, piece, new List<Mesure>(),
                idOperateur: IdSecretaire, nomOperateur: "Marie FALL");

            using var ctx = _factory.CreateDbContext();
            var stored = ctx.Commandes.First(c => c.IdCommande == commande.IdCommande);

            Assert.That(stored.IdOperateurCreation,  Is.EqualTo(IdSecretaire));
            Assert.That(stored.NomOperateurCreation, Is.EqualTo("Marie FALL"));
        }

        [Test]
        public void T19_Commande_Creee_Par_Boss_A_Son_IdOperateur()
        {
            var (commande, piece) = FabriquerCommandePiece(IdClient2, "Costard", 35000m);

            _commandeService.Ajouter(commande, piece, new List<Mesure>(),
                idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO");

            using var ctx = _factory.CreateDbContext();
            var stored = ctx.Commandes.First(c => c.IdCommande == commande.IdCommande);

            Assert.That(stored.IdOperateurCreation,  Is.EqualTo(IdBoss));
            Assert.That(stored.NomOperateurCreation, Is.EqualTo("Mamadou DIALLO"));
        }

        // ================================================================
        // 5. EXCEPTION DoublonCommandeException — propriétés exposées
        // ================================================================

        [Test]
        public void T20_DoublonCommandeException_Expose_CleEtEcart()
        {
            var (c1, p1) = FabriquerCommandePiece(IdClient1, "Chemise", 7000m);
            _commandeService.Ajouter(c1, p1, new List<Mesure>(),
                idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO");

            var (c2, p2) = FabriquerCommandePiece(IdClient1, "Chemise", 7000m);
            var ex = Assert.Throws<DoublonCommandeException>(() =>
                _commandeService.Ajouter(c2, p2, new List<Mesure>(),
                    idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO"));

            // La clé doit contenir les composants qui font le doublon
            Assert.That(ex.CleDoublon, Does.Contain(IdBoss.ToString()));
            Assert.That(ex.CleDoublon, Does.Contain(IdClient1.ToString()));
            Assert.That(ex.CleDoublon, Does.Contain("Chemise"));
            // L'écart doit être très court (test s'exécute en moins d'1 seconde)
            Assert.That(ex.EcartDepuisDerniereCreation, Is.LessThan(TimeSpan.FromSeconds(5)));
        }

        // ================================================================
        // 6. DuplicatClientException — propriétés exposées
        // ================================================================

        [Test]
        public void T21_DuplicatClientException_Expose_ClientExistant()
        {
            var doublon = new Client
            {
                Nom = "SARR", Prenom = "Fatou", Telephone = "771234567"
            };

            var ex = Assert.Throws<DuplicatClientException>(() =>
                _clientService.Ajouter(doublon));

            Assert.That(ex.ClientExistant,         Is.Not.Null);
            Assert.That(ex.ClientExistant.IdClient, Is.EqualTo(IdClient1));
            Assert.That(ex.ClientExistant.Nom,      Is.EqualTo("SARR"));
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static (Commande, PieceCommande) FabriquerCommandePiece(
            int idClient, string typeVetement, decimal montant)
        {
            var commande = new Commande
            {
                IdClient  = idClient,
                DateDebut = DateTime.Now,
                DateFin   = DateTime.Now.AddDays(7)
            };
            var piece = new PieceCommande
            {
                TypeVetement   = typeVetement,
                MontantCouture = montant,
                Statut         = "A faire"
            };
            return (commande, piece);
        }

        private int CreerCommandeEnBase()
        {
            var (commande, piece) = FabriquerCommandePiece(IdClient1, "Robe", 15000m);
            _commandeService.Ajouter(commande, piece, new List<Mesure>(),
                idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO");
            return commande.IdCommande;
        }
    }

    // ================================================================
    // Infrastructure de test partagée avec SecurityTests
    // ================================================================

    /// <summary>
    /// Factory de contexte EF Core en mémoire pour les tests d'intégrité financière.
    /// Pattern identique à <see cref="TestDbContextFactory"/> dans SecurityTests.cs,
    /// défini ici pour éviter la dépendance inter-fichiers dans NUnit.
    /// </summary>
    internal class FinancialTestDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;

        public FinancialTestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
            => _options = options;

        public ApplicationDbContext CreateDbContext()
            => new ApplicationDbContext(_options);
    }
}
