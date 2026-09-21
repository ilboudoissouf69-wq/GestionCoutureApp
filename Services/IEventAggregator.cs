namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Interface pour le service d'agrégation d'événements.
    /// Permet la communication entre les services et les vues sans couplage fort.
    /// </summary>
    public interface IEventAggregator
    {
        /// <summary>
        /// S'abonner aux changements de paramètres.
        /// </summary>
        void Subscribe(SettingsChangedType type, Action<SettingsChangedEvent> handler);
        
        /// <summary>
        /// Se désabonner des changements de paramètres.
        /// </summary>
        void Unsubscribe(SettingsChangedType type, Action<SettingsChangedEvent> handler);
        
        /// <summary>
        /// Publier un changement de paramètres.
        /// </summary>
        void Publish(SettingsChangedEvent @event);
    }
}
