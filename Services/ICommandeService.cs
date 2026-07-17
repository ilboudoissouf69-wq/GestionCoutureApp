// Services/ICommandeService.cs
// Interface du service Commande.
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface ICommandeService
    {
        List<Commande> ObtenirTous();
        Commande? ObtenirParId(int id);
        void Ajouter(Commande commande, List<Mesure> mesures);
        void Modifier(Commande commande, List<Mesure> mesures);
        void Supprimer(int id);
        List<Commande> Rechercher(string motCle);
        List<Mesure> ObtenirMesures(int idCommande);
    }
}