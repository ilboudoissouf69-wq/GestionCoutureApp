using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class ClientService : IClientService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public ClientService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public List<Client> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Clients.ToList();
        }

        public void Ajouter(Client client)
        {
            using var context = _contextFactory.CreateDbContext();
            context.Clients.Add(client);
            context.SaveChanges();
        }

        public void Modifier(Client client)
        {
            using var context = _contextFactory.CreateDbContext();
            var existant = context.Clients.Find(client.IdClient);
            if (existant != null)
            {
                existant.Nom = client.Nom;
                existant.Prenom = client.Prenom;
                existant.Telephone = client.Telephone;
                context.SaveChanges();
            }
        }

        public void Supprimer(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            var client = context.Clients.Find(id);
            if (client != null)
            {
                context.Clients.Remove(client);
                context.SaveChanges();
            }
        }

        public List<Client> Rechercher(string motCle)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Clients
                .Where(c => c.Nom.Contains(motCle)
                         || c.Prenom.Contains(motCle)
                         || c.Telephone.Contains(motCle))
                .ToList();
        }
    }
}
