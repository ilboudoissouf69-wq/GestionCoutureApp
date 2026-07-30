// Services/ICommandeService.cs
// Interface du service Commande.
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface ICommandeService
    {
        List<Commande> ObtenirTous();
        Commande? ObtenirParId(int id);

        // ÉTAPE 1b-i (Point 1) : "piece" porte désormais TypeVetement/
        // IdCouturier/MontantCouture — CommandesView n'en crée qu'UNE seule
        // par commande pour l'instant (garanti jusqu'à l'UI multi-pièces de
        // l'Étape 1b-ii). "mesures" sont maintenant rattachées à CETTE pièce
        // (voir Mesure.IdPieceCommande), plus à la commande directement.
        void Ajouter(Commande commande, PieceCommande piece, List<Mesure> mesures);
        void Modifier(Commande commande, PieceCommande piece, List<Mesure> mesures);

        void Supprimer(int id);
        List<Commande> Rechercher(string motCle);

        // ÉTAPE 1b-i : lit désormais les mesures d'une PIÈCE, pas d'une
        // commande entière (signature volontairement différente pour que
        // tout appel existant utilisant l'ancien idCommande soit détecté à
        // la compilation plutôt que de silencieusement renvoyer une liste
        // vide).
        List<Mesure> ObtenirMesuresPiece(int idPieceCommande);
    }
}