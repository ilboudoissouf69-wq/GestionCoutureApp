using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class ClientService : IClientService
    {
        private readonly ApplicationDbContext _context;

        public ClientService(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<Client> ObtenirTous()
        {
            return _context.Clients.ToList();
        }

        public void Ajouter(Client client)
        {
            _context.Clients.Add(client);
            _context.SaveChanges();
        }

        public void Modifier(Client client)
        {
            var existant = _context.Clients.Find(client.IdClient);
            if (existant != null)
            {
                existant.Nom = client.Nom;
                existant.Prenom = client.Prenom;
                existant.Telephone = client.Telephone;
                _context.SaveChanges();
            }
        }

        public void Supprimer(int id)
        {
            var client = _context.Clients.Find(id);
            if (client != null)
            {
                _context.Clients.Remove(client);
                _context.SaveChanges();
            }
        }

        public List<Client> Rechercher(string motCle)
        {
            return _context.Clients
                .Where(c => c.Nom.Contains(motCle)
                         || c.Prenom.Contains(motCle)
                         || c.Telephone.Contains(motCle))
                .ToList();
        }
    }
}