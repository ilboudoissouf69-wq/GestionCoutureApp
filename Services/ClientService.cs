using System.ComponentModel.DataAnnotations;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    public class ClientService : IClientService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<ClientService> _logger;

        public ClientService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<ClientService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public List<Client> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Clients.ToList();
        }

        public void Ajouter(Client client)
        {
            ValiderClient(client);
            using var context = _contextFactory.CreateDbContext();
            context.Clients.Add(client);
            context.SaveChanges();
            _logger.LogInformation("Client ajouté — {Prenom} {Nom}", client.Prenom, client.Nom);
        }

        public void Modifier(Client client)
        {
            ValiderClient(client);
            using var context = _contextFactory.CreateDbContext();
            var existant = context.Clients.Find(client.IdClient);
            if (existant == null) return;
            existant.Nom = client.Nom;
            existant.Prenom = client.Prenom;
            existant.Telephone = client.Telephone;
            context.SaveChanges();
            _logger.LogInformation("Client modifié — {Id} {Prenom} {Nom}", client.IdClient, client.Prenom, client.Nom);
        }

        public void Supprimer(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            var client = context.Clients
                .Include(c => c.Commandes)
                .FirstOrDefault(c => c.IdClient == id);

            if (client == null) return;

            if (client.Commandes.Any())
                throw new InvalidOperationException(
                    "Impossible de supprimer ce client : il a des commandes enregistrées. " +
                    "Supprimez ou réattribuez d'abord ses commandes si nécessaire.");

            context.Clients.Remove(client);
            context.SaveChanges();
            _logger.LogWarning("Client supprimé — {Id} {Prenom} {Nom}", id, client.Prenom, client.Nom);
        }

        public List<Client> Rechercher(string motCle)
        {
            using var context = _contextFactory.CreateDbContext();

            // CORRECTIF (bug silencieux — voir Helpers/TexteHelper.cs) :
            // comparaison normalisée (minuscule + sans accents) au lieu d'un
            // Contains() SQL brut qui ratait silencieusement les noms accentués.
            string cle = Helpers.TexteHelper.NormaliserPourRecherche(motCle);

            return context.Clients
                .AsEnumerable()
                .Where(c => Helpers.TexteHelper.NormaliserPourRecherche(c.Nom).Contains(cle)
                         || Helpers.TexteHelper.NormaliserPourRecherche(c.Prenom).Contains(cle)
                         || (c.Telephone ?? "").Contains(motCle))
                .ToList();
        }

        // ----------------------------------------------------------------
        // Validation centralisée avec DataAnnotations
        // ----------------------------------------------------------------
        private static void ValiderClient(Client client)
        {
            var ctx = new ValidationContext(client);
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(client, ctx, errors, validateAllProperties: true))
            {
                var msg = string.Join("\n", errors.Select(e => e.ErrorMessage));
                throw new InvalidOperationException(msg);
            }
        }
    }
}
