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

        // Ajoute un nouveau client en base
        void Ajouter(Client client);

        // Met à jour un client existant
        void Modifier(Client client);

        // Supprime un client par son Id
        void Supprimer(int id);

        // Cherche des clients par nom ou téléphone
        List<Client> Rechercher(string motCle);
        
        // ✅ PAGINATION : Cherche des clients avec pagination
        Task<PagedResult<Client>> RechercherPageAsync(string motCle, int page, int pageSize);
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