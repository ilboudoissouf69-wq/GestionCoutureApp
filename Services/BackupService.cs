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
        // ÉTAPE 7 PERF — rétention 7 sauvegardes, au plus 1 fois par jour
        private const int MaxBackups   = 7;
        private const int IntervalHrs  = 24;

        private readonly ILogger<BackupService> _logger;
        private readonly System.Threading.Timer _timer;
        // Chemin du dernier marqueur "déjà sauvegardé aujourd'hui"
        private static string MarqueurFichier =>
            Path.Combine(AppPaths.DossierBackups, ".last_backup_date");

        public BackupService(ILogger<BackupService> logger)
        {
            _logger = logger;

            // Sauvegarde en arrière-plan (VACUUM INTO est non-bloquant pour les lecteurs).
            // Délai initial de 30s pour laisser le démarrage de l'app se terminer.
            _timer = new System.Threading.Timer(
                _ => SauvegarderSiNecessaire(),
                null,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromHours(IntervalHrs));
        }

        /// <summary>Sauvegarde uniquement si aucune sauvegarde n'a été faite aujourd'hui.</summary>
        public void SauvegarderSiNecessaire()
        {
            try
            {
                string today = DateTime.Today.ToString("yyyyMMdd");
                if (File.Exists(MarqueurFichier) && File.ReadAllText(MarqueurFichier).Trim() == today)
                {
                    _logger.LogDebug("Sauvegarde ignorée : déjà effectuée aujourd'hui ({Date}).", today);
                    return;
                }
                Sauvegarder();
                File.WriteAllText(MarqueurFichier, today);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vérification 'déjà sauvegardé' échouée, sauvegarde forcée.");
                Sauvegarder();
            }
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

                using (var connexion = new SqliteConnection($"Data Source={dbSource}"))
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

                // Copie vers l'emplacement EXTERNE, si configuré.
                // Un échec ici ne doit JAMAIS faire échouer la sauvegarde locale déjà réussie.
                CopierVersEmplacementExterne(dest, nom);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de la sauvegarde automatique");
            }
        }

        private void CopierVersEmplacementExterne(string cheminSauvegardeLocale, string nomFichier)
        {
            string? dossierExterne = AppPaths.CheminSauvegardeExterne;
            if (string.IsNullOrEmpty(dossierExterne)) return;

            try
            {
                if (!Directory.Exists(dossierExterne))
                {
                    _logger.LogWarning(
                        "Sauvegarde externe configurée mais introuvable : {Dossier} " +
                        "(clé USB débranchée ? partage réseau hors ligne ?)", dossierExterne);
                    return;
                }

                string destExterne = Path.Combine(dossierExterne, nomFichier);
                File.Copy(cheminSauvegardeLocale, destExterne, overwrite: true);
                _logger.LogInformation("Sauvegarde externe créée : {Fichier}", destExterne);

                var fichiersExternes = Directory.GetFiles(dossierExterne, "gestion_couture_*.db")
                    .OrderByDescending(f => f)
                    .ToList();

                foreach (var ancien in fichiersExternes.Skip(MaxBackups))
                {
                    File.Delete(ancien);
                    _logger.LogInformation("Ancienne sauvegarde externe supprimée : {Fichier}", ancien);
                }
            }
            catch (Exception ex)
            {
                // On journalise mais on ne propage jamais : la sauvegarde locale a déjà réussi.
                _logger.LogWarning(ex, "Échec de la copie vers l'emplacement de sauvegarde externe");
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
            // ÉTAPE 7 PERF — PRAGMA optimize à la fermeture (met à jour les statistiques du query planner)
            try
            {
                string dbSource = AppPaths.CheminBaseDeDonnees;
                if (!File.Exists(dbSource)) return;
                using var conn = new SqliteConnection($"Data Source={dbSource}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA optimize;";
                cmd.ExecuteNonQuery();
                _logger.LogDebug("PRAGMA optimize exécuté à la fermeture.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PRAGMA optimize échoué (non critique).");
            }
        }
    }
}
