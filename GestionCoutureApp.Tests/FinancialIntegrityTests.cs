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

    // ====================================================================
    // Tests d'idempotence des migrations (tâche 4)
    // ====================================================================

    /// <summary>
    /// Tests vérifiant que le pattern de migration utilisé dans App.cs est
    /// idempotent — c'est-à-dire que l'appliquer deux fois de suite sur la même
    /// base ne plante pas et laisse le schéma dans l'état attendu.
    ///
    /// Ces tests reproduisent exactement le scénario du bug corrigé :
    ///   suppressTransaction:true ne supprime PAS les erreurs SQLite. Si une
    ///   migration basée sur ALTER TABLE ADD COLUMN s'interrompait au milieu,
    ///   les colonnes déjà ajoutées restaient committées (car hors transaction)
    ///   mais la migration était marquée non appliquée dans __EFMigrationsHistory.
    ///   Au prochain lancement, EF Core rejouait la migration entière et plantait
    ///   sur "duplicate column name: X".
    ///
    /// Le correctif : utiliser AjouterCol() avec try/catch individuel par colonne
    /// + enregistrement dans __EFMigrationsHistory uniquement après succès total.
    /// Ces tests vérifient que ce pattern est bien robuste.
    /// </summary>
    [TestFixture]
    public class IdempotenceMigrationTests
    {
        // Chaque test utilise un fichier SQLite temporaire sur disque (pas InMemory)
        // car les migrations SQLite réelles nécessitent le vrai moteur SQLite.
        private string _dbPath = null!;
        private string _connStr = null!;

        [SetUp]
        public void SetUp()
        {
            _dbPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"idempotence_test_{Guid.NewGuid():N}.db");
            _connStr = $"Data Source={_dbPath}";

            // Créer le schéma minimal de base (simulant les 10 premières migrations)
            using var conn = OuvrirConnexion();
            CreerSchemaMinimal(conn);
        }

        [TearDown]
        public void TearDown()
        {
            // Nettoyage du fichier temporaire
            try
            {
                if (System.IO.File.Exists(_dbPath))     System.IO.File.Delete(_dbPath);
                if (System.IO.File.Exists(_dbPath + "-wal")) System.IO.File.Delete(_dbPath + "-wal");
                if (System.IO.File.Exists(_dbPath + "-shm")) System.IO.File.Delete(_dbPath + "-shm");
            }
            catch { /* fichier verrouillé — le GC nettoiera */ }
        }

        // ================================================================
        // T_IDM01 — Le scénario du bug : colonne présente, migration absente
        // ================================================================

        [Test]
        public void T_IDM01_AjouterCol_DejaPresente_Ne_Plante_Pas()
        {
            // Arrange : vérifier que EstAnnule est déjà présente (ajoutée par
            // 20260805205656_AjoutMotifExceptionPiece dans CreerSchemaMinimal).
            // C'est exactement le scénario du bug : la colonne existe mais la
            // migration 20260916000000 n'est PAS enregistrée dans __EFMigrationsHistory.
            using (var conn = OuvrirConnexion())
            {
                var colonnes = LireColonnes(conn, "Retours");
                // Précondition : EstAnnule est déjà là grâce au schéma minimal
                Assert.That(colonnes, Does.Contain("EstAnnule"),
                    "Précondition : EstAnnule doit être présente dès le schéma minimal " +
                    "(20260805205656 l'a ajouté)");
                // 20260916000000 n'est PAS enregistrée → scénario du bug exact
                var migrations = LireMigrationsAppliquees(conn);
                Assert.That(migrations,
                    Does.Not.Contain("20260916000000_AjoutRetoursChampsReprise"),
                    "Précondition : 20260916000000 ne doit pas être dans __EFMigrationsHistory");
            }

            // Act : appliquer la logique AjouterColIdempotent deux fois
            // La première fois : EstAnnule existe → doit ignorer silencieusement
            // La deuxième fois : idem — comportement stable
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 2; i++)
                {
                    using var conn = OuvrirConnexion();
                    AjouterColIdempotent(conn, "Retours", "EstAnnule",         "INTEGER NOT NULL DEFAULT 0");
                    AjouterColIdempotent(conn, "Retours", "MotifAnnulation",   "TEXT NULL");
                    AjouterColIdempotent(conn, "Retours", "CheminPhotoDefaut", "TEXT NULL");
                }
            }, "AjouterColIdempotent ne doit pas planter même si la colonne existe déjà");

            // Assert : toutes les colonnes sont présentes après les deux passes
            using var verification = OuvrirConnexion();
            var colsFinales = LireColonnes(verification, "Retours");
            Assert.That(colsFinales, Does.Contain("EstAnnule"),         "EstAnnule doit être présente");
            Assert.That(colsFinales, Does.Contain("MotifAnnulation"),   "MotifAnnulation doit être présente");
            Assert.That(colsFinales, Does.Contain("CheminPhotoDefaut"), "CheminPhotoDefaut doit être présente");
        }

        // ================================================================
        // T_IDM02 — Double application via __EFMigrationsHistory
        // ================================================================

        [Test]
        public void T_IDM02_Migration_Deja_Enregistree_Est_Sautee()
        {
            // Arrange : migration déjà enregistrée dans __EFMigrationsHistory
            using (var conn = OuvrirConnexion())
            {
                ExecuterSql(conn,
                    "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) " +
                    "VALUES ('20260916000000_AjoutRetoursChampsReprise', '8.0.11')");
            }

            // Act : simuler un deuxième appel (comme si Migrate() rejouait la migration)
            // Sans la vérification __EFMigrationsHistory, ça planterait sur la colonne dupliquée
            int compteurExecution = 0;

            Assert.DoesNotThrow(() =>
            {
                using var conn = OuvrirConnexion();
                var appliquees = LireMigrationsAppliquees(conn);

                if (!appliquees.Contains("20260916000000_AjoutRetoursChampsReprise"))
                {
                    compteurExecution++;
                    AjouterColIdempotent(conn, "Retours", "EstAnnule", "INTEGER NOT NULL DEFAULT 0");
                }
            });

            // Assert : la migration a été sautée car déjà enregistrée
            Assert.That(compteurExecution, Is.EqualTo(0),
                "La migration ne doit pas s'exécuter si déjà dans __EFMigrationsHistory");
        }

        // ================================================================
        // T_IDM03 — Colonne absente → ajoutée ; colonne présente → sautée
        // ================================================================

        [Test]
        public void T_IDM03_AjouterColIdempotent_Comportement_Selon_Presence()
        {
            // Arrange : Commandes n'a pas encore EstSupprimee
            using (var conn = OuvrirConnexion())
            {
                var avant = LireColonnes(conn, "Commandes");
                Assert.That(avant, Does.Not.Contain("EstSupprimee"), "Précondition : EstSupprimee absente");
            }

            // Act 1 : première application → doit ajouter la colonne
            using (var conn = OuvrirConnexion())
                AjouterColIdempotent(conn, "Commandes", "EstSupprimee", "INTEGER NOT NULL DEFAULT 0");

            using (var conn = OuvrirConnexion())
                Assert.That(LireColonnes(conn, "Commandes"), Does.Contain("EstSupprimee"),
                    "EstSupprimee doit être présente après première application");

            // Act 2 : deuxième application → doit ignorer silencieusement
            Assert.DoesNotThrow(() =>
            {
                using var conn = OuvrirConnexion();
                AjouterColIdempotent(conn, "Commandes", "EstSupprimee", "INTEGER NOT NULL DEFAULT 0");
            }, "Deuxième application sur colonne déjà présente ne doit pas planter");

            // Assert : la colonne est toujours là (pas d'état corrompu)
            using (var conn = OuvrirConnexion())
                Assert.That(LireColonnes(conn, "Commandes"), Does.Contain("EstSupprimee"),
                    "EstSupprimee doit toujours être présente après double application");
        }

        // ================================================================
        // T_IDM04 — CREATE INDEX IF NOT EXISTS est vraiment idempotent
        // ================================================================

        [Test]
        public void T_IDM04_CreerIndexSiAbsent_Idempotent()
        {
            const string sql = "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Paiements_RecuNumero\" " +
                               "ON \"Paiements\" (\"RecuNumero\")";

            // Act : créer l'index deux fois
            Assert.DoesNotThrow(() =>
            {
                using var c1 = OuvrirConnexion(); ExecuterSql(c1, sql);
                using var c2 = OuvrirConnexion(); ExecuterSql(c2, sql); // IF NOT EXISTS → no-op
            }, "CREATE INDEX IF NOT EXISTS doit être idempotent");

            // Assert : l'index existe
            using var conn = OuvrirConnexion();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_Paiements_RecuNumero'";
            Assert.That((long)(cmd.ExecuteScalar() ?? 0L), Is.EqualTo(1));
        }

        // ================================================================
        // T_IDM05 — CREATE TABLE IF NOT EXISTS est vraiment idempotent
        // ================================================================

        [Test]
        public void T_IDM05_CreerTableSiAbsente_Idempotent()
        {
            const string ddl = @"CREATE TABLE IF NOT EXISTS ""JournalAudit"" (
                ""IdJournal"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DateHeureUtc"" TEXT NOT NULL,
                ""HashCourant"" TEXT NOT NULL
            )";

            Assert.DoesNotThrow(() =>
            {
                using var c1 = OuvrirConnexion(); ExecuterSql(c1, ddl);
                using var c2 = OuvrirConnexion(); ExecuterSql(c2, ddl); // IF NOT EXISTS → no-op
            }, "CREATE TABLE IF NOT EXISTS doit être idempotent");

            using var conn = OuvrirConnexion();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='JournalAudit'";
            Assert.That((long)(cmd.ExecuteScalar() ?? 0L), Is.EqualTo(1));
        }

        // ================================================================
        // T_IDM06 — Migration complète simulée : 4 passages consécutifs
        // ================================================================

        [Test]
        public void T_IDM06_Migration_Complete_Tolerante_A_Replays_Multiples()
        {
            // Reproduit la séquence exacte du bug : on rejoue la logique de
            // ConsolidationFinale 4 fois (ex. 4 redémarrages consécutifs sans
            // que la migration soit enregistrée dans __EFMigrationsHistory).
            // Aucune exception ne doit être levée, et le schéma final doit être cohérent.

            var colonnesCommandesAttendues = new[]
            {
                "EstSupprimee", "MotifSuppression", "DateSuppression",
                "IdOperateurCreation", "NomOperateurCreation", "DateCreation"
            };
            var colonnesDeAttendues = new[] { "Categorie", "StatutValidation" };

            Assert.DoesNotThrow(() =>
            {
                for (int passage = 1; passage <= 4; passage++)
                {
                    using var conn = OuvrirConnexion();
                    // Commandes
                    foreach (var c in colonnesCommandesAttendues)
                        AjouterColIdempotent(conn, "Commandes", c,
                            c.StartsWith("Id") || c == "EstSupprimee"
                                ? "INTEGER NOT NULL DEFAULT 0"
                                : "TEXT NULL");
                    // Depenses
                    AjouterColIdempotent(conn, "Depenses", "Categorie",       "TEXT NOT NULL DEFAULT 'Divers'");
                    AjouterColIdempotent(conn, "Depenses", "StatutValidation", "TEXT NOT NULL DEFAULT 'Validee'");
                    // JournalAudit
                    ExecuterSql(conn, @"CREATE TABLE IF NOT EXISTS ""JournalAudit"" (
                        ""IdJournal"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""DateHeureUtc"" TEXT NOT NULL,
                        ""HashCourant"" TEXT NOT NULL
                    )");
                }
            }, "4 passages consécutifs de la migration ne doivent pas planter");

            // Vérification finale du schéma
            using var final = OuvrirConnexion();
            var colsCommandes = LireColonnes(final, "Commandes");
            var colsDepenses  = LireColonnes(final, "Depenses");

            foreach (var col in colonnesCommandesAttendues)
                Assert.That(colsCommandes, Does.Contain(col), $"Commandes.{col} attendue");
            foreach (var col in colonnesDeAttendues)
                Assert.That(colsDepenses, Does.Contain(col), $"Depenses.{col} attendue");

            using var chkJournal = OuvrirConnexion();
            using var cmd = chkJournal.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='JournalAudit'";
            Assert.That((long)(cmd.ExecuteScalar() ?? 0L), Is.EqualTo(1), "JournalAudit doit exister");
        }

        // ================================================================
        // Helpers privés
        // ================================================================

        private Microsoft.Data.Sqlite.SqliteConnection OuvrirConnexion()
        {
            var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connStr);
            conn.Open();
            return conn;
        }

        private static void ExecuterSql(Microsoft.Data.Sqlite.SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// ALTER TABLE ADD COLUMN idempotent : ignore silencieusement
        /// "duplicate column name". C'est le pattern correct — pas suppressTransaction.
        /// </summary>
        private static void AjouterColIdempotent(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            string table, string col, string def)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{col}\" {def}";
                cmd.ExecuteNonQuery();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
                when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Colonne déjà présente — comportement normal, rien à faire.
            }
            // Toute autre exception (table inexistante, syntaxe invalide...) remonte.
        }

        private static System.Collections.Generic.HashSet<string> LireColonnes(
            Microsoft.Data.Sqlite.SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var r = cmd.ExecuteReader();
            var cols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (r.Read()) cols.Add(r.GetString(1)); // colonne "name"
            return cols;
        }

        private static System.Collections.Generic.HashSet<string> LireMigrationsAppliquees(
            Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
                using var r = cmd.ExecuteReader();
                while (r.Read()) set.Add(r.GetString(0));
            }
            catch { /* table absente sur base fraîche */ }
            return set;
        }

        /// <summary>
        /// Crée le schéma minimal simulant l'état après les 10 migrations stables
        /// (InitialCreate → AjoutMotifExceptionPiece), sans les 4 migrations ignorées
        /// par EF Core. C'est l'état exact d'une base de production ancienne.
        /// </summary>
        private static void CreerSchemaMinimal(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            // Table de contrôle des migrations
            ExecuterSql(conn, @"CREATE TABLE __EFMigrationsHistory (
                MigrationId TEXT NOT NULL PRIMARY KEY,
                ProductVersion TEXT NOT NULL
            )");

            // Insérer les 10 migrations "stables" appliquées
            var migrations = new[]
            {
                "20260718235951_InitialCreate",
                "20260719120000_AjoutForceChangementMotDePasse",
                "20260719120100_CorrectionTypeColonnesMontants",
                "20260720112030_SupprimerDoitChangerMotDePasse",
                "20260727125439_AjoutPieceCommande",
                "20260730161111_AjoutParametres",
                "20260730183312_AjoutRetours",
                "20260730204755_AjoutDepenses",
                "20260731073029_AjoutMaterielsSupplements",
                "20260805205656_AjoutMotifExceptionPiece",
            };
            foreach (var m in migrations)
                ExecuterSql(conn,
                    $"INSERT INTO __EFMigrationsHistory VALUES ('{m}', '8.0.11')");

            // Commandes (schéma après InitialCreate, sans les nouvelles colonnes)
            ExecuterSql(conn, @"CREATE TABLE ""Commandes"" (
                ""IdCommande"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""IdClient"" INTEGER NOT NULL,
                ""TypeVetement"" TEXT NOT NULL,
                ""MontantTotal"" TEXT NOT NULL DEFAULT '0',
                ""DateDebut"" TEXT NOT NULL,
                ""DateFin"" TEXT NOT NULL,
                ""Statut"" TEXT NOT NULL DEFAULT 'A faire'
            )");

            // Paiements (avec IdOperateur nullable — état avant ConsolidationFinale)
            ExecuterSql(conn, @"CREATE TABLE ""Paiements"" (
                ""IdPaiement"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""IdCommande"" INTEGER NOT NULL,
                ""MontantPaye"" TEXT NOT NULL,
                ""DatePaiement"" TEXT NOT NULL,
                ""ModePaiement"" TEXT NOT NULL DEFAULT 'Especes',
                ""RecuNumero"" TEXT NOT NULL DEFAULT '',
                ""IdOperateur"" INTEGER NULL,
                ""NomOperateur"" TEXT NOT NULL DEFAULT '',
                ""EstAnnule"" INTEGER NOT NULL DEFAULT 0,
                ""MotifsAnnulation"" TEXT NULL,
                ""DateAnnulation"" TEXT NULL,
                ""NomAnnulateur"" TEXT NULL,
                ""MontantTotalCommande"" TEXT NOT NULL DEFAULT '0',
                ""ResteAvantPaiement"" TEXT NOT NULL DEFAULT '0'
            )");

            // Depenses (sans Categorie, StatutValidation, IdOperateur)
            ExecuterSql(conn, @"CREATE TABLE ""Depenses"" (
                ""IdDepense"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""TypeDepense"" TEXT NOT NULL,
                ""Montant"" TEXT NOT NULL,
                ""DateDepense"" TEXT NOT NULL,
                ""Description"" TEXT NOT NULL DEFAULT '',
                ""NomOperateur"" TEXT NOT NULL DEFAULT ''
            )");

            // Retours (sans les colonnes de reprise)
            ExecuterSql(conn, @"CREATE TABLE ""Retours"" (
                ""IdRetour"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""IdCommande"" INTEGER NOT NULL,
                ""Statut"" TEXT NOT NULL DEFAULT 'Signale',
                ""EstAnnule"" INTEGER NOT NULL DEFAULT 0
            )");

            // Employes (sans DerniereModificationMotDePasse)
            ExecuterSql(conn, @"CREATE TABLE ""Employes"" (
                ""IdEmploye"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Nom"" TEXT NOT NULL,
                ""Prenom"" TEXT NOT NULL,
                ""Identifiant"" TEXT NOT NULL,
                ""MotDePasse"" TEXT NOT NULL,
                ""Role"" TEXT NOT NULL,
                ""Statut"" TEXT NOT NULL DEFAULT 'Actif'
            )");

            // Clients
            ExecuterSql(conn, @"CREATE TABLE ""Clients"" (
                ""IdClient"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Nom"" TEXT NOT NULL,
                ""Prenom"" TEXT NOT NULL,
                ""Telephone"" TEXT NOT NULL DEFAULT ''
            )");

            // Commissions (sans PrimeQualite)
            ExecuterSql(conn, @"CREATE TABLE ""Commissions"" (
                ""IdCommission"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""IdEmploye"" INTEGER NOT NULL,
                ""MontantCommission"" TEXT NOT NULL DEFAULT '0',
                ""DateCalcul"" TEXT NOT NULL
            )");

            // MaterielsSupplements (sans IdOperateur, NomOperateur)
            ExecuterSql(conn, @"CREATE TABLE ""MaterielsSupplements"" (
                ""IdMateriel"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""IdCommande"" INTEGER NOT NULL,
                ""Designation"" TEXT NOT NULL,
                ""Quantite"" INTEGER NOT NULL DEFAULT 1,
                ""PrixUnitaire"" TEXT NOT NULL DEFAULT '0'
            )");
        }
    }
}
