using System.Reflection;

namespace GestionCoutureApp.Helpers
{
    /// <summary>Version déclarée par la propriété <c>Version</c> du fichier projet.</summary>
    public static class VersionApplication
    {
        public static string Courante
        {
            get
            {
                var assembly = typeof(VersionApplication).Assembly;
                string? version = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

                // Le SDK peut ajouter le hash Git à la version informative :
                // l'interface n'affiche que le numéro défini dans le .csproj.
                if (!string.IsNullOrWhiteSpace(version))
                    return version.Split('+', 2)[0];

                return assembly.GetName().Version?.ToString(3) ?? "inconnue";
            }
        }
    }
}
