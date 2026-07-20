using System.Globalization;
using System.Text;

namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// CORRECTIF (bug silencieux) : les recherches (clients, commandes) utilisaient
    /// directement `string.Contains` traduit en LIKE par EF Core / SQLite. Le LIKE
    /// natif de SQLite n'est insensible à la casse QUE pour les caractères ASCII
    /// (a-z/A-Z) — jamais pour les caractères accentués. Résultat concret et
    /// silencieux (aucune erreur, juste "ça ne trouve rien") : rechercher
    /// "kabore" ne trouvait pas un client enregistré "KABORÉ", et "OUEDRAOGO"
    /// ne trouvait pas "Ouédraogo", ce qui est extrêmement courant avec des
    /// noms burkinabè saisis avec des variations de casse/accents. On normalise
    /// donc manuellement (minuscule + suppression des accents) des deux côtés
    /// de la comparaison, après un chargement en mémoire (les volumes d'un
    /// atelier de couture restent modestes — voir le même choix déjà fait
    /// ailleurs dans le code pour les sommes decimal via AsEnumerable()).
    /// </summary>
    public static class TexteHelper
    {
        public static string NormaliserPourRecherche(string? texte)
        {
            if (string.IsNullOrEmpty(texte)) return string.Empty;

            string decompose = texte.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decompose.Length);

            foreach (char c in decompose)
            {
                var categorie = CharUnicodeInfo.GetUnicodeCategory(c);
                if (categorie != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }
    }
}
