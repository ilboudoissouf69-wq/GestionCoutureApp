using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IMaterielService
    {
        List<MaterielSupplement> ObtenirTous();
        List<MaterielSupplement> ObtenirParCommande(int idCommande);
        List<MaterielSupplement> ObtenirParPiece(int idPieceCommande);

        /// <summary>
        /// Enregistre un matériau/supplément.
        /// <paramref name="idOperateur"/> et <paramref name="nomOperateur"/> sont
        /// automatiquement renseignés par l'appelant depuis
        /// <c>AuthService.UtilisateurConnecte</c> — aucune ressaisie.
        /// </summary>
        void Ajouter(MaterielSupplement materiel, int idOperateur, string nomOperateur);

        void Modifier(MaterielSupplement materiel);
        void Supprimer(int idMateriel);
        decimal TotalParCommande(int idCommande);
        decimal TotalParPiece(int idPieceCommande);
    }
}