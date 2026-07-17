using System.Threading.Tasks;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Interface du service pour gérer les types de vêtements.
    /// </summary>
    public interface ITypeVetementService
    {
        Task<List<TypeVetement>> ObtenirTous();
        Task<TypeVetement?> ObtenirParId(int id);
        Task Ajouter(TypeVetement typeVetement);
        Task Modifier(TypeVetement typeVetement);
        Task Supprimer(int id);
        Task<List<TypeVetement>> Rechercher(string terme);
        Task<List<TypeVetement>> ObtenirListeSimple();
    }
}