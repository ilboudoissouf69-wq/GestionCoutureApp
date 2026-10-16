using System.Collections.Concurrent;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Implémentation du pattern Event Aggregator pour la communication
    /// inter-composants sans couplage fort.
    /// Gère deux familles d'événements : paramètres UI (SettingsChangedEvent)
    /// et données métier (DomainChangedEvent).
    /// </summary>
    public class EventAggregator : IEventAggregator
    {
        // ── Paramètres / UI ──────────────────────────────────────────────

        private readonly ConcurrentDictionary<SettingsChangedType, List<Action<SettingsChangedEvent>>>
            _settingsHandlers = new();

        private readonly object _settingsLock = new();

        public void Subscribe(SettingsChangedType type, Action<SettingsChangedEvent> handler)
        {
            lock (_settingsLock)
            {
                if (!_settingsHandlers.TryGetValue(type, out var list))
                {
                    list = new List<Action<SettingsChangedEvent>>();
                    _settingsHandlers[type] = list;
                }
                if (!list.Contains(handler))
                    list.Add(handler);
            }
        }

        public void Unsubscribe(SettingsChangedType type, Action<SettingsChangedEvent> handler)
        {
            lock (_settingsLock)
            {
                if (_settingsHandlers.TryGetValue(type, out var list))
                    list.Remove(handler);
            }
        }

        public void Publish(SettingsChangedEvent @event)
        {
            List<Action<SettingsChangedEvent>> snapshot;
            lock (_settingsLock)
            {
                if (!_settingsHandlers.TryGetValue(@event.Type, out var list) || list.Count == 0)
                    return;
                snapshot = list.ToList();
            }
            foreach (var handler in snapshot)
            {
                try { handler(@event); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[EventAggregator] Erreur handler SettingsChanged({@event.Type}): {ex.Message}");
                }
            }
        }

        // ── Domaine métier ────────────────────────────────────────────────

        private readonly ConcurrentDictionary<DomainChangedType, List<Action<DomainChangedEvent>>>
            _domainHandlers = new();

        private readonly object _domainLock = new();

        public void Subscribe(DomainChangedType type, Action<DomainChangedEvent> handler)
        {
            lock (_domainLock)
            {
                if (!_domainHandlers.TryGetValue(type, out var list))
                {
                    list = new List<Action<DomainChangedEvent>>();
                    _domainHandlers[type] = list;
                }
                if (!list.Contains(handler))
                    list.Add(handler);
            }
        }

        public void Unsubscribe(DomainChangedType type, Action<DomainChangedEvent> handler)
        {
            lock (_domainLock)
            {
                if (_domainHandlers.TryGetValue(type, out var list))
                    list.Remove(handler);
            }
        }

        public void Publish(DomainChangedEvent @event)
        {
            List<Action<DomainChangedEvent>> snapshot;
            lock (_domainLock)
            {
                if (!_domainHandlers.TryGetValue(@event.Type, out var list) || list.Count == 0)
                    return;
                snapshot = list.ToList();
            }
            foreach (var handler in snapshot)
            {
                try { handler(@event); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[EventAggregator] Erreur handler DomainChanged({@event.Type}): {ex.Message}");
                }
            }
        }
    }
}
