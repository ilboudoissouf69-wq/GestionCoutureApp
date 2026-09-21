namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Interface pour le service de gestion de la localisation (multilinguisme).
    /// Permet de changer dynamiquement la langue de l'application.
    /// </summary>
    public interface ILanguageService
    {
        /// <summary>
        /// Obtenir la langue actuelle ("fr" ou "en").
        /// </summary>
        Task<string> GetCurrentLanguageAsync();
        
        /// <summary>
        /// Définir la langue actuelle et notifier tous les abonnés.
        /// </summary>
        Task SetLanguageAsync(string languageCode);
        
        /// <summary>
        /// Obtenir la chaîne localisée pour une clé donnée.
        /// </summary>
        string GetString(string key);
        
        /// <summary>
        /// S'abonner aux changements de langue.
        /// </summary>
        void SubscribeToLanguageChanges(Action<string> handler);
        
        /// <summary>
        /// Se désabonner des changements de langue.
        /// </summary>
        void UnsubscribeFromLanguageChanges(Action<string> handler);
    }
}
