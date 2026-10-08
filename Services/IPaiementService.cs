
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>Filtre de statut pour la liste paginée des paiements.</summary>
    public enum StatutFiltrePaiement
    {
        Valides,
        Annules,
        Tous
    }

    public interface IPaiementService
    {
        List<Paiement> ObtenirTous();
        
        // ✅ PAGINATION : Récupère les paiements avec pagination
        Task<PagedResult<Paiement>> ObtenirPageAsync(int page, int pageSize);
        
        // ✅ OPTIMISATION : Version légère pour affichage tableau
        Task<PagedResult<Paiement>> ObtenirPageLightAsync(int page, int pageSize);

        // ✅ RECHERCHE + FILTRE : surcharge additive (n'altère pas la signature existante)
        Task<PagedResult<Paiement>> ObtenirPageLightAsync(int page, int pageSize,
            string? recherche, StatutFiltrePaiement filtre);
        
        List<Paiement> ObtenirParCommande(int idCommande);
        void Ajouter(Paiement paiement, int idOperateur, string nomOperateur);
        void Annuler(int idPaiement, string motif, int idAnnulateur, string nomAnnulateur);
        decimal TotalPayeParCommande(int idCommande);
        decimal TotalValideParCommande(int idCommande);
        string GenererNumeroRecu();
    }
}
