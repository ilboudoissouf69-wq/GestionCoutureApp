namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Interface pour le service de gestion des thèmes (couleurs d'accentuation).
    /// Permet de changer dynamiquement les couleurs de l'application.
    /// </summary>
    public interface IThemeService
    {
        /// <summary>
        /// Obtenir la couleur d'accentuation actuelle.
        /// </summary>
        Task<string> GetAccentColorAsync();
        
        /// <summary>
        /// Définir la couleur d'accentuation et notifier tous les abonnés.
        /// </summary>
        Task SetAccentColorAsync(string hexColor);
        
        /// <summary>
        /// Appliquer la couleur d'accentuation aux ressources de l'application.
        /// </summary>
        void ApplyAccentColor(string hexColor);
    }
}
