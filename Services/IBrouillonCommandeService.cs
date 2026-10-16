using System.Threading.Tasks;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service de gestion du brouillon automatique de commande.
    /// </summary>
    public interface IBrouillonCommandeService
    {
        /// <summary>
        /// Sauvegarde ou met à jour le brouillon pour un employé.
        /// Remplace automatiquement le brouillon précédent s'il existe.
        /// </summary>
        Task SauvegarderBrouillonAsync(int idEmploye, BrouillonCommandeDto brouillon);

        /// <summary>
        /// Récupère le brouillon d'un employé, ou null s'il n'en a pas.
        /// </summary>
        Task<BrouillonCommandeDto?> ChargerBrouillonAsync(int idEmploye);

        /// <summary>
        /// Supprime le brouillon d'un employé.
        /// </summary>
        Task SupprimerBrouillonAsync(int idEmploye);

        /// <summary>
        /// Vérifie si un employé a un brouillon en cours.
        /// </summary>
        Task<bool> ABrouillonAsync(int idEmploye);

        /// <summary>
        /// Retourne le nom du client et la date du brouillon pour affichage dans le bandeau.
        /// </summary>
        Task<(string nomClient, DateTime date)?> ObtenirInfosBrouillonAsync(int idEmploye);
    }
}
