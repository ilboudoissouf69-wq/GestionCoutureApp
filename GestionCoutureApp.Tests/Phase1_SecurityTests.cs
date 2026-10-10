using GestionCoutureApp.Models;
using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace GestionCoutureApp.Tests
{
    /// <summary>
    /// Tests Phase 1 — Sécurité critique.
    /// Règles testées :
    ///   - Longueur minimale mot de passe (10 caractères)
    ///   - Liste noire des mots de passe faibles
    ///   - Verrouillage progressif (paliers 5/10/15/20 échecs)
    ///   - CommissionService.Annuler par ID (pas par nom)
    ///   - PaiementService.Ajouter refuse Couturier et ID inexistant
    /// </summary>
    [TestFixture]
    public class Phase1_SecurityTests
    {
        private IDbContextFactory<ApplicationDbContext> _contextFactory = null!;
        private PaiementService _paiementService = null!;
        private CommissionService _commissionService = null!;
        private AuthService _authService = null!;
        private ApplicationDbContext _context = null!;

        // IDs fixes pour les tests
        private const int IdBoss = 1;
        private const int IdSecretaire = 2;
        private const int IdCouturier = 3;
        private const int IdBossInactif = 4;

        [SetUp]
        public void Setup()
        {
            CommandeService.ViderCacheDoublonPourTests();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"Phase1_{Guid.NewGuid()}")
                .Options;

            _contextFactory = new Phase1DbContextFactory(options);
            _context = _contextFactory.CreateDbContext();

            var loggerFactory = NullLoggerFactory.Instance;
            _paiementService = new PaiementService(
                _contextFactory, loggerFactory.CreateLogger<PaiementService>());
            _commissionService = new CommissionService(
                _contextFactory, loggerFactory.CreateLogger<CommissionService>());
            _authService = new AuthService(
                _contextFactory, loggerFactory.CreateLogger<AuthService>());

            SeedData();
        }

        private void SeedData()
        {
            _context.Employes.AddRange(
                new Employe
                {
                    IdEmploye = IdBoss, Nom = "TRAORE", Prenom = "Ibrahim",
                    Identifiant = "boss_test",
                    MotDePasse = PasswordHasher.Hasher("MotDePasseSecur!2026"),
                    Role = "Boss", Statut = "Actif"
                },
                new Employe
                {
                    IdEmploye = IdSecretaire, Nom = "OUEDRAOGO", Prenom = "Fatoumata",
                    Identifiant = "secretaire_test",
                    MotDePasse = PasswordHasher.Hasher("MotDePasseSecur!2026"),
                    Role = "Secretaire", Statut = "Actif"
                },
                new Employe
                {
                    IdEmploye = IdCouturier, Nom = "KABORE", Prenom = "Dramane",
                    Identifiant = "couturier_test",
                    MotDePasse = PasswordHasher.Hasher("MotDePasseSecur!2026"),
                    Role = "Couturier", Statut = "Actif"
                },
                new Employe
                {
                    IdEmploye = IdBossInactif, Nom = "ZONGO", Prenom = "Rasmane",
                    Identifiant = "boss_inactif",
                    MotDePasse = PasswordHasher.Hasher("MotDePasseSecur!2026"),
                    Role = "Boss", Statut = "Inactif"
                }
            );
            _context.Clients.Add(new Client
            {
                IdClient = 1, Nom = "CLIENT", Prenom = "Test", Telephone = "70000001"
            });
            _context.SaveChanges();
        }

        [TearDown]
        public void TearDown() => _context?.Dispose();

        // ================================================================
        // TESTS — Longueur minimale du mot de passe (10 caractères)
        // ================================================================

        [Test]
        public void MDP01_MotDePasse_Inferieur_10_CaractersRejete()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                AuthService.ValiderForceMotDePasse("Abc12345")); // 8 caractères
            Assert.That(ex.Message, Does.Contain("10").IgnoreCase);
        }

        [Test]
        public void MDP02_MotDePasse_Exactement_10_Caracteres_Accepte()
        {
            Assert.DoesNotThrow(() =>
                AuthService.ValiderForceMotDePasse("AbcDef1234")); // 10 caractères
        }

        [Test]
        public void MDP03_MotDePasse_Contenant_boss123_Rejete()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                AuthService.ValiderForceMotDePasse("Xboss123YZ!!")); // contient boss123
            Assert.That(ex.Message, Does.Contain("prévisible").IgnoreCase
                .Or.Contain("faible").IgnoreCase);
        }

        [Test]
        public void MDP04_MotDePasse_Contenant_couture_Rejete()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                AuthService.ValiderForceMotDePasse("CoutureAtelier2026"));
            Assert.That(ex.Message, Does.Contain("prévisible").IgnoreCase
                .Or.Contain("faible").IgnoreCase);
        }

        [Test]
        public void MDP05_MotDePasse_Fort_Accepte()
        {
            // Pas de mot interdit, 10+ caractères
            Assert.DoesNotThrow(() =>
                AuthService.ValiderForceMotDePasse("Xtr@2026_Securite!"));
        }

        [Test]
        public void MDP06_ChangerMotDePasse_Longueur9_Rejete()
        {
            // La méthode ChangerMotDePasse doit aussi appliquer la règle des 10 caractères
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _authService.ChangerMotDePasse(IdBoss, "MotDePasseSecur!2026", "Abc123456"));
            Assert.That(ex.Message, Does.Contain("10").IgnoreCase);
        }

        // ================================================================
        // TESTS — CommissionService.Annuler par ID (pas par nom)
        // ================================================================

        private Commission CreerCommissionTest(int idCommission)
        {
            return new Commission
            {
                IdCommission    = idCommission,
                IdEmploye       = IdCouturier,
                DateCalcul      = DateTime.Now,
                DateDebutPeriode = DateTime.Now.AddMonths(-1),
                DateFinPeriode  = DateTime.Now,
                MontantCommission = 5000m,
                EstAnnulee      = false,
                NomEmployeSnapshot = "Dramane KABORE",
                NbCommandes     = 0,
                IdOperateur     = IdBoss,
                NomOperateur    = "Ibrahim TRAORE"
            };
        }

        [Test]
        public void COM01_Annuler_Commission_Par_ID_Boss_Succes()
        {
            var commission = CreerCommissionTest(1);
            _context.Commissions.Add(commission);
            _context.SaveChanges();

            Assert.DoesNotThrow(() =>
                _commissionService.Annuler(1, "Test annulation Phase1", IdBoss, "Ibrahim TRAORE"));

            // Créer un nouveau contexte pour lire (évite le cache EF de _context)
            using var ctx2 = _contextFactory.CreateDbContext();
            var updated = ctx2.Commissions.Find(1);
            Assert.That(updated!.EstAnnulee, Is.True);
        }

        [Test]
        public void COM02_Annuler_Commission_Par_Secretaire_Rejete()
        {
            var commission = CreerCommissionTest(2);
            _context.Commissions.Add(commission);
            _context.SaveChanges();

            // La secrétaire ne peut pas annuler une commission
            Assert.Throws<UnauthorizedAccessException>(() =>
                _commissionService.Annuler(2, "Motif", IdSecretaire, "Fatoumata OUEDRAOGO"));
        }

        [Test]
        public void COM03_Annuler_Commission_Operateur_Inactif_Rejete()
        {
            var commission = CreerCommissionTest(3);
            _context.Commissions.Add(commission);
            _context.SaveChanges();

            // Le Boss inactif ne peut pas annuler
            Assert.Throws<UnauthorizedAccessException>(() =>
                _commissionService.Annuler(3, "Motif", IdBossInactif, "Rasmane ZONGO"));
        }

        [Test]
        public void COM04_Annuler_Commission_ID_Inexistant_Rejete()
        {
            // ID opérateur inexistant → rejeté
            var commission = CreerCommissionTest(4);
            _context.Commissions.Add(commission);
            _context.SaveChanges();

            Assert.Throws<UnauthorizedAccessException>(() =>
                _commissionService.Annuler(4, "Motif", 99999, "Inconnu"));
        }

        // ================================================================
        // TESTS — PaiementService.Ajouter refuse Couturier et ID inexistant
        // ================================================================

        private (Commande, PieceCommande) CreerCommandeAvecPiece()
        {
            var commande = new Commande
            {
                IdClient = 1,
                DateDebut = DateTime.Now,
                DateFin = DateTime.Now.AddDays(7),
                Statut = "A faire"
            };
            var piece = new PieceCommande
            {
                TypeVetement = "Pantalon",
                MontantCouture = 5000m,
                Statut = "A faire"
            };
            commande.Pieces = new List<PieceCommande> { piece };
            _context.Commandes.Add(commande);
            _context.SaveChanges();
            return (commande, piece);
        }

        [Test]
        public void PAI01_Ajouter_Par_Couturier_Rejete()
        {
            var (commande, _) = CreerCommandeAvecPiece();

            var ex = Assert.Throws<UnauthorizedAccessException>(() =>
                _paiementService.Ajouter(
                    new Paiement
                    {
                        IdCommande = commande.IdCommande,
                        MontantPaye = 2000m,
                        ModePaiement = "Especes"
                    },
                    idOperateur: IdCouturier,
                    nomOperateur: "Dramane KABORE"
                ));

            Assert.That(ex.Message, Does.Contain("Accès refusé").IgnoreCase
                .Or.Contain("rôle").IgnoreCase);
        }

        [Test]
        public void PAI02_Ajouter_Par_Operateur_ID_Inexistant_Rejete()
        {
            var (commande, _) = CreerCommandeAvecPiece();

            // L'ID 99999 n'existe pas
            Assert.Throws<UnauthorizedAccessException>(() =>
                _paiementService.Ajouter(
                    new Paiement
                    {
                        IdCommande = commande.IdCommande,
                        MontantPaye = 2000m,
                        ModePaiement = "Especes"
                    },
                    idOperateur: 99999,
                    nomOperateur: "Fantome"
                ));
        }

        [Test]
        public void PAI03_Ajouter_Par_Operateur_Inactif_Rejete()
        {
            var (commande, _) = CreerCommandeAvecPiece();

            var ex = Assert.Throws<UnauthorizedAccessException>(() =>
                _paiementService.Ajouter(
                    new Paiement
                    {
                        IdCommande = commande.IdCommande,
                        MontantPaye = 2000m,
                        ModePaiement = "Especes"
                    },
                    idOperateur: IdBossInactif,
                    nomOperateur: "Rasmane ZONGO"
                ));

            Assert.That(ex.Message, Does.Contain("inactif").IgnoreCase);
        }

        [Test]
        public void PAI04_Ajouter_Par_Boss_Accepte()
        {
            var (commande, _) = CreerCommandeAvecPiece();

            Assert.DoesNotThrow(() =>
                _paiementService.Ajouter(
                    new Paiement
                    {
                        IdCommande = commande.IdCommande,
                        MontantPaye = 2000m,
                        ModePaiement = "Especes"
                    },
                    idOperateur: IdBoss,
                    nomOperateur: "Ibrahim TRAORE"
                ));
        }

        [Test]
        public void PAI05_Ajouter_Par_Secretaire_Accepte()
        {
            var (commande, _) = CreerCommandeAvecPiece();

            Assert.DoesNotThrow(() =>
                _paiementService.Ajouter(
                    new Paiement
                    {
                        IdCommande = commande.IdCommande,
                        MontantPaye = 2000m,
                        ModePaiement = "Especes"
                    },
                    idOperateur: IdSecretaire,
                    nomOperateur: "Fatoumata OUEDRAOGO"
                ));
        }

        // ================================================================
        // TESTS — Verrouillage progressif (utilise AuthService sur InMemory)
        // ================================================================

        [Test]
        public void VER01_5_Echecs_Consecutifs_Bloquent_Compte()
        {
            // 5 tentatives échouées → verrouillage au prochain appel
            for (int i = 0; i < 5; i++)
                _authService.Authentifier("boss_test", "mauvaisMDP_12345");

            // Le 6e appel doit lever CompteVerrouilleException
            var ex = Assert.Throws<CompteVerrouilleException>(() =>
                _authService.Authentifier("boss_test", "mauvaisMDP_12345"));

            Assert.That(ex.TempsRestant.TotalSeconds, Is.GreaterThan(0));
        }

        [Test]
        public void VER02_Connexion_Reussie_Remet_Compteur_A_Zero()
        {
            // 4 échecs (pas encore verrouillé)
            for (int i = 0; i < 4; i++)
                _authService.Authentifier("boss_test", "mauvaisMDP_12345");

            // Connexion réussie
            var employe = _authService.Authentifier("boss_test", "MotDePasseSecur!2026");
            Assert.That(employe, Is.Not.Null);

            // Vérifier que le compteur est à 0 en base
            using var ctx = _contextFactory.CreateDbContext();
            var emp = ctx.Employes.FirstOrDefault(e => e.Identifiant == "boss_test");
            Assert.That(emp!.NbEchecConnexion, Is.EqualTo(0));
            Assert.That(emp.DateVerrouJusqua, Is.Null);
        }

        [Test]
        public void VER03_Compteur_Echecs_Persiste_En_Base()
        {
            // 3 échecs
            for (int i = 0; i < 3; i++)
                _authService.Authentifier("boss_test", "mauvaisMDP_12345");

            // Vérifier la valeur persistée
            using var ctx = _contextFactory.CreateDbContext();
            var emp = ctx.Employes.FirstOrDefault(e => e.Identifiant == "boss_test");
            Assert.That(emp!.NbEchecConnexion, Is.EqualTo(3));
        }

        [Test]
        public void VER04_Verrouillage_Deuxieme_Vague_Verrou_Reactif()
        {
            // 5 premiers échecs → verrou 30s posé
            for (int i = 0; i < 5; i++)
                _authService.Authentifier("boss_test", "mauvaisX_12345");

            // Vérifier que NbEchecs = 5 et DateVerrouJusqua est posé
            using (var ctx = _contextFactory.CreateDbContext())
            {
                var emp = ctx.Employes.FirstOrDefault(e => e.Identifiant == "boss_test");
                Assert.That(emp!.NbEchecConnexion, Is.EqualTo(5));
                Assert.That(emp.DateVerrouJusqua, Is.Not.Null, "Un verrou doit être posé après 5 échecs.");
            }

            // Simuler l'expiration du premier verrou en base
            using (var ctx = _contextFactory.CreateDbContext())
            {
                var emp = ctx.Employes.FirstOrDefault(e => e.Identifiant == "boss_test");
                emp!.DateVerrouJusqua = DateTime.UtcNow.AddSeconds(-1);
                ctx.SaveChanges();
            }

            // Un 6e échec après expiration du verrou → verrou recalculé
            // (NbEchecs reste ≥ 5 donc le palier est toujours ≥ 30s)
            try { _authService.Authentifier("boss_test", "mauvaisX_12345"); } catch { }

            using var ctx2 = _contextFactory.CreateDbContext();
            var empAfter = ctx2.Employes.FirstOrDefault(e => e.Identifiant == "boss_test");
            Assert.That(empAfter!.NbEchecConnexion, Is.GreaterThanOrEqualTo(6));
            Assert.That(empAfter.DateVerrouJusqua, Is.Not.Null, "Un nouveau verrou doit être posé.");
            var restant = empAfter.DateVerrouJusqua!.Value - DateTime.UtcNow;
            Assert.That(restant.TotalSeconds, Is.GreaterThan(25),
                "Le verrou après 6+ échecs doit être d'au moins 30s.");
        }

        // ─── Factory pour InMemory ────────────────────────────────────────────
        private class Phase1DbContextFactory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _options;
            public Phase1DbContextFactory(DbContextOptions<ApplicationDbContext> options)
                => _options = options;
            public ApplicationDbContext CreateDbContext() => new(_options);
        }
    }
}
