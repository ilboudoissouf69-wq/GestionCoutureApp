using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GestionCoutureApp.Converters
{
    /// <summary>
    /// Convertit une chaîne en Visibility.
    /// Non-vide → Visible
    /// Vide/null → Collapsed
    /// </summary>
    public class NotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => DependencyProperty.UnsetValue;
    }
}
