using System.Linq;
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
        private readonly ApplicationDbContext _context;

        public TypeVetementService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<TypeVetement>> ObtenirTous()
        {
            return await _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .OrderBy(t => t.Nom)
                .ToListAsync();
        }

        public async Task<TypeVetement?> ObtenirParId(int id)
        {
            return await _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .FirstOrDefaultAsync(t => t.IdTypeVetement == id);
        }

        public async Task Ajouter(TypeVetement typeVetement)
        {
            _context.TypesVetements.Add(typeVetement);
            await _context.SaveChangesAsync();
        }

        public async Task Modifier(TypeVetement typeVetement)
        {
            // Récupérer le type existant avec ses mesures
            var typeExistant = await _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .FirstOrDefaultAsync(t => t.IdTypeVetement == typeVetement.IdTypeVetement);

            if (typeExistant != null)
            {
                // Supprimer les anciennes mesures requises
                _context.MesuresRequises.RemoveRange(typeExistant.MesuresRequises);

                // Mettre à jour les propriétés
                typeExistant.Nom = typeVetement.Nom;
                typeExistant.PrixBase = typeVetement.PrixBase;

                // Ajouter les nouvelles mesures requises
                foreach (var mesure in typeVetement.MesuresRequises)
                {
                    mesure.IdTypeVetement = typeExistant.IdTypeVetement;
                    _context.MesuresRequises.Add(mesure);
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task Supprimer(int id)
        {
            var type = await _context.TypesVetements.FindAsync(id);
            if (type != null)
            {
                _context.TypesVetements.Remove(type);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<TypeVetement>> Rechercher(string terme)
        {
            if (string.IsNullOrWhiteSpace(terme))
                return await ObtenirTous();

            return await _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .Where(t => t.Nom.Contains(terme))
                .OrderBy(t => t.Nom)
                .ToListAsync();
        }

        public async Task<List<TypeVetement>> ObtenirListeSimple()
        {
            return await _context.TypesVetements
                .OrderBy(t => t.Nom)
                .ToListAsync();
        }
    }
}