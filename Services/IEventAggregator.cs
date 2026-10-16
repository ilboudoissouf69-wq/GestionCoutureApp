namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Interface pour le service d'agrégation d'événements.
    /// Permet la communication entre les services et les vues sans couplage fort.
    /// </summary>
    public interface IEventAggregator
    {
        // ── Paramètres / UI ──────────────────────────────────────────────

        /// <summary>S'abonner aux changements de paramètres.</summary>
        void Subscribe(SettingsChangedType type, Action<SettingsChangedEvent> handler);

        /// <summary>Se désabonner des changements de paramètres.</summary>
        void Unsubscribe(SettingsChangedType type, Action<SettingsChangedEvent> handler);

        /// <summary>Publier un changement de paramètres.</summary>
        void Publish(SettingsChangedEvent @event);

        // ── Domaine métier (données) ──────────────────────────────────────

        /// <summary>S'abonner aux changements de données d'un domaine métier.</summary>
        void Subscribe(DomainChangedType type, Action<DomainChangedEvent> handler);

        /// <summary>Se désabonner des changements de données d'un domaine métier.</summary>
        void Unsubscribe(DomainChangedType type, Action<DomainChangedEvent> handler);

        /// <summary>Publier un changement de données métier.</summary>
        void Publish(DomainChangedEvent @event);
    }
}
