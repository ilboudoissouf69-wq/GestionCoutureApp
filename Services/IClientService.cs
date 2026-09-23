// Services/IClientService.cs
// Interface du service Client.
using GestionCoutureApp.Models;


namespace GestionCoutureApp.Services
{
    public interface IClientService
    {
        // Récupère tous les clients de la base
        List<Client> ObtenirTous();

        // ✅ PAGINATION : Récupère les clients avec pagination
        Task<PagedResult<Client>> ObtenirPageAsync(int page, int pageSize);

        /// <summary>
        /// Ajoute un nouveau client.
        /// </summary>
        /// <exception cref="DuplicatClientException">
        /// Levée si un client avec le même nom+prénom (normalisés, sans accents/casse)
        /// ET le même numéro de téléphone existe déjà. L'UI peut intercepter cette
        /// exception pour proposer la fiche existante au lieu de créer un doublon.
        /// </exception>
        void Ajouter(Client client);

        // Met à jour un client existant
        void Modifier(Client client);

        // Supprime un client par son Id
        void Supprimer(int id);

        // Cherche des clients par nom ou téléphone
        List<Client> Rechercher(string motCle);

        // ✅ PAGINATION : Cherche des clients avec pagination
        Task<PagedResult<Client>> RechercherPageAsync(string motCle, int page, int pageSize);

        /// <summary>
        /// Rapport de détection des doublons existants en base (clients ayant le même
        /// nom+prénom normalisé, avec ou sans téléphone identique).
        /// Utilisé par le Boss pour décider de fusions manuelles.
        /// </summary>
        List<GroupeDoublonsClient> RechercherDoublons();
    }

    /// <summary>
    /// Groupe de clients potentiellement en doublon, renvoyé par
    /// <see cref="IClientService.RechercherDoublons"/>.
    /// </summary>
    public class GroupeDoublonsClient
    {
        /// <summary>Nom+Prénom normalisés (clé de regroupement).</summary>
        public string CleNormalise { get; set; } = string.Empty;

        /// <summary>Liste des fiches qui partagent cette clé.</summary>
        public List<Client> Clients { get; set; } = new();
    }
    
    /// <summary>
    /// Résultat paginé générique
    /// </summary>
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < TotalPages;
    }
}