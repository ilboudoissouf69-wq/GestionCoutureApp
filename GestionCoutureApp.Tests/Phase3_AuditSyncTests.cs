using System.Text.RegularExpressions;
using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace GestionCoutureApp.Tests
{
    /// <summary>
    /// Tests Lot A+ — Régressions du commit 281c885.
    ///   SYS01 : l'écriture d'audit doit être terminée au retour de l'opération
    ///           (plus de « tire et oublie » — une ligne JournalAudit existe dès
    ///           le retour de SupprimerPiece).
    ///   SYS02 : PRAGMA foreign_keys doit valoir 1 sur un contexte créé comme
    ///           le fait l'application (chaîne de connexion au format
    ///           AppPaths.ChaineConnexionSqlite). Microsoft.Data.Sqlite active
    ///           foreign_keys par défaut ; ce test verrouille ce comportement
    ///           (si un jour il échoue, ajouter Foreign Keys=True à la chaîne
    ///           de connexion dans Helpers/AppPaths.cs).
    /// </summary>
    [TestFixture]
    public class Phase3_AuditSyncTests
    {
        private IDbContextFactory<ApplicationDbContext> _contextFactory = null!;
        private ApplicationDbContext _context = null!;
        private CommandeService _commandeService = null!;

        private const int IdBoss = 1;
        private const int IdClient = 1;

        [SetUp]
        public void Setup()
        {
            CommandeService.ViderCacheDoublonPourTests();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"Phase3_{Guid.NewGuid()}")
                .Options;

            _contextFactory = new Phase3DbFactory(options);
            _context = _contextFactory.CreateDbContext();

            var loggerFactory = NullLoggerFactory.Instance;
            var auditService = new AuditService(
                _contextFactory,
                new MockWhatsAppService(),
                new MockParametresService(),
                loggerFactory.CreateLogger<AuditService>());
            _commandeService = new CommandeService(
                _contextFactory, loggerFactory.CreateLogger<CommandeService>(), auditService);

            SeedData();
        }

        private void SeedData()
        {
            _context.Employes.Add(new Employe
            {
                IdEmploye = IdBoss, Nom = "TRAORE", Prenom = "Ibrahim",
                Identifiant = "boss_phase3",
                MotDePasse = PasswordHasher.Hasher("MotDePasseSecur!2026"),
                Role = "Boss", Statut = "Actif"
            });
            _context.Clients.Add(new Client
            {
                IdClient = IdClient, Nom = "SAWADOGO", Prenom = "Aminata",
                Telephone = "70000002"
            });
            _context.Commandes.Add(new Commande
            {
                IdCommande = 1, IdClient = IdClient,
                DateDebut = DateTime.Today, DateFin = DateTime.Today.AddDays(7),
                MontantTotal = 20_000m
            });
            _context.PiecesCommande.AddRange(
                new PieceCommande
                {
                    IdPieceCommande = 1, IdCommande = 1, TypeVetement = "Pantalon",
                    MontantCouture = 10_000m, Statut = "A faire"
                },
                new PieceCommande
                {
                    IdPieceCommande = 2, IdCommande = 1, TypeVetement = "Chemise",
                    MontantCouture = 10_000m, Statut = "A faire"
                });
            _context.SaveChanges();
        }

        [TearDown]
        public void TearDown() => _context?.Dispose();

        // ================================================================
        // SYS01 — Audit écrit de façon synchrone (régression 281c885)
        // ================================================================

        [Test]
        public void SYS01_Audit_Existe_Des_Le_Retour_De_La_Methode()
        {
            _commandeService.SupprimerPiece(2, IdBoss, "Ibrahim TRAORE");

            // Contexte NEUF : on lit ce qui est réellement persisté, pas le cache
            // du contexte d'origine. Dès le retour de SupprimerPiece, la ligne
            // d'audit DOIT exister (écriture attendue, pas « tire et oublie »).
            using var lecteur = _contextFactory.CreateDbContext();
            var lignes = lecteur.JournalAudit
                .Where(j => j.TypeAction == "PIECE_SUPPRIMEE" && j.IdEntite == 2)
                .ToList();

            Assert.That(lignes.Count, Is.EqualTo(1),
                "La ligne JournalAudit doit exister dès le retour de la méthode " +
                "(l'audit doit être écrit de façon synchrone, pas en tire-et-oublie).");
            Assert.That(lignes[0].Entite, Is.EqualTo("PieceCommande"));
            Assert.That(lignes[0].IdOperateur, Is.EqualTo(IdBoss));
        }

        // ================================================================
        // SYS02 — PRAGMA foreign_keys = 1 sur un contexte au format App
        // ================================================================

        [Test]
        public void SYS02_PRAGMA_ForeignKeys_Vaut_1_Sur_Contexte_App()
        {
            // Chaîne au format réel de AppPaths.ChaineConnexionSqlite, seul le
            // chemin du fichier est remplacé par :memory: pour ne pas toucher
            // à la base de l'utilisateur.
            string chaineReelle = AppPaths.ChaineConnexionSqlite;
            string chaineTest = Regex.Replace(
                chaineReelle, @"Data Source=[^;]+", "Data Source=:memory:");

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(chaineTest)
                .Options;
            using var context = new ApplicationDbContext(options);
            context.Database.OpenConnection();

            var connexion = (SqliteConnection)context.Database.GetDbConnection();
            using var cmd = connexion.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys;";
            long valeur = (long)(cmd.ExecuteScalar() ?? 0L);

            Assert.That(valeur, Is.EqualTo(1L),
                "foreign_keys doit être actif sur chaque connexion ouverte par " +
                "l'application. Si ce test échoue, ajouter Foreign Keys=True à " +
                "AppPaths.ChaineConnexionSqlite.");
        }
    }

    /// <summary>Factory de tests du lot A+ (même modèle que Phase1/Phase2).</summary>
    public class Phase3DbFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public Phase3DbFactory(DbContextOptions<ApplicationDbContext> options) => _options = options;
        public ApplicationDbContext CreateDbContext() => new(_options);
    }
}
