using System.IO;
using System.IO.Compression;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using DriveData = Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using GestionCoutureApp.Helpers;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Sauvegarde Google Drive (Option A — wa.me style, gratuit).
    ///
    /// Ce que fait ce service :
    ///  1. Crée un .zip horodaté contenant la DB SQLite + le dossier photos compressées.
    ///  2. Recherche (ou crée) un dossier "Retouche Choco Backups" dans le Drive de l'utilisateur.
    ///  3. Uploade le .zip dans ce dossier Drive.
    ///  4. Conserve les 10 derniers backups Drive (rotation automatique).
    ///
    /// Authentification : OAuth2 via client_secret.json (installé à la racine du projet
    /// et copié dans le dossier de sortie). Le token est mis en cache dans
    /// %LOCALAPPDATA%\GestionCoutureApp\DriveToken\ après le premier consentement.
    /// </summary>
    public class GoogleDriveBackupService
    {
        private const int MaxBackupsDrive = 10;
        private const string NomDossierDrive = "Retouche Choco Backups";
        private const string MimeZip = "application/zip";
        private const string MimeDossier = "application/vnd.google-apps.folder";

        private readonly ILogger<GoogleDriveBackupService> _logger;

        // Dernier statut — lu par ParametresView pour la pastille
        public string StatutDerniereSync { get; private set; } = "Jamais synchronisé";
        public DateTime? DateDerniereSync { get; private set; }
        public bool DerniereSyncReussie { get; private set; } = false;

        public GoogleDriveBackupService(ILogger<GoogleDriveBackupService> logger)
        {
            _logger = logger;
        }

        // ==================================================================
        // Point d'entrée principal — appelé manuellement ou à la fermeture
        // ==================================================================
        public async Task<bool> SauvegarderAsync(IProgress<string>? progression = null)
        {
            try
            {
                progression?.Report("Préparation de l'archive…");

                // 1. Créer l'archive .zip
                string cheminZip = await CreerArchiveAsync();
                string nomZip    = Path.GetFileName(cheminZip);

                progression?.Report("Connexion à Google Drive…");

                // 2. Authentifier et obtenir le service Drive
                var driveService = await ObtenirServiceDriveAsync();

                progression?.Report("Recherche du dossier Drive…");

                // 3. Trouver ou créer le dossier Drive
                string idDossier = await ObtenirOuCreerDossierAsync(driveService);

                progression?.Report($"Upload de {nomZip}…");

                // 4. Upload
                await UploaderFichierAsync(driveService, idDossier, cheminZip, nomZip);

                progression?.Report("Nettoyage des anciens backups Drive…");

                // 5. Rotation
                await SupprimerAnciensBackupsAsync(driveService, idDossier);

                // 6. Supprimer le zip temporaire local
                try { File.Delete(cheminZip); } catch { /* pas critique */ }

                StatutDerniereSync   = "✅ Synchronisé";
                DateDerniereSync     = DateTime.Now;
                DerniereSyncReussie  = true;

                _logger.LogInformation("Backup Google Drive réussi : {Nom}", nomZip);
                progression?.Report("✅ Sauvegarde Google Drive terminée !");
                return true;
            }
            catch (Exception ex)
            {
                StatutDerniereSync  = "❌ Échec : " + ex.Message;
                DerniereSyncReussie = false;
                _logger.LogError(ex, "Échec backup Google Drive");
                progression?.Report("❌ Erreur : " + ex.Message);
                return false;
            }
        }

        // ==================================================================
        // Création de l'archive .zip (DB + photos)
        // ==================================================================
        private static async Task<string> CreerArchiveAsync()
        {
            string horodatage = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            string nomZip     = $"Backup_RetoucheChoco_{horodatage}.zip";
            string cheminZip  = Path.Combine(Path.GetTempPath(), nomZip);

            if (File.Exists(cheminZip)) File.Delete(cheminZip);

            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(cheminZip, ZipArchiveMode.Create);

                // DB SQLite (VACUUM INTO dans un fichier temp pour cohérence)
                string dbSource = AppPaths.CheminBaseDeDonnees;
                if (File.Exists(dbSource))
                {
                    string dbTemp = dbSource + ".backup_temp";
                    try
                    {
                        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                            $"Data Source={dbSource};Cache=Shared");
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "VACUUM INTO $dest";
                        cmd.Parameters.AddWithValue("$dest", dbTemp);
                        cmd.ExecuteNonQuery();
                        archive.CreateEntryFromFile(dbTemp, "gestion_couture.db",
                            CompressionLevel.Optimal);
                    }
                    finally
                    {
                        try { File.Delete(dbTemp); } catch { }
                    }
                }

                // Dossier photos
                string dossierPhotos = AppPaths.DossierPhotos;
                if (Directory.Exists(dossierPhotos))
                {
                    foreach (string photo in Directory.GetFiles(dossierPhotos, "*.*",
                        SearchOption.AllDirectories))
                    {
                        string relatif = Path.GetRelativePath(
                            Path.GetDirectoryName(dossierPhotos)!, photo);
                        archive.CreateEntryFromFile(photo,
                            "photos/" + Path.GetFileName(photo),
                            CompressionLevel.Fastest); // photos déjà compressées
                    }
                }
            });

            return cheminZip;
        }

        // ==================================================================
        // Authentification Google OAuth2
        // ==================================================================
        private static async Task<DriveService> ObtenirServiceDriveAsync()
        {
            // Chercher client_secret.json dans le dossier de l'exécutable
            string[] candidats = {
                Path.Combine(AppContext.BaseDirectory, "client_secret.json"),
                Path.Combine(AppPaths.DossierApplication, "client_secret.json"),
                "client_secret.json"
            };

            string? cheminSecret = candidats.FirstOrDefault(File.Exists);
            if (cheminSecret == null)
                throw new FileNotFoundException(
                    "client_secret.json introuvable. Placez-le dans le dossier de l'application.");

            string dossierToken = Path.Combine(AppPaths.DossierApplication, "DriveToken");
            Directory.CreateDirectory(dossierToken);

            UserCredential credential;
            using (var stream = new FileStream(cheminSecret, FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    new[] { DriveService.Scope.DriveFile },
                    "retouchechoco_boss",
                    CancellationToken.None,
                    new FileDataStore(dossierToken, true));
            }

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName       = "Retouche Choco Backup"
            });
        }

        // ==================================================================
        // Dossier Drive
        // ==================================================================
        private static async Task<string> ObtenirOuCreerDossierAsync(DriveService service)
        {
            // Chercher le dossier existant
            var req = service.Files.List();
            req.Q          = $"name='{NomDossierDrive}' and mimeType='{MimeDossier}' and trashed=false";
            req.Fields     = "files(id,name)";
            req.Spaces     = "drive";
            var resultat   = await req.ExecuteAsync();

            if (resultat.Files.Count > 0)
                return resultat.Files[0].Id;

            // Créer le dossier
            var meta = new DriveData.File
            {
                Name     = NomDossierDrive,
                MimeType = MimeDossier
            };
            var cree = await service.Files.Create(meta).ExecuteAsync();
            return cree.Id;
        }

        // ==================================================================
        // Upload
        // ==================================================================
        private static async Task UploaderFichierAsync(
            DriveService service, string idDossier, string cheminZip, string nomZip)
        {
            var meta = new DriveData.File
            {
                Name    = nomZip,
                Parents = new List<string> { idDossier }
            };

            using var stream = new FileStream(cheminZip, FileMode.Open, FileAccess.Read);
            var upload = service.Files.Create(meta, stream, MimeZip);
            upload.Fields = "id,name";
            var result = await upload.UploadAsync();

            if (result.Status == Google.Apis.Upload.UploadStatus.Failed)
                throw new Exception("Upload échoué : " + result.Exception?.Message);
        }

        // ==================================================================
        // Rotation — conserve les MaxBackupsDrive derniers fichiers
        // ==================================================================
        private static async Task SupprimerAnciensBackupsAsync(DriveService service, string idDossier)
        {
            var req    = service.Files.List();
            req.Q      = $"'{idDossier}' in parents and name contains 'Backup_RetoucheChoco' and trashed=false";
            req.Fields = "files(id,name,createdTime)";
            req.OrderBy = "createdTime desc";
            var res    = await req.ExecuteAsync();

            var anciens = res.Files.Skip(MaxBackupsDrive).ToList();
            foreach (var f in anciens)
                await service.Files.Delete(f.Id).ExecuteAsync();
        }
    }
}
