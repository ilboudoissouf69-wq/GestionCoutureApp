using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Globalization;

namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// CORRECTIF AUDIT #A3 : Helper pour la validation des saisies utilisateur.
    /// Empêche la saisie de caractères invalides dans les TextBox numériques.
    /// </summary>
    public static class ValidationHelper
    {
        /// <summary>
        /// Validation pour TextBox acceptant uniquement des nombres décimaux positifs.
        /// Usage XAML : PreviewTextInput="ValidationHelper.TextBox_PreviewTextInputDecimal"
        /// </summary>
        /// <remarks>
        /// Autorise :
        /// - Chiffres 0-9
        /// - Un seul point décimal (.)
        /// - Pas de signe négatif (-)
        /// </remarks>
        public static void TextBox_PreviewTextInputDecimal(object sender, TextCompositionEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox textBox)
            {
                e.Handled = true;
                return;
            }

            // Regex : uniquement chiffres et point décimal
            var regex = new Regex("[^0-9.]+");
            bool estInvalide = regex.IsMatch(e.Text);

            if (estInvalide)
            {
                e.Handled = true;
                return;
            }

            // Vérifier qu'on n'a pas déjà un point décimal
            if (e.Text == ".")
            {
                // Si le TextBox contient déjà un point, refuser
                if (textBox.Text.Contains("."))
                {
                    e.Handled = true;
                    return;
                }

                // Si c'est le premier caractère, ajouter un 0 devant
                if (string.IsNullOrEmpty(textBox.Text))
                {
                    textBox.Text = "0.";
                    textBox.SelectionStart = textBox.Text.Length;
                    e.Handled = true;
                    return;
                }
            }

            // Accepter la saisie
            e.Handled = false;
        }

        /// <summary>
        /// Validation pour TextBox acceptant uniquement des nombres entiers positifs.
        /// Usage XAML : PreviewTextInput="ValidationHelper.TextBox_PreviewTextInputEntier"
        /// </summary>
        public static void TextBox_PreviewTextInputEntier(object sender, TextCompositionEventArgs e)
        {
            // Regex : uniquement chiffres
            var regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        /// <summary>
        /// Empêche le collage de texte non numérique dans un TextBox.
        /// Usage XAML : DataObject.Pasting="ValidationHelper.TextBox_Pasting"
        /// </summary>
        public static void TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                
                // Vérifier si le texte collé est valide (chiffres et un seul point)
                var regex = new Regex("^[0-9]+(\\.[0-9]*)?$");
                if (!regex.IsMatch(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        /// <summary>
        /// Valide qu'une chaîne représente un nombre décimal positif valide.
        /// </summary>
        public static bool EstDecimalPositifValide(string texte, out decimal valeur)
        {
            valeur = 0;
            
            if (string.IsNullOrWhiteSpace(texte))
                return false;

            // Nettoyer la chaîne (espaces, virgules -> points)
            string texteNettoye = texte.Replace(" ", "").Replace(",", ".");

            if (!decimal.TryParse(texteNettoye, NumberStyles.Any, CultureInfo.InvariantCulture, out valeur))
                return false;

            return valeur > 0;
        }

        /// <summary>
        /// Valide qu'une chaîne représente un nombre entier positif valide.
        /// </summary>
        public static bool EstEntierPositifValide(string texte, out int valeur)
        {
            valeur = 0;
            
            if (string.IsNullOrWhiteSpace(texte))
                return false;

            if (!int.TryParse(texte, out valeur))
                return false;

            return valeur > 0;
        }

        /// <summary>
        /// Empêche les caractères spéciaux dangereux dans les TextBox de texte libre.
        /// Utile pour prévenir les tentatives d'injection.
        /// </summary>
        public static void TextBox_PreviewTextInputTexteSecurise(object sender, TextCompositionEventArgs e)
        {
            // Bloquer certains caractères potentiellement dangereux
            char[] caracteresInterdits = { '<', '>', '{', '}', '|', '\\', '^', '`', ';', '-', '\'', '"' };
            
            if (e.Text.Any(c => caracteresInterdits.Contains(c)))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Détecte les patterns d'injection SQL dans une chaîne de caractères.
        /// </summary>
        public static bool ContientInjectionSQL(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte))
                return false;

            string texteMaj = texte.ToUpper();
            string[] patternsSQL = 
            {
                "DROP TABLE", "DELETE FROM", "INSERT INTO", "UPDATE", "SELECT",
                "UNION SELECT", "OR 1=1", "AND 1=1", "'; DROP", "'; --", "' OR '",
                "--", "/*", "*/", "XP_", "EXEC(", "EXECUTE", "TRUNCATE", "ALTER TABLE",
                "CREATE TABLE", "GRANT", "REVOKE"
            };

            return patternsSQL.Any(pattern => texteMaj.Contains(pattern));
        }

        /// <summary>
        /// Valide qu'une chaîne de texte est sécurisée (pas d'injection SQL ou XSS).
        /// </summary>
        public static bool EstTexteSecurise(string texte, out string erreur)
        {
            erreur = string.Empty;

            if (string.IsNullOrWhiteSpace(texte))
            {
                erreur = "Le texte ne peut pas être vide";
                return false;
            }

            // Limite de longueur raisonnable pour les noms et descriptions
            if (texte.Length > 200)
            {
                erreur = "Le texte est trop long (maximum 200 caractères)";
                return false;
            }

            // Vérifier les caractères de contrôle et invisibles dangereux
            foreach (char c in texte)
            {
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                {
                    erreur = "Le texte contient des caractères de contrôle non autorisés";
                    return false;
                }
                
                // Bloquer les caractères invisibles spécifiques
                if (c == '\u0000' || c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF')
                {
                    erreur = "Le texte contient des caractères invisibles non autorisés";
                    return false;
                }
            }

            if (ContientInjectionSQL(texte))
            {
                erreur = "Le texte contient des caractères ou patterns dangereux";
                return false;
            }

            // Détecter XSS basique
            string texteMaj = texte.ToUpper();
            if (texteMaj.Contains("<SCRIPT>") || texteMaj.Contains("JAVASCRIPT:") || 
                texteMaj.Contains("ONERROR=") || texteMaj.Contains("ONLOAD="))
            {
                erreur = "Le texte contient du code potentiellement dangereux";
                return false;
            }

            // Vérifier uniquement les patterns d'injection SQL réellement dangereux
            // Autoriser les apostrophes dans les noms (ex: "O'Connor") mais bloquer les combos
            string[] patternsInjection = { "'; DROP", "'; --", "1' OR", "1' AND", " OR '1", " AND '1" };
            foreach (string pattern in patternsInjection)
            {
                if (texteMaj.Contains(pattern.ToUpper()))
                {
                    erreur = "Le texte contient des patterns d'injection interdits";
                    return false;
                }
            }

            return true;
        }
    }
}
