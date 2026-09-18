namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service de logging centralisé pour l'application Gestion Couture.
    /// Enregistre toutes les actions, erreurs et événements dans un fichier journal.
    /// </summary>
    public interface ILogService
    {
        /// <summary>
        /// Enregistre une information générale (ex: connexion utilisateur, action réussie)
        /// </summary>
        void LogInfo(string message, string? utilisateur = null);

        /// <summary>
        /// Enregistre un avertissement (ex: valeur inhabituelle, situation à surveiller)
        /// </summary>
        void LogWarning(string message, string? utilisateur = null);

        /// <summary>
        /// Enregistre une erreur avec exception
        /// </summary>
        void LogError(string message, Exception? exception = null, string? utilisateur = null);

        /// <summary>
        /// Enregistre une action utilisateur spécifique (ex: création commande, modification client)
        /// </summary>
        void LogAction(string action, string details, string? utilisateur = null);

        /// <summary>
        /// Obtient le chemin complet du dossier contenant les fichiers de log
        /// </summary>
        string ObtenirCheminDossierLogs();
    }
}
