using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service de gestion du brouillon automatique de commande.
    /// Un seul brouillon par utilisateur, remplacé automatiquement.
    /// </summary>
    public class BrouillonCommandeService : IBrouillonCommandeService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public BrouillonCommandeService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task SauvegarderBrouillonAsync(int idEmploye, BrouillonCommandeDto brouillon)
        {
            using var context = _contextFactory.CreateDbContext();

            var json = JsonSerializer.Serialize(brouillon, new JsonSerializerOptions 
            { 
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var existant = await context.BrouillonsCommande
                .FirstOrDefaultAsync(b => b.IdEmploye == idEmploye);

            if (existant != null)
            {
                // Remplacer le brouillon existant
                existant.ContenuJson = json;
                existant.DateMiseAJour = DateTime.Now;
            }
            else
            {
                // Créer un nouveau brouillon
                var nouveau = new BrouillonCommande
                {
                    IdEmploye = idEmploye,
                    ContenuJson = json,
                    DateMiseAJour = DateTime.Now
                };
                context.BrouillonsCommande.Add(nouveau);
            }

            await context.SaveChangesAsync();
        }

        public async Task<BrouillonCommandeDto?> ChargerBrouillonAsync(int idEmploye)
        {
            using var context = _contextFactory.CreateDbContext();

            var brouillon = await context.BrouillonsCommande
                .FirstOrDefaultAsync(b => b.IdEmploye == idEmploye);

            if (brouillon == null)
                return null;

            try
            {
                return JsonSerializer.Deserialize<BrouillonCommandeDto>(
                    brouillon.ContenuJson,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch
            {
                // Si le JSON est corrompu, supprimer le brouillon
                await SupprimerBrouillonAsync(idEmploye);
                return null;
            }
        }

        public async Task SupprimerBrouillonAsync(int idEmploye)
        {
            using var context = _contextFactory.CreateDbContext();

            var brouillon = await context.BrouillonsCommande
                .FirstOrDefaultAsync(b => b.IdEmploye == idEmploye);

            if (brouillon != null)
            {
                context.BrouillonsCommande.Remove(brouillon);
                await context.SaveChangesAsync();
            }
        }

        public async Task<bool> ABrouillonAsync(int idEmploye)
        {
            using var context = _contextFactory.CreateDbContext();
            return await context.BrouillonsCommande.AnyAsync(b => b.IdEmploye == idEmploye);
        }

        public async Task<(string nomClient, DateTime date)?> ObtenirInfosBrouillonAsync(int idEmploye)
        {
            using var context = _contextFactory.CreateDbContext();

            var brouillon = await context.BrouillonsCommande
                .FirstOrDefaultAsync(b => b.IdEmploye == idEmploye);

            if (brouillon == null)
                return null;

            try
            {
                var dto = JsonSerializer.Deserialize<BrouillonCommandeDto>(
                    brouillon.ContenuJson,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                string nomClient = dto?.NomClient ?? "Client non défini";
                return (nomClient, brouillon.DateMiseAJour);
            }
            catch
            {
                return ("Brouillon corrompu", brouillon.DateMiseAJour);
            }
        }
    }
}
