using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service pour gérer les types de vêtements.
    /// </summary>
    public class TypeVetementService : ITypeVetementService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public TypeVetementService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<TypeVetement>> ObtenirTous()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.TypesVetements
                .Include(t => t.MesuresRequises)
                .OrderBy(t => t.Nom)
                .ToListAsync();
        }

        public async Task<TypeVetement?> ObtenirParId(int id)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.TypesVetements
                .Include(t => t.MesuresRequises)
                .FirstOrDefaultAsync(t => t.IdTypeVetement == id);
        }

        public async Task Ajouter(TypeVetement typeVetement)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            context.TypesVetements.Add(typeVetement);
            await context.SaveChangesAsync();
        }

        public async Task Modifier(TypeVetement typeVetement)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            // Récupérer le type existant avec ses mesures
            var typeExistant = await context.TypesVetements
                .Include(t => t.MesuresRequises)
                .FirstOrDefaultAsync(t => t.IdTypeVetement == typeVetement.IdTypeVetement);

            if (typeExistant != null)
            {
                // Supprimer les anciennes mesures requises
                context.MesuresRequises.RemoveRange(typeExistant.MesuresRequises);

                // Mettre à jour les propriétés
                typeExistant.Nom = typeVetement.Nom;
                typeExistant.PrixBase = typeVetement.PrixBase;

                // Ajouter les nouvelles mesures requises
                foreach (var mesure in typeVetement.MesuresRequises)
                {
                    mesure.IdTypeVetement = typeExistant.IdTypeVetement;
                    context.MesuresRequises.Add(mesure);
                }

                await context.SaveChangesAsync();
            }
        }

        public async Task Supprimer(int id)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var type = await context.TypesVetements.FindAsync(id);
            if (type != null)
            {
                context.TypesVetements.Remove(type);
                await context.SaveChangesAsync();
            }
        }

        public async Task<List<TypeVetement>> Rechercher(string terme)
        {
            if (string.IsNullOrWhiteSpace(terme))
                return await ObtenirTous();

            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.TypesVetements
                .Include(t => t.MesuresRequises)
                .Where(t => t.Nom.Contains(terme))
                .OrderBy(t => t.Nom)
                .ToListAsync();
        }

        public async Task<List<TypeVetement>> ObtenirListeSimple()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.TypesVetements
                .OrderBy(t => t.Nom)
                .ToListAsync();
        }
    }
}
