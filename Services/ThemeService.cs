using System.Windows;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service de gestion des thèmes (couleurs d'accentuation).
    /// Met à jour dynamiquement les ressources de l'application.
    /// </summary>
    public class ThemeService : IThemeService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IParametresService _parametresService;
        private readonly IEventAggregator _eventAggregator;

        public ThemeService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IParametresService parametresService,
            IEventAggregator eventAggregator)
        {
            _contextFactory = contextFactory;
            _parametresService = parametresService;
            _eventAggregator = eventAggregator;
        }

        public async Task<string> GetAccentColorAsync()
        {
            return await _parametresService.ObtenirCouleurAccent();
        }

        public async Task SetAccentColorAsync(string hexColor)
        {
            // Valider le format hex
            if (!System.Text.RegularExpressions.Regex.IsMatch(hexColor, "^#[0-9A-Fa-f]{6}$"))
                throw new ArgumentException("Color must be in hex format (e.g., #CC0000)");

            // Sauvegarder dans la base de données
            await _parametresService.DefinirCouleurAccent(hexColor);

            // Appliquer aux ressources
            ApplyAccentColor(hexColor);

            // Publier l'événement global
            _eventAggregator.Publish(new SettingsChangedEvent
            {
                Type = SettingsChangedType.AccentColor,
                Data = hexColor
            });
        }

        public void ApplyAccentColor(string hexColor)
        {
            // Convertir hex en Color
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            
            // Créer les Brush dérivés
            var accentBrush = new SolidColorBrush(color);
            var accentHoverBrush = new SolidColorBrush(
                Color.FromRgb(
                    (byte)(color.R * 0.85),
                    (byte)(color.G * 0.85),
                    (byte)(color.B * 0.85)
                )
            );

            // Mettre à jour les ressources de l'application
            if (Application.Current.Resources.Contains("AccentColor"))
            {
                Application.Current.Resources["AccentColor"] = color;
                Application.Current.Resources["AccentBrush"] = accentBrush;
                Application.Current.Resources["AccentHoverBrush"] = accentHoverBrush;
            }
        }
    }
}
