using System.Globalization;
using System.Windows.Controls;

namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// CORRECTIF AUDIT #6 : ValidationRule WPF pour empêcher la saisie de montants négatifs
    /// ou excessifs dans les TextBox monétaires.
    /// 
    /// Applique les règles suivantes :
    /// - Le montant doit être un nombre décimal valide
    /// - Le montant doit être strictement positif (> 0)
    /// - Le montant doit être inférieur à 1 milliard (limite réaliste anti-erreur de saisie)
    /// </summary>
    public class MontantPositifValidationRule : ValidationRule
    {
        private const decimal MontantMaximal = 1_000_000_000m; // 1 milliard FCFA

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (value is not string str)
                return new ValidationResult(false, "Type invalide");

            // Nettoyer la chaîne (espaces, virgules)
            str = str.Replace(" ", "").Replace(",", ".");

            if (string.IsNullOrWhiteSpace(str))
                return new ValidationResult(false, "Le montant est requis");

            if (!decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal montant))
                return new ValidationResult(false, "Format numérique invalide");

            if (montant <= 0)
                return new ValidationResult(false, "Le montant doit être positif");

            if (montant >= MontantMaximal)
                return new ValidationResult(false, $"Montant trop élevé (max {MontantMaximal:N0} FCFA)");

            return ValidationResult.ValidResult;
        }
    }
}
