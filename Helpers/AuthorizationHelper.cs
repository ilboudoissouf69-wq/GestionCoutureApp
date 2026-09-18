using GestionCoutureApp.Models;

namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// Helper de contrôle d'accès basé sur les rôles (RBAC).
    /// Applique une défense en profondeur : validation côté services
    /// en plus des contrôles UI (MainWindow).
    /// </summary>
    public static class AuthorizationHelper
    {
        /// <summary>
        /// Vérifie que l'utilisateur connecté possède l'un des rôles autorisés.
        /// Lève une UnauthorizedAccessException si ce n'est pas le cas.
        /// </summary>
        /// <param name="utilisateur">Utilisateur à vérifier (null = non connecté)</param>
        /// <param name="rolesAutorises">Liste des rôles autorisés (Boss, Secretaire, Couturier)</param>
        /// <exception cref="UnauthorizedAccessException">Accès refusé</exception>
        public static void RequireRole(Employe? utilisateur, params string[] rolesAutorises)
        {
            if (utilisateur == null)
                throw new UnauthorizedAccessException(
                    "Aucun utilisateur connecté. Cette opération nécessite une authentification.");
            
            if (!rolesAutorises.Contains(utilisateur.Role))
                throw new UnauthorizedAccessException(
                    $"Accès refusé. Rôle requis : {string.Join(" ou ", rolesAutorises)}. " +
                    $"Votre rôle actuel : {utilisateur.Role}.");
        }

        /// <summary>
        /// Vérifie que l'opérateur identifié par son ID possède le rôle requis.
        /// Version pour les services qui reçoivent un idOperateur au lieu de l'objet Employe.
        /// </summary>
        public static void RequireRoleById(
            Microsoft.EntityFrameworkCore.IDbContextFactory<Data.ApplicationDbContext> contextFactory,
            int idOperateur, 
            params string[] rolesAutorises)
        {
            using var context = contextFactory.CreateDbContext();
            var operateur = context.Employes.Find(idOperateur);
            RequireRole(operateur, rolesAutorises);
        }
    }
}
