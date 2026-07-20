using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.IO;
using GestionCoutureApp.Helpers;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Sauvegarde automatique de la base dans %LOCALAPPDATA%\GestionCoutureApp\Backups.
    /// — Au démarrage de l'application (copie immédiate)
    /// — Toutes les 4 heures tant que l'appli est ouverte
    /// — Conserve les 15 dernières sauvegardes (rotation automatique)
    ///
    /// CORRECTIF IMPORTANT (fiabilité) :
    /// L'ancienne implémentation faisait un simple File.Copy() du fichier
    /// .db pendant que l'application pouvait être en train d'écrire dedans.
    /// SQLite n'est pas un fichier "atomique" du point de vue du système de
    /// fichiers : une copie brute pendant une transaction en cours peut
    /// produire un fichier de sauvegarde corrompu ou incohérent (un
    /// "torn read"), sans qu'aucune erreur ne soit levée au moment de la
    /// copie — le problème n'apparaît que le jour où on a besoin de restaurer
    /// cette sauvegarde, c'est-à-dire le pire moment possible pour le
    /// découvrir.
    ///
    /// On utilise désormais la commande native SQLite "VACUUM INTO", conçue
    /// spécifiquement pour produire une copie cohérente de la base même
    /// pendant qu'elle est utilisée (elle s'appuie sur les mêmes garanties
    /// transactionnelles que SQLite utilise pour toutes ses écritures).
    /// </summary>
    public class BackupService : IDisposable
    {
        private const int MaxBackups = 15;
        private const int IntervalHrs = 4;

        private readonly ILogger<BackupService> _logger;
        private readonly System.Threading.Timer _timer;

        public BackupService(ILogger<BackupService> logger)
        {
            _logger = logger;

            // Sauvegarde immédiate au démarrage, puis toutes les IntervalHrs heures
            _timer = new System.Threading.Timer(
                _ => Sauvegarder(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromHours(IntervalHrs));
        }

        public void Sauvegarder()
        {
            try
            {
                string dbSource = AppPaths.CheminBaseDeDonnees;
                if (!File.Exists(dbSource)) return;

                string nom = $"gestion_couture_{DateTime.Now:yyyyMMdd_HHmmss}.db";
                string dest = Path.Combine(AppPaths.DossierBackups, nom);

                // Supprime une éventuelle sauvegarde partielle d'un essai précédent
                if (File.Exists(dest)) File.Delete(dest);

                using (var connexion = new SqliteConnection($"Data Source={dbSource};Cache=Shared"))
                {
                    connexion.Open();
                    using var commande = connexion.CreateCommand();
                    // VACUUM INTO produit une copie cohérente et compactée,
                    // même si une écriture est en cours ailleurs dans l'appli.
                    commande.CommandText = "VACUUM INTO $dest";
                    commande.Parameters.AddWithValue("$dest", dest);
                    commande.ExecuteNonQuery();
                }

                _logger.LogInformation("Sauvegarde DB créée : {Fichier}", dest);

                // Rotation : supprime les plus anciennes si > MaxBackups
                var fichiers = Directory.GetFiles(AppPaths.DossierBackups, "gestion_couture_*.db")
                    .OrderByDescending(f => f)
                    .ToList();

                foreach (var ancien in fichiers.Skip(MaxBackups))
                {
                    File.Delete(ancien);
                    _logger.LogInformation("Ancienne sauvegarde supprimée : {Fichier}", ancien);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de la sauvegarde automatique");
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
