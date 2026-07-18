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

            var client = context.Clients
                .Include(c => c.Commandes)
                .FirstOrDefault(c => c.IdClient == id);

            if (client == null) return;

            // Un client ayant des commandes (meme sans paiement) ne doit jamais etre
            // supprime directement : cela effacerait silencieusement son historique
            // (nom du client sur les recus, mesures, etc.). On bloque avec un message clair.
            if (client.Commandes.Any())
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer ce client : il a des commandes enregistrées. " +
                    "Supprimez ou réattribuez d'abord ses commandes si nécessaire.");
            }

            context.Clients.Remove(client);
            context.SaveChanges();
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
