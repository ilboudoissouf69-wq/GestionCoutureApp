using System.IO;

namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// Centralise tous les chemins de fichiers persistants de l'application
    /// (base de données, photos, sauvegardes).
    ///
    /// CORRECTIF IMPORTANT : avant, ces chemins étaient relatifs
    /// (ex: "gestion_couture.db", "photos/", "Backups/"), donc résolus par
    /// rapport au "répertoire courant" du processus. Ce répertoire n'est PAS
    /// garanti d'être le dossier de l'exécutable :
    ///   - il dépend du raccourci utilisé pour lancer l'app (propriété
    ///     "Démarrer dans"),
    ///   - si l'app est installée dans "Program Files", le processus n'a
    ///     généralement PAS le droit d'écrire à cet endroit pour un compte
    ///     utilisateur standard (Windows bloque l'écriture) => l'application
    ///     plante silencieusement ou perd des données à la première tentative
    ///     d'écriture,
    ///   - deux lancements depuis deux répertoires différents peuvent créer
    ///     DEUX bases de données différentes sans que l'utilisateur ne s'en
    ///     rende compte (perte de données "fantôme").
    ///
    /// La solution standard Windows consiste à stocker les données utilisateur
    /// dans %LOCALAPPDATA%, qui est toujours inscriptible par l'utilisateur
    /// courant, quel que soit l'endroit où l'exécutable est installé.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>
        /// Dossier racine de l'application dans %LOCALAPPDATA%.
        /// Ex: C:\Users\Fatou\AppData\Local\GestionCoutureApp
        /// </summary>
        public static string DossierApplication { get; } = InitialiserDossierApplication();

        public static string CheminBaseDeDonnees => Path.Combine(DossierApplication, "gestion_couture.db");

        public static string DossierPhotos => CreerEtRetourner(Path.Combine(DossierApplication, "photos"));

        public static string DossierBackups => CreerEtRetourner(Path.Combine(DossierApplication, "Backups"));

        public static string DossierLogs => CreerEtRetourner(Path.Combine(DossierApplication, "Logs"));

        /// <summary>
        /// Chaîne de connexion SQLite prête à l'emploi, pointant vers le bon dossier.
        /// </summary>
        public static string ChaineConnexionSqlite => $"Data Source={CheminBaseDeDonnees};Cache=Shared";

        private static string InitialiserDossierApplication()
        {
            string racine = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData);
            string dossier = Path.Combine(racine, "GestionCoutureApp");
            Directory.CreateDirectory(dossier);
            return dossier;
        }

        private static string CreerEtRetourner(string dossier)
        {
            Directory.CreateDirectory(dossier);
            return dossier;
        }
    }
}
