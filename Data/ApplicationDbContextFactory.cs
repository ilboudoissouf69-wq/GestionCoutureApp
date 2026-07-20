using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using GestionCoutureApp.Helpers;

namespace GestionCoutureApp.Data
{
    /// <summary>
    /// Factory utilisée UNIQUEMENT par les outils EF Core au design-time
    /// (dotnet ef migrations add / update).
    /// Elle n'est jamais appelée à l'exécution de l'application.
    ///
    /// CORRECTIF : alignée sur AppPaths pour pointer vers la même base que
    /// l'application au runtime (utile si vous générez une migration puis
    /// voulez tester `dotnet ef database update` directement en ligne de
    /// commande sur la vraie base locale).
    /// </summary>
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlite(AppPaths.ChaineConnexionSqlite);
            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
