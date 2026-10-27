using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;

namespace GestionCoutureApp.Tests
{
    /// <summary>
    /// Helper partagé pour créer des options InMemory dans les tests.
    /// Configure l'ignorance de l'avertissement "transactions non supportées"
    /// propre à la base InMemory d'EF Core.
    /// En production, SQLite supporte les transactions sans avertissement.
    /// </summary>
    internal static class TestDbHelper
    {
        /// <summary>
        /// Crée des DbContextOptions pour une base InMemory unique.
        /// InMemoryEventId.TransactionIgnoredWarning est ignoré (EF Core 8 le lève en exception sinon).
        /// </summary>
        public static DbContextOptions<ApplicationDbContext> CreerOptions(string prefix = "Test")
            => new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{prefix}_{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

        /// <summary>
        /// Crée un vrai contexte SQLite en mémoire. La connexion est retournée ouverte :
        /// l'appelant doit la garder vivante jusqu'à la fin du test.
        /// </summary>
        public static (ApplicationDbContext Context, SqliteConnection Connection) CreerContexteSqliteEnMemoire()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new ApplicationDbContext(options);
            context.Database.EnsureCreated();
            return (context, connection);
        }

        /// <summary>
        /// Étape 10 — IAuditService no-op partagé pour les tests qui n'ont pas besoin
        /// de vérifier l'audit. L'audit est désormais une dépendance OBLIGATOIRE de
        /// CommandeService ; les tests doivent fournir au moins ce stub.
        /// </summary>
        public static readonly IAuditService NoOpAudit = new NoOpAuditService();
    }

    /// <summary>
    /// IAuditService sans effet de bord pour les tests unitaires qui ne testent
    /// pas l'audit. Ne persiste rien, ne lève jamais d'exception.
    /// Étape 10 : remplace le passage de null à CommandeService (désormais interdit).
    /// </summary>
    internal sealed class NoOpAuditService : IAuditService
    {
        public Task EnregistrerActionAsync(int idOperateur, string nomOperateur,
            string roleOperateur, string typeAction, string entite, int idEntite,
            object? valeursAvant = null, object? valeursApres = null,
            string? motif = null, bool envoyerNotification = false)
            => Task.CompletedTask;

        public List<JournalAudit> ObtenirToutesLesEntrees() => new();
        public PagedResult<JournalAudit> ObtenirPage(int page, int pageSize,
            int? idOperateur = null, string? typeAction = null,
            string? recherche = null, DateTime? debut = null, DateTime? fin = null)
            => new() { Items = new(), TotalCount = 0, Page = page, PageSize = pageSize };
        public List<JournalAudit> ObtenirParPeriode(DateTime debut, DateTime fin) => new();
        public List<JournalAudit> ObtenirParOperateur(int id) => new();
        public List<JournalAudit> ObtenirParTypeAction(string type) => new();
        public List<JournalAudit> ObtenirParEntite(string entite, int id) => new();
        public (bool Integre, string Message) VerifierIntegriteChaine() => (true, "Stub — aucune entrée");
        public StatistiquesAudit ObtenirStatistiques(DateTime debut, DateTime fin) => new();
        public List<IndicateurAntiFraude> ObtenirIndicateurAntiFraude(DateTime depuisUtc) => new();
    }
}
