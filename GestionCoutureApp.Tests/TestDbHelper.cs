using GestionCoutureApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
    }
}
