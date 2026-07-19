using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Sauvegarde automatique de gestion_couture.db dans un dossier Backups/.
    /// — Au démarrage de l'application (copie immédiate)
    /// — Toutes les 4 heures tant que l'appli est ouverte
    /// — Conserve les 15 dernières sauvegardes (rotation automatique)
    /// </summary>
    public class BackupService : IDisposable
    {
        private const string DbSource    = "gestion_couture.db";
        private const string BackupDir   = "Backups";
        private const int    MaxBackups  = 15;
        private const int    IntervalHrs = 4;

        private readonly ILogger<BackupService> _logger;
        private readonly System.Threading.Timer _timer;

        public BackupService(ILogger<BackupService> logger)
        {
            _logger = logger;
            Directory.CreateDirectory(BackupDir);

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
                if (!File.Exists(DbSource)) return;

                string nom = $"gestion_couture_{DateTime.Now:yyyyMMdd_HHmmss}.db";
                string dest = Path.Combine(BackupDir, nom);

                File.Copy(DbSource, dest, overwrite: true);
                _logger.LogInformation("Sauvegarde DB créée : {Fichier}", dest);

                // Rotation : supprime les plus anciennes si > MaxBackups
                var fichiers = Directory.GetFiles(BackupDir, "gestion_couture_*.db")
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
