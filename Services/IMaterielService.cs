using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IMaterielService
    {
        List<MaterielSupplement> ObtenirTous();
        List<MaterielSupplement> ObtenirParCommande(int idCommande);
        List<MaterielSupplement> ObtenirParPiece(int idPieceCommande);
        void Ajouter(MaterielSupplement materiel);
        void Modifier(MaterielSupplement materiel);
        void Supprimer(int idMateriel);
        decimal TotalParCommande(int idCommande);
        decimal TotalParPiece(int idPieceCommande);
    }
}