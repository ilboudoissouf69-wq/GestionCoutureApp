using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service de gestion de la localisation (multilinguisme).
    /// Change dynamiquement la culture de l'application et notifie les vues.
    /// </summary>
    public class LanguageService : ILanguageService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly ConcurrentBag<Action<string>> _languageChangeHandlers = new();
        private readonly ResourceManager _resourceManager;
        
        public LanguageService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IEventAggregator eventAggregator)
        {
            _contextFactory = contextFactory;
            _eventAggregator = eventAggregator;
            _resourceManager = new ResourceManager("GestionCoutureApp.Resources.Strings", typeof(LanguageService).Assembly);
        }

        public async Task<string> GetCurrentLanguageAsync()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var param = await context.Parametres.FindAsync("Langue");
                return param?.Valeur ?? "fr";
            }
            catch
            {
                return "fr";
            }
        }

        public async Task SetLanguageAsync(string languageCode)
        {
            // Valider le code de langue
            if (languageCode != "fr" && languageCode != "en")
                throw new ArgumentException("Language must be 'fr' or 'en'");

            // Sauvegarder dans la base de données
            using var context = _contextFactory.CreateDbContext();
            var param = await context.Parametres.FindAsync("Langue");
            if (param == null)
            {
                context.Parametres.Add(new Parametre { Cle = "Langue", Valeur = languageCode });
            }
            else
            {
                param.Valeur = languageCode;
            }
            await context.SaveChangesAsync();

            // Changer la culture de l'application
            var culture = new CultureInfo(languageCode == "fr" ? "fr-FR" : "en-GB");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            // Notifier tous les abonnés
            foreach (var handler in _languageChangeHandlers)
            {
                try
                {
                    handler(languageCode);
                }
                catch
                {
                    // Ignorer les erreurs de handlers
                }
            }

            // Publier l'événement global
            _eventAggregator.Publish(new SettingsChangedEvent
            {
                Type = SettingsChangedType.Language,
                Data = languageCode
            });
        }

        public string GetString(string key)
        {
            try
            {
                var translation = _resourceManager.GetString(key, CultureInfo.CurrentUICulture);
                return translation ?? key;
            }
            catch
            {
                // Fallback : retourner la clé si pas de traduction
                return key;
            }
        }

        public void SubscribeToLanguageChanges(Action<string> handler)
        {
            _languageChangeHandlers.Add(handler);
        }

        public void UnsubscribeFromLanguageChanges(Action<string> handler)
        {
            var tempHandlers = _languageChangeHandlers.ToList();
            tempHandlers.Remove(handler);
            
            // Nettoyer si vide
            if (!tempHandlers.Any())
            {
                _languageChangeHandlers.Clear();
            }
        }
    }
}
