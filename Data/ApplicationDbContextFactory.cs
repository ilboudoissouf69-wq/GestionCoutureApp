using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GestionCoutureApp.Data
{
    /// <summary>
    /// Factory utilisée UNIQUEMENT par les outils EF Core au design-time
    /// (dotnet ef migrations add / update).
    /// Elle n'est jamais appelée à l'exécution de l'application.
    /// </summary>
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlite("Data Source=gestion_couture.db;Cache=Shared");
            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
