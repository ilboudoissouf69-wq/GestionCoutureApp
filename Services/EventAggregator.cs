using System.Collections.Concurrent;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Implémentation simple du pattern Event Aggregator pour la communication
    /// inter-composants sans couplage fort.
    /// </summary>
    public class EventAggregator : IEventAggregator
    {
        private readonly ConcurrentDictionary<SettingsChangedType, ConcurrentBag<Action<SettingsChangedEvent>>> _handlers
            = new();

        public void Subscribe(SettingsChangedType type, Action<SettingsChangedEvent> handler)
        {
            var handlers = _handlers.GetOrAdd(type, _ => new ConcurrentBag<Action<SettingsChangedEvent>>());
            handlers.Add(handler);
        }

        public void Unsubscribe(SettingsChangedType type, Action<SettingsChangedEvent> handler)
        {
            if (_handlers.TryGetValue(type, out var handlers))
            {
                var tempHandlers = handlers.ToList();
                tempHandlers.Remove(handler);
                
                // Si c'était le dernier handler, nettoyer
                if (!tempHandlers.Any())
                {
                    _handlers.TryRemove(type, out _);
                }
            }
        }

        public void Publish(SettingsChangedEvent @event)
        {
            if (_handlers.TryGetValue(@event.Type, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler(@event);
                    }
                    catch
                    {
                        // Ignorer les erreurs de handlers pour éviter de casser tout le système
                    // de notification si une vue a un problème
                    System.Diagnostics.Debug.WriteLine($"Erreur dans handler pour {@event.Type}");
                    }
                }
            }
        }
    }
}
