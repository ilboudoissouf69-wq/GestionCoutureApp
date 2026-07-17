using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IPaiementService
    {
        List<Paiement> ObtenirTous();
        List<Paiement> ObtenirParCommande(int idCommande);
        void Ajouter(Paiement paiement, int idOperateur, string nomOperateur);
        void Annuler(int idPaiement, string motif, string nomAnnulateur);
        double TotalPayeParCommande(int idCommande);
        double TotalValideParCommande(int idCommande);
        string GenererNumeroRecu();
    }
}
