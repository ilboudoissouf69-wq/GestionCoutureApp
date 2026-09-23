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

        // ✅ PAGINATION : Récupère les clients avec pagination
        public async Task<PagedResult<Client>> ObtenirPageAsync(int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Clients.AsQueryable();
            var totalCount = await query.CountAsync();
            
            var items = await query
                .OrderBy(c => c.Nom)
                .ThenBy(c => c.Prenom)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            
            return new PagedResult<Client>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public void Ajouter(Client client)
        {
            ValiderClient(client);

            using var context = _contextFactory.CreateDbContext();

            // ── Détection de doublon Nom+Prénom+Téléphone ────────────────────
            // Normalisation anti-accents/casse pour éviter les doublons orthographiques
            // ("Koné" vs "Kone", "Marie" vs "marie").
            // Règle :
            //   • Si téléphone renseigné : doublon = même (nomPrenom normalisé) ET même téléphone
            //   • Si téléphone vide      : doublon = même (nomPrenom normalisé) seulement
            //     → avertissement moins fort car deux personnes du même nom peuvent
            //       n'avoir aucun téléphone sans être la même personne.
            string nomPrenomNormalise = Helpers.TexteHelper.NormaliserPourRecherche(
                $"{client.Nom} {client.Prenom}");

            var candidats = context.Clients
                .AsEnumerable()
                .Where(c =>
                    Helpers.TexteHelper.NormaliserPourRecherche($"{c.Nom} {c.Prenom}") == nomPrenomNormalise)
                .ToList();

            if (candidats.Count > 0)
            {
                bool telephoneRenseigne = !string.IsNullOrWhiteSpace(client.Telephone);

                if (telephoneRenseigne)
                {
                    // Doublon fort : même nom normalisé ET même numéro de téléphone
                    var doublonStrict = candidats.FirstOrDefault(c =>
                        !string.IsNullOrWhiteSpace(c.Telephone) &&
                        c.Telephone.Trim() == client.Telephone.Trim());

                    if (doublonStrict != null)
                    {
                        _logger.LogWarning(
                            "Tentative de création d'un client doublon — " +
                            "{Prenom} {Nom} / {Tel} — existe déjà sous Id #{Id}.",
                            client.Prenom, client.Nom, client.Telephone, doublonStrict.IdClient);

                        throw new DuplicatClientException(doublonStrict);
                    }
                }
                else
                {
                    // Doublon faible : même nom normalisé, aucun téléphone des deux côtés
                    // → lever quand même l'exception pour laisser l'UI décider
                    var doublonSanstel = candidats.FirstOrDefault(c =>
                        string.IsNullOrWhiteSpace(c.Telephone));

                    if (doublonSanstel != null)
                    {
                        _logger.LogWarning(
                            "Tentative de création d'un client potentiellement en doublon " +
                            "(même nom, sans téléphone) — {Prenom} {Nom} — existe sous Id #{Id}.",
                            client.Prenom, client.Nom, doublonSanstel.IdClient);

                        throw new DuplicatClientException(doublonSanstel);
                    }
                }
            }

            context.Clients.Add(client);
            context.SaveChanges();
            _logger.LogInformation(
                "Client ajouté — #{Id} {Prenom} {Nom} / {Tel}",
                client.IdClient, client.Prenom, client.Nom, client.Telephone);
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
        
        // ✅ PAGINATION : Cherche des clients avec pagination
        public async Task<PagedResult<Client>> RechercherPageAsync(string motCle, int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            // ✅ OPTIMISATION : Filtrer côté base de données quand possible
            string cle = Helpers.TexteHelper.NormaliserPourRecherche(motCle);
            
            var query = context.Clients.AsQueryable();
            
            // Si pas d'accents dans la recherche, utiliser le filtrage SQL
            if (!Helpers.TexteHelper.ContientAccents(motCle))
            {
                query = query.Where(c => c.Nom.Contains(motCle) 
                                      || c.Prenom.Contains(motCle)
                                      || (c.Telephone ?? "").Contains(motCle));
            }
            else
            {
                // Sinon, filtrer en mémoire (plus lent mais nécessaire pour les accents)
                query = query.AsEnumerable()
                    .Where(c => Helpers.TexteHelper.NormaliserPourRecherche(c.Nom).Contains(cle)
                             || Helpers.TexteHelper.NormaliserPourRecherche(c.Prenom).Contains(cle)
                             || (c.Telephone ?? "").Contains(motCle))
                    .AsQueryable();
            }
            
            var totalCount = query.Count();
            var items = query
                .OrderBy(c => c.Nom)
                .ThenBy(c => c.Prenom)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            
            return new PagedResult<Client>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        // ----------------------------------------------------------------
        // Rapport de détection des doublons existants (pour le Boss)
        // ----------------------------------------------------------------
        public List<GroupeDoublonsClient> RechercherDoublons()
        {
            using var context = _contextFactory.CreateDbContext();

            // Chargement complet en mémoire : SQLite ne gère pas les
            // fonctions de normalisation côté base — traitement .NET nécessaire.
            var tousLesClients = context.Clients.ToList();

            return tousLesClients
                .GroupBy(c => Helpers.TexteHelper.NormaliserPourRecherche($"{c.Nom} {c.Prenom}"))
                .Where(g => g.Count() > 1)           // garder uniquement les groupes avec ≥ 2 fiches
                .Select(g => new GroupeDoublonsClient
                {
                    CleNormalise  = g.Key,
                    Clients       = g.OrderBy(c => c.IdClient).ToList()
                })
                .OrderBy(g => g.CleNormalise)
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
