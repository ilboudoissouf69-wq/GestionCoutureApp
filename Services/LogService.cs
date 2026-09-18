using Serilog;
using System.IO;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Implémentation du service de logging utilisant Serilog.
    /// Les logs sont sauvegardés dans AppData\Roaming\GestionCouture\Logs
    /// avec rotation quotidienne automatique.
    /// </summary>
    public class LogService : ILogService
    {
        private readonly ILogger _logger;
        private readonly string _cheminDossierLogs;

        public LogService()
        {
            // Définir le chemin des logs : AppData\Roaming\GestionCouture\Logs
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cheminDossierLogs = Path.Combine(appData, "GestionCouture", "Logs");

            // Créer le dossier s'il n'existe pas
            if (!Directory.Exists(_cheminDossierLogs))
            {
                Directory.CreateDirectory(_cheminDossierLogs);
            }

            // Configuration de Serilog
            string cheminFichierLog = Path.Combine(_cheminDossierLogs, "gestion_couture_.log");

            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: cheminFichierLog,
                    rollingInterval: RollingInterval.Day,           // Nouveau fichier chaque jour
                    retainedFileCountLimit: 30,                     // Garder 30 jours d'historique
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Utilisateur}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            // Log de démarrage
            _logger.Information("═══════════════════════════════════════════════════");
            _logger.Information("Application Gestion Couture démarrée");
            _logger.Information("Version: 2.0 - Retouche Choco");
            _logger.Information("═══════════════════════════════════════════════════");
        }

        public void LogInfo(string message, string? utilisateur = null)
        {
            _logger
                .ForContext("Utilisateur", utilisateur ?? "SYSTÈME")
                .Information(message);
        }

        public void LogWarning(string message, string? utilisateur = null)
        {
            _logger
                .ForContext("Utilisateur", utilisateur ?? "SYSTÈME")
                .Warning(message);
        }

        public void LogError(string message, Exception? exception = null, string? utilisateur = null)
        {
            if (exception != null)
            {
                _logger
                    .ForContext("Utilisateur", utilisateur ?? "SYSTÈME")
                    .Error(exception, message);
            }
            else
            {
                _logger
                    .ForContext("Utilisateur", utilisateur ?? "SYSTÈME")
                    .Error(message);
            }
        }

        public void LogAction(string action, string details, string? utilisateur = null)
        {
            _logger
                .ForContext("Utilisateur", utilisateur ?? "SYSTÈME")
                .Information("ACTION: {Action} | {Details}", action, details);
        }

        public string ObtenirCheminDossierLogs()
        {
            return _cheminDossierLogs;
        }
    }
}
