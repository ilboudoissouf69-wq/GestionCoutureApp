using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GestionCoutureApp.Converters
{
    /// <summary>
    /// Convertit un bool en Visibility.
    /// true  → Visible
    /// false → Collapsed
    /// Paramètre optionnel "Invert" pour inverser.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool bv && bv;
            bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
            return (b ^ invert) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible;
    }
}
