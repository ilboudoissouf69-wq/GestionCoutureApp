using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IPaiementService
    {
        List<Paiement> ObtenirTous();
        List<Paiement> ObtenirParCommande(int idCommande);
        void Ajouter(Paiement paiement, int idOperateur, string nomOperateur);
        void Annuler(int idPaiement, string motif, string nomAnnulateur);
        decimal TotalPayeParCommande(int idCommande);
        decimal TotalValideParCommande(int idCommande);
        string GenererNumeroRecu();
    }
}
