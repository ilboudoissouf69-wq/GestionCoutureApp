using System.Windows;
using System.Data;
using System.Diagnostics;
using System.Windows.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using GestionCoutureApp.Views;

namespace GestionCoutureApp
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ✅ Activer la détection et le logging des erreurs de binding WPF
            // Ces erreurs sont normalement silencieuses et peuvent causer des bugs difficiles à détecter
            ConfigurerBindingErrorLogging();

            // ====== Configuration DI ======
            var services = new ServiceCollection();

            // ------------------------------------------------------------------
            // IMPORTANT : on utilise une FACTORY plutot qu'un DbContext Singleton.
            // Un DbContext EF Core n'est pas concu pour vivre pendant toute une
            // session applicative (fuite memoire progressive : le "change
            // tracker" accumule toutes les entites chargees depuis le demarrage,
            // et deux operations concurrentes sur le meme DbContext levent une
            // exception). Chaque ecran / operation cree desormais son propre
            // DbContext de courte duree via IDbContextFactory, puis le "dispose".
            // C'est le pattern recommande par Microsoft pour les apps WPF/WinForms.
            // ------------------------------------------------------------------
            // Logs structurés (sortie debug VS + Event Log Windows)
            services.AddLogging(logging =>
            {
                logging.AddDebug();
                // ✅ CORRECTIF AUDIT #4 : Niveau Warning pour production
                // En production, on limite aux avertissements et erreurs pour éviter
                // de surcharger les logs avec des informations de debug/trace.
                logging.SetMinimumLevel(LogLevel.Warning);
            });

            // CORRECTIF : la base est désormais stockée dans %LOCALAPPDATA%
            // (voir Helpers/AppPaths.cs) au lieu d'un chemin relatif, qui
            // dépendait du répertoire de lancement et posait des problèmes
            // de droits d'écriture une fois l'app installée dans Program Files.
            services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseSqlite(AppPaths.ChaineConnexionSqlite));

            // Services
            services.AddSingleton<ILogService, LogService>();  // Service de logging
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IClientService, ClientService>();
            services.AddSingleton<ICommandeService, CommandeService>();
            services.AddSingleton<IPaiementService, PaiementService>();
            services.AddSingleton<ICommissionService, CommissionService>();
            services.AddSingleton<ITypeVetementService, TypeVetementService>();
            services.AddSingleton<IParametresService, ParametresService>();
            services.AddSingleton<IRetourService, RetourService>();
            services.AddSingleton<IAlerteService, AlerteService>();
            services.AddSingleton<IDepenseService, DepenseService>();
            services.AddSingleton<IMaterielService, MaterielService>();
            services.AddSingleton<IWhatsAppService, WhatsAppService>();
            services.AddSingleton<ITresorerieService, TresorerieService>();
            services.AddSingleton<IAuditService, AuditService>();
            
            // ✅ CORRECTIF AUDIT #14 : EventAggregator pour notifications globales
            services.AddSingleton<IEventAggregator, EventAggregator>();
            
            // ✅ CORRECTIF AUDIT #15 : LanguageService pour multilinguisme dynamique
            services.AddSingleton<ILanguageService, LanguageService>();
            
            // ✅ CORRECTIF AUDIT #16 : ThemeService pour gestion dynamique des couleurs
            services.AddSingleton<IThemeService, ThemeService>();
            
            // ✅ CORRECTIF AUDIT #17 : ReceiptService pour synchronisation des infos reçus
            services.AddSingleton<IReceiptService, ReceiptService>();

            // Sauvegarde automatique
            services.AddSingleton<BackupService>();
            services.AddSingleton<GoogleDriveBackupService>();

            Services = services.BuildServiceProvider();

            // Initialiser le service de logging
            var logService = Services.GetRequiredService<ILogService>();
            logService.LogInfo("═══════════════════════════════════════════════════");
            logService.LogInfo("Application Gestion Couture démarrée");
            logService.LogInfo($"Version: 2.0 - Retouche Choco");
            logService.LogInfo($"Date de démarrage: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            logService.LogInfo("═══════════════════════════════════════════════════");

            // ✅ TESTS EDGE CASES : Exécuter les tests de robustesse au démarrage
            // Désactivé en production - activer uniquement pour les tests
            /*
            try
            {
                string edgeCaseReport = EdgeCaseValidator.RunEdgeCaseTests();
                logService.LogInfo("TESTS EDGE CASES EXÉCUTÉS AVEC SUCCÈS");
                
                // Sauvegarder le rapport dans un fichier
                string reportPath = System.IO.Path.Combine(AppPaths.DossierApplication, "EdgeCaseReport.txt");
                System.IO.File.WriteAllText(reportPath, edgeCaseReport);
                logService.LogInfo($"Rapport sauvegardé: {reportPath}");
            }
            catch (Exception ex)
            {
                logService.LogError($"Erreur lors des tests edge cases: {ex.Message}");
            }
            */

            // Démarre la sauvegarde automatique dès le lancement
            Services.GetRequiredService<BackupService>();

            // ====== Créer la base ======
            try
            {
                var contextFactory = Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                using var context = contextFactory.CreateDbContext();

                // ── LOG DIAGNOSTIC (étape 1) : état AVANT Migrate() ──────────
                LogDiagnosticSchema(logService, context, "AVANT Migrate()");

                // ── Pré-correction : enregistrer les migrations déjà appliquées
                // manuellement (ancien bloc colonnesRetours) sans passer par EF Core.
                // Sans ça, Migrate() tente d'exécuter AddColumn sur des colonnes déjà
                // présentes → SQLite Error "duplicate column name".
                PreEnregistrerMigrationsAppliqueesManuellement(context, logService);

                // Migrate() applique les migrations que EF Core reconnaît.
                context.Database.Migrate();

                // ── APPLICATION MANUELLE des migrations ignorées par EF Core ──
                // EF Core ignore les migrations dont le .Designer.cs est minimal
                // (sans BuildTargetModel complet). On les applique manuellement
                // via ADO.NET en vérifiant __EFMigrationsHistory pour l'idempotence.
                AppliquerMigrationsManquantes(context, logService);

                // ── LOG DIAGNOSTIC (étape 2) : état APRÈS tout ───────────────
                LogDiagnosticSchema(logService, context, "APRÈS Migrate() + corrections");

                // ── Vérification bloquante du schéma ─────────────────────────
                // Colonnes critiques dont l'absence cause des crashes immédiats.
                // Noms exacts tels qu'ils apparaissent dans la base SQLite.
                var tablesCritiques = new Dictionary<string, string[]>
                {
                    ["Mesures"]  = new[] { "IdMesure", "IdCommande", "NomMesure", "Valeur", "IdPieceCommande" },
                    ["Commandes"] = new[] { "IdCommande", "IdClient", "DateDebut", "DateFin",
                                           "MontantTotal", "Statut", "TypeVetement",
                                           "EstSupprimee", "IdOperateurCreation" },
                    ["Paiements"] = new[] { "IdPaiement", "IdCommande", "MontantPaye",
                                           "DatePaiement", "ModePaiement", "IdOperateur" },
                    ["Depenses"]  = new[] { "IdDepense", "TypeDepense", "Montant",
                                           "Categorie", "StatutValidation", "IdOperateur" },
                    ["MaterielsSupplements"] = new[] { "IdMateriel", "IdCommande",
                                                       "IdOperateur", "NomOperateur" },
                    ["JournalAudit"] = new[] { "IdJournal", "DateHeureUtc", "TypeAction",
                                              "HashCourant" },
                };

                var erreursSchema = VerifierSchemaComplet(context, tablesCritiques);

                if (erreursSchema.Length > 0)
                {
                    string messageErreur =
                        $"Schéma de base de données incohérent après Migrate() :\n\n{erreursSchema}\n\n" +
                        $"Base : {AppPaths.CheminBaseDeDonnees}\n\n" +
                        "Supprimez le fichier .db (et .db-wal / .db-shm s'ils existent) " +
                        "puis relancez l'application pour recréer un schéma propre.";

                    logService.LogError("[SCHEMA] " + messageErreur);
                    MessageBox.Show(
                        messageErreur,
                        "Erreur de schéma — démarrage impossible",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown(-1);
                    return;
                }

                logService.LogInfo("[SCHEMA] Toutes les tables critiques sont cohérentes.");

                // ── Colonnes retours ajoutées progressivement (idempotent) ──
                // ALTER TABLE en SQLite ne supporte pas IF NOT EXISTS.
                // On utilise PRAGMA table_info pour tester l'existence avant d'ajouter.
                var colonnesRetours = new (string col, string def)[]
                {
                    ("EstAnnule",          "INTEGER NOT NULL DEFAULT 0"),
                    ("MotifAnnulation",    "TEXT"),
                    ("DateAnnulation",     "TEXT"),
                    ("NomAnnulateur",      "TEXT"),
                    ("IdCouturierReprise", "INTEGER"),
                    ("CheminPhotoDefaut",  "TEXT"),
                    ("DateRdvReprise",     "TEXT"),
                    ("HeureDebutReprise",  "TEXT"),
                    ("HeureFinReprise",    "TEXT"),
                };

                foreach (var (col, def) in colonnesRetours)
                {
                    try
                    {
                        // Vérifier via ADO.NET direct (plus simple que SqlQueryRaw<T>)
                        var conn = context.Database.GetDbConnection();
                        if (conn.State != System.Data.ConnectionState.Open)
                            conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Retours') WHERE name='{col}'";
                        long count = (long)(cmd.ExecuteScalar() ?? 0L);
                        if (count == 0)
                        {
                            FormattableString sql = $"ALTER TABLE Retours ADD COLUMN {col} {def};";
                            context.Database.ExecuteSql(sql);
                        }
                    }
                    catch { /* colonne déjà présente ou table inexistante */ }
                }

                // ── Colonne PrimeQualite dans Commissions (idempotent) ──
                try
                {
                    var conn2 = context.Database.GetDbConnection();
                    if (conn2.State != System.Data.ConnectionState.Open) conn2.Open();
                    using var cmd2 = conn2.CreateCommand();
                    cmd2.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Commissions') WHERE name='PrimeQualite'";
                    long cnt = (long)(cmd2.ExecuteScalar() ?? 0L);
                    if (cnt == 0)
                        context.Database.ExecuteSql($"ALTER TABLE Commissions ADD COLUMN PrimeQualite TEXT NOT NULL DEFAULT '0';");
                }
                catch { /* déjà présente */ }

                // ── Colonnes Depenses ajoutées progressivement (idempotent) ──
                var colonnesDepenses = new (string col, string def)[]
                {
                    ("Categorie",        "TEXT NOT NULL DEFAULT 'Divers'"),
                    ("StatutValidation", "TEXT NOT NULL DEFAULT 'Validee'"),
                };
                foreach (var (col, def) in colonnesDepenses)
                {
                    try
                    {
                        var conn3 = context.Database.GetDbConnection();
                        if (conn3.State != System.Data.ConnectionState.Open) conn3.Open();
                        using var cmd3 = conn3.CreateCommand();
                        cmd3.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Depenses') WHERE name='{col}'";
                        long cnt3 = (long)(cmd3.ExecuteScalar() ?? 0L);
                        if (cnt3 == 0)
                        {
                            FormattableString sql = $"ALTER TABLE Depenses ADD COLUMN {col} {def};";
                            context.Database.ExecuteSql(sql);
                        }
                    }
                    catch { /* déjà présente */ }
                }

                // CORRECTIF (robustesse concurrence) : le mode journal par défaut de
                // SQLite ("DELETE" / rollback journal) bloque tous les lecteurs pendant
                // qu'une écriture est en cours. Le mode WAL (Write-Ahead Logging) permet
                // aux lectures de continuer pendant une écriture, ce qui réduit fortement
                // les erreurs "database is locked" quand plusieurs opérations se
                // chevauchent (ex. la sauvegarde automatique VACUUM INTO en tâche de fond
                // pendant que l'utilisateur enregistre un paiement). PRAGMA journal_mode
                // est persistant dans le fichier .db : l'exécuter au démarrage à chaque
                // lancement garantit qu'il reste actif même après une restauration
                // manuelle d'une ancienne sauvegarde qui n'aurait pas ce mode.
                // ✅ CORRECTIF AUDIT #5 : Utilisation de ExecuteSql
                context.Database.ExecuteSql($"PRAGMA journal_mode=WAL;");

                // Compte Boss par défaut : créé UNE SEULE FOIS au tout premier lancement.
                // On ne touche plus jamais à son mot de passe ensuite (sinon un Boss qui a
                // changé son mot de passe se le voit réinitialisé à "boss123" à chaque démarrage,
                // ce qui est à la fois une faille de sécurité et un bug fonctionnel).
                bool aucunEmploye = !context.Employes.Any();
                if (aucunEmploye)
                {
                    var boss = new Employe
                    {
                        Nom = "Admin",
                        Prenom = "Boss",
                        Identifiant = "boss",
                        MotDePasse = PasswordHasher.Hasher("boss123"),
                        Role = "Boss",
                        Statut = "Actif"
                    };
                    context.Employes.Add(boss);
                    context.SaveChanges();

                    MessageBox.Show(
                        "Compte administrateur créé.\nIdentifiant : boss\nMot de passe : boss123\n\n" +
                        "IMPORTANT : changez ce mot de passe immédiatement après votre première connexion.",
                        "Premier démarrage", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Types de vêtements initiaux avec descriptions
                if (!context.TypesVetements.Any())
                {
                    var descriptionsPantalon = new List<DescriptionCourante>
                    {
                        new DescriptionCourante { Texte = "Coupe droite classique" },
                        new DescriptionCourante { Texte = "Coupe slim / ajustée" },
                        new DescriptionCourante { Texte = "Avec poches latérales" },
                        new DescriptionCourante { Texte = "Avec pinces" }
                    };

                    var descriptionsChemise = new List<DescriptionCourante>
                    {
                        new DescriptionCourante { Texte = "Col chemise classique" },
                        new DescriptionCourante { Texte = "Col V" },
                        new DescriptionCourante { Texte = "Manches longues" },
                        new DescriptionCourante { Texte = "Manches courtes" }
                    };

                    var descriptionsRobe = new List<DescriptionCourante>
                    {
                        new DescriptionCourante { Texte = "Robe longue" },
                        new DescriptionCourante { Texte = "Robe midi" },
                        new DescriptionCourante { Texte = "Robe courte" },
                        new DescriptionCourante { Texte = "Avec ceinture" }
                    };

                    var descriptionsBoubou = new List<DescriptionCourante>
                    {
                        new DescriptionCourante { Texte = "Boubou classique" },
                        new DescriptionCourante { Texte = "Boubou brodé" },
                        new DescriptionCourante { Texte = "Boubou avec poche" }
                    };

                    var descriptionsVeste = new List<DescriptionCourante>
                    {
                        new DescriptionCourante { Texte = "Veste classique" },
                        new DescriptionCourante { Texte = "Veste cintrée" },
                        new DescriptionCourante { Texte = "Avec boutons" },
                        new DescriptionCourante { Texte = "Sans manches" }
                    };

                    var typesInitiaux = new List<TypeVetement>
                    {
                        new TypeVetement
                        {
                            Nom = "Pantalon",
                            PrixBase = 1000,
                            MesuresRequises = new List<MesureRequise>
                            {
                                new MesureRequise { NomMesure = "Longueur" },
                                new MesureRequise { NomMesure = "Tour de taille" },
                                new MesureRequise { NomMesure = "Tour de cuisse" },
                                new MesureRequise { NomMesure = "Entrejambe" },
                                new MesureRequise { NomMesure = "Bas de patte" }
                            },
                            Descriptions = descriptionsPantalon
                        },
                        new TypeVetement
                        {
                            Nom = "Chemise",
                            PrixBase = 1000,
                            MesuresRequises = new List<MesureRequise>
                            {
                                new MesureRequise { NomMesure = "Longueur dos" },
                                new MesureRequise { NomMesure = "Tour de poitrine" },
                                new MesureRequise { NomMesure = "Tour d'épaule" },
                                new MesureRequise { NomMesure = "Longueur manche" },
                                new MesureRequise { NomMesure = "Tour de poignet" }
                            },
                            Descriptions = descriptionsChemise
                        },
                        new TypeVetement
                        {
                            Nom = "Robe",
                            PrixBase = 1000,
                            MesuresRequises = new List<MesureRequise>
                            {
                                new MesureRequise { NomMesure = "Longueur" },
                                new MesureRequise { NomMesure = "Tour de poitrine" },
                                new MesureRequise { NomMesure = "Tour de taille" },
                                new MesureRequise { NomMesure = "Tour de hanches" },
                                new MesureRequise { NomMesure = "Longueur épaule" }
                            },
                            Descriptions = descriptionsRobe
                        },
                        new TypeVetement
                        {
                            Nom = "Boubou",
                            PrixBase = 1000,
                            MesuresRequises = new List<MesureRequise>
                            {
                                new MesureRequise { NomMesure = "Longueur" },
                                new MesureRequise { NomMesure = "Tour de poitrine" },
                                new MesureRequise { NomMesure = "Longueur manche" },
                                new MesureRequise { NomMesure = "Largeur col" }
                            },
                            Descriptions = descriptionsBoubou
                        },
                        new TypeVetement
                        {
                            Nom = "Veste",
                            PrixBase = 1000,
                            MesuresRequises = new List<MesureRequise>
                            {
                                new MesureRequise { NomMesure = "Longueur" },
                                new MesureRequise { NomMesure = "Tour de poitrine" },
                                new MesureRequise { NomMesure = "Tour de taille" },
                                new MesureRequise { NomMesure = "Longueur manche" },
                                new MesureRequise { NomMesure = "Épaisseur épaule" }
                            },
                            Descriptions = descriptionsVeste
                        }
                    };

                    context.TypesVetements.AddRange(typesInitiaux);
                    context.SaveChanges();
                }

                // ====== Données de démonstration ======
                // CORRECTIF CRITIQUE : DemoDataSeeder.Seeder() se déclenchait
                // automatiquement dès que la table Clients était vide — ce qui
                // est EXACTEMENT l'état d'une installation neuve chez un vrai
                // utilisateur. Résultat : n'importe quelle installation
                // "production" se retrouvait truffée de 350 faux clients,
                // ~600 fausses commandes et, surtout, de comptes employés
                // fictifs avec des mots de passe prévisibles et documentés
                // dans le code source (ex. identifiant "secretaire01" / mot de
                // passe "sec01pass", "couturier001" / "cou001pass" — voir
                // Data/DemoDataSeeder.cs). N'importe qui ayant lu (ou deviné)
                // ce schéma pouvait se connecter à l'application d'un vrai
                // client avec un accès Secrétaire ou Couturier.
                //
                // Le jeu de données de démo n'est désormais inséré que si on
                // le demande explicitement, en lançant l'application avec
                // l'argument "--demo" (ex. depuis un raccourci de
                // démonstration/formation, jamais pour un poste client réel).
                bool demandeDemoExplicite = e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
                if (demandeDemoExplicite)
                {
                    DemoDataSeeder.Seeder(context);
                }
            }
            catch (Exception ex)
            {
                // CORRECTIF : une erreur d'initialisation de la base (fichier
                // verrouillé, migration corrompue, disque plein, droits
                // insuffisants...) empêchait l'app de fonctionner correctement,
                // mais elle continuait quand même vers l'écran de connexion.
                // Résultat : l'utilisateur pouvait se connecter puis voir
                // l'app planter à la moindre lecture/écriture en base, sans
                // comprendre pourquoi. On arrête maintenant proprement
                // l'application dans ce cas, avec un message clair.
                MessageBox.Show(
                    "Erreur critique lors de l'initialisation de la base de données :\n\n" + ex.Message +
                    "\n\nL'application va se fermer. Si le problème persiste, vérifiez qu'aucune " +
                    "autre instance de l'application n'est ouverte et que le dossier\n" +
                    AppPaths.DossierApplication + "\nest accessible en écriture.",
                    "Erreur critique", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            var loginWindow = new LoginWindow();
            loginWindow.Show();
        }

        private void Application_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            var ex = e.Exception;
            string details = ex.Message;
            if (ex.InnerException != null)
                details += "\n\nCause : " + ex.InnerException.Message;
            details += "\n\n--- Stack ---\n" + ex.StackTrace;

            // Logger l'exception
            try
            {
                var logService = Services?.GetService<ILogService>();
                logService?.LogError("Exception non gérée dans le Dispatcher", ex);
            }
            catch { /* Si le logging échoue, on ne veut pas créer une nouvelle exception */ }

            MessageBox.Show("Erreur inattendue :\n" + details,
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                // Logger l'exception critique
                try
                {
                    var logService = Services?.GetService<ILogService>();
                    logService?.LogError("Exception critique non gérée dans AppDomain", ex);
                }
                catch { /* Si le logging échoue, on ne veut pas créer une nouvelle exception */ }

                MessageBox.Show(
                    "Erreur critique :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Logger la fermeture
            try
            {
                var logService = Services?.GetService<ILogService>();
                var authService = Services?.GetService<IAuthService>();
                string utilisateur = authService?.UtilisateurConnecte?.Nom ?? "Inconnu";
                logService?.LogInfo($"Fermeture de l'application par {utilisateur}");
            }
            catch { /* Si le logging échoue, on continue la fermeture normale */ }

            // Sauvegarde Google Drive automatique à la fermeture
            // (en tâche de fond, 2–3 secondes maximum)
            try
            {
                var driveBackup = Services.GetService<GoogleDriveBackupService>();
                if (driveBackup != null)
                {
                    // Fire-and-wait avec timeout 8 secondes pour ne pas bloquer la fermeture
                    var tache = driveBackup.SauvegarderAsync();
                    tache.Wait(TimeSpan.FromSeconds(8));
                }
            }
            catch { /* ne jamais bloquer la fermeture */ }

            base.OnExit(e);
        }

        /// <summary>
        /// Détecte les migrations dont le contenu a été appliqué manuellement (ancien code App.cs)
        /// sans que EF Core en soit informé, et les enregistre dans __EFMigrationsHistory.
        /// Sans ça, Migrate() tente d'exécuter AddColumn sur des colonnes existantes → crash.
        /// Doit être appelé AVANT context.Database.Migrate().
        /// </summary>
        private static void PreEnregistrerMigrationsAppliqueesManuellement(
            ApplicationDbContext context, ILogService log)
        {
            // Utilise une connexion ADO.NET SÉPARÉE pour éviter les conflits
            // avec la connexion EF Core qui sera utilisée par Migrate() juste après.
            string connStr = context.Database.GetConnectionString() ?? 
                             AppPaths.ChaineConnexionSqlite;
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
                conn.Open();

                // __EFMigrationsHistory peut ne pas exister sur une base toute fraîche
                using (var chkHist = conn.CreateCommand())
                {
                    chkHist.CommandText =
                        "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
                    if (chkHist.ExecuteScalar() == null)
                    {
                        log.LogInfo("[PRE-ENREG] __EFMigrationsHistory absente — base fraîche, Migrate() la créera.");
                        return;
                    }
                }

                var appliquees = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) appliquees.Add(r.GetString(0));
                }

                void EnregistrerSiColonneExiste(string migrationId, string table, string colonne)
                {
                    if (appliquees.Contains(migrationId)) return;
                    try
                    {
                        using var chk = conn.CreateCommand();
                        chk.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{colonne}'";
                        long existe = (long)(chk.ExecuteScalar() ?? 0L);
                        if (existe > 0)
                        {
                            using var ins = conn.CreateCommand();
                            ins.CommandText =
                                $"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) " +
                                $"VALUES ('{migrationId}', '8.0.11')";
                            ins.ExecuteNonQuery();
                            log.LogInfo($"[PRE-ENREG] {migrationId} enregistrée (colonnes déjà présentes).");
                            appliquees.Add(migrationId);
                        }
                    }
                    catch (Exception ex) { log.LogError($"[PRE-ENREG] {migrationId} : {ex.Message}", ex); }
                }

                void EnregistrerSiIndexExiste(string migrationId, string indexName)
                {
                    if (appliquees.Contains(migrationId)) return;
                    try
                    {
                        using var chk = conn.CreateCommand();
                        chk.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{indexName}'";
                        long existe = (long)(chk.ExecuteScalar() ?? 0L);
                        if (existe > 0)
                        {
                            using var ins = conn.CreateCommand();
                            ins.CommandText =
                                $"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) " +
                                $"VALUES ('{migrationId}', '8.0.11')";
                            ins.ExecuteNonQuery();
                            log.LogInfo($"[PRE-ENREG] {migrationId} enregistrée (index déjà présent).");
                            appliquees.Add(migrationId);
                        }
                    }
                    catch (Exception ex) { log.LogError($"[PRE-ENREG] {migrationId} : {ex.Message}", ex); }
                }

                EnregistrerSiColonneExiste("20260805205656_AjoutMotifExceptionPiece",             "Retours",  "EstAnnule");
                EnregistrerSiColonneExiste("20260916000000_AjoutRetoursChampsReprise",             "Retours",  "CheminPhotoDefaut");
                EnregistrerSiIndexExiste  ("20260918000000_AjoutContrainteUniqueRecuNumero",        "IX_Paiements_RecuNumero");
                EnregistrerSiColonneExiste("20260918000001_AjoutDerniereModificationMotDePasse",   "Employes", "DerniereModificationMotDePasse");
                EnregistrerSiColonneExiste("20260925000000_ConsolidationFinale",                   "Commandes","EstSupprimee");
            }
            catch (Exception ex)
            {
                log.LogError($"[PRE-ENREG] Erreur générale : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Applique manuellement les migrations que EF Core ignore (fichiers .Designer.cs
        /// minimaux sans BuildTargetModel complet). Vérifie __EFMigrationsHistory pour
        /// l'idempotence — chaque migration n'est appliquée qu'une seule fois.
        /// </summary>
        private static void AppliquerMigrationsManquantes(ApplicationDbContext context, ILogService log)
        {
            // Connexion ADO.NET séparée — ne pas réutiliser celle du context EF Core
            // qui vient d'exécuter Migrate() et peut avoir des locks residuels.
            string connStr = context.Database.GetConnectionString() ?? AppPaths.ChaineConnexionSqlite;
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
            conn.Open();

            // Lire les migrations déjà appliquées
            var appliquees = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
                using var r = cmd.ExecuteReader();
                while (r.Read()) appliquees.Add(r.GetString(0));
            }

            // Helper : exécute SQL idempotent et enregistre dans __EFMigrationsHistory
            void AppliquerSi(string migrationId, Action<System.Data.Common.DbConnection> action)
            {
                if (appliquees.Contains(migrationId)) return;
                log.LogInfo($"[MIGRATION MANUELLE] Application de {migrationId}...");
                try
                {
                    action(conn);
                    using var ins = conn.CreateCommand();
                    ins.CommandText =
                        "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) " +
                        $"VALUES ('{migrationId}', '8.0.11')";
                    ins.ExecuteNonQuery();
                    log.LogInfo($"[MIGRATION MANUELLE] {migrationId} appliquée avec succès.");
                }
                catch (Exception ex)
                {
                    log.LogError($"[MIGRATION MANUELLE] Erreur sur {migrationId} : {ex.Message}", ex);
                }
            }

            // Helper : ALTER TABLE idempotent (ignore "duplicate column name")
            void AjouterCol(System.Data.Common.DbConnection c, string table, string col, string def)
            {
                try
                {
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{col}\" {def}";
                    cmd.ExecuteNonQuery();
                }
                catch { /* colonne déjà présente — ignoré */ }
            }

            // Helper : CREATE INDEX IF NOT EXISTS
            void CreerIndex(System.Data.Common.DbConnection c, string sql)
            {
                try { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
                catch { }
            }

            // ── 20260916000000_AjoutRetoursChampsReprise ──────────────────────
            AppliquerSi("20260916000000_AjoutRetoursChampsReprise", c =>
            {
                AjouterCol(c, "Retours", "EstAnnule",          "INTEGER NOT NULL DEFAULT 0");
                AjouterCol(c, "Retours", "MotifAnnulation",    "TEXT NULL");
                AjouterCol(c, "Retours", "DateAnnulation",     "TEXT NULL");
                AjouterCol(c, "Retours", "NomAnnulateur",      "TEXT NULL");
                AjouterCol(c, "Retours", "IdCouturierReprise", "INTEGER NULL");
                AjouterCol(c, "Retours", "CheminPhotoDefaut",  "TEXT NULL");
                AjouterCol(c, "Retours", "DateRdvReprise",     "TEXT NULL");
                AjouterCol(c, "Retours", "HeureDebutReprise",  "TEXT NULL");
                AjouterCol(c, "Retours", "HeureFinReprise",    "TEXT NULL");
            });

            // ── 20260918000000_AjoutContrainteUniqueRecuNumero ────────────────
            AppliquerSi("20260918000000_AjoutContrainteUniqueRecuNumero", c =>
                CreerIndex(c, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Paiements_RecuNumero\" ON \"Paiements\" (\"RecuNumero\")"));

            // ── 20260918000001_AjoutDerniereModificationMotDePasse ────────────
            AppliquerSi("20260918000001_AjoutDerniereModificationMotDePasse", c =>
                AjouterCol(c, "Employes", "DerniereModificationMotDePasse", "TEXT NULL"));

            // ── 20260925000000_ConsolidationFinale ────────────────────────────
            AppliquerSi("20260925000000_ConsolidationFinale", c =>
            {
                // Commissions
                AjouterCol(c, "Commissions", "PrimeQualite", "TEXT NOT NULL DEFAULT '0'");

                // Paiements : recréer avec IdOperateur NOT NULL
                // On vérifie si la table Paiements_V2 existe déjà (migration interrompue)
                using (var chk = c.CreateCommand())
                {
                    chk.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Paiements_V2'";
                    long exists = (long)(chk.ExecuteScalar() ?? 0L);
                    if (exists == 0)
                    {
                        using var cr = c.CreateCommand();
                        cr.CommandText = @"CREATE TABLE ""Paiements_V2"" (
                            ""IdPaiement"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                            ""IdCommande"" INTEGER NOT NULL,
                            ""MontantPaye"" TEXT NOT NULL,
                            ""DatePaiement"" TEXT NOT NULL,
                            ""ModePaiement"" TEXT NOT NULL DEFAULT 'Especes',
                            ""RecuNumero"" TEXT NOT NULL DEFAULT '',
                            ""IdOperateur"" INTEGER NOT NULL DEFAULT 0,
                            ""NomOperateur"" TEXT NOT NULL DEFAULT '',
                            ""EstAnnule"" INTEGER NOT NULL DEFAULT 0,
                            ""MotifsAnnulation"" TEXT NULL,
                            ""DateAnnulation"" TEXT NULL,
                            ""NomAnnulateur"" TEXT NULL,
                            ""MontantTotalCommande"" TEXT NOT NULL DEFAULT '0',
                            ""ResteAvantPaiement"" TEXT NOT NULL DEFAULT '0',
                            FOREIGN KEY (""IdCommande"") REFERENCES ""Commandes""(""IdCommande"") ON DELETE RESTRICT
                        )";
                        cr.ExecuteNonQuery();
                    }
                }
                using (var ins = c.CreateCommand())
                {
                    ins.CommandText = @"INSERT OR IGNORE INTO ""Paiements_V2""
                        (""IdPaiement"",""IdCommande"",""MontantPaye"",""DatePaiement"",""ModePaiement"",
                         ""RecuNumero"",""IdOperateur"",""NomOperateur"",""EstAnnule"",""MotifsAnnulation"",
                         ""DateAnnulation"",""NomAnnulateur"",""MontantTotalCommande"",""ResteAvantPaiement"")
                        SELECT ""IdPaiement"",""IdCommande"",""MontantPaye"",""DatePaiement"",""ModePaiement"",
                               ""RecuNumero"",COALESCE(""IdOperateur"",0),""NomOperateur"",""EstAnnule"",
                               ""MotifsAnnulation"",""DateAnnulation"",""NomAnnulateur"",
                               COALESCE(""MontantTotalCommande"",'0'),COALESCE(""ResteAvantPaiement"",'0')
                        FROM ""Paiements""
                        WHERE ""IdPaiement"" NOT IN (SELECT ""IdPaiement"" FROM ""Paiements_V2"")";
                    ins.ExecuteNonQuery();
                }
                using (var drop = c.CreateCommand()) { drop.CommandText = "DROP TABLE \"Paiements\""; drop.ExecuteNonQuery(); }
                using (var ren = c.CreateCommand()) { ren.CommandText = "ALTER TABLE \"Paiements_V2\" RENAME TO \"Paiements\""; ren.ExecuteNonQuery(); }
                CreerIndex(c, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Paiements_RecuNumero\" ON \"Paiements\" (\"RecuNumero\")");

                // Employes
                AjouterCol(c, "Employes", "DerniereModificationMotDePasse", "TEXT NULL");

                // Depenses
                AjouterCol(c, "Depenses", "Categorie",        "TEXT NOT NULL DEFAULT 'Divers'");
                AjouterCol(c, "Depenses", "StatutValidation",  "TEXT NOT NULL DEFAULT 'Validee'");
                AjouterCol(c, "Depenses", "IdOperateur",       "INTEGER NOT NULL DEFAULT 0");

                // MaterielsSupplements
                AjouterCol(c, "MaterielsSupplements", "IdOperateur",  "INTEGER NOT NULL DEFAULT 0");
                AjouterCol(c, "MaterielsSupplements", "NomOperateur", "TEXT NOT NULL DEFAULT ''");

                // Commandes
                AjouterCol(c, "Commandes", "EstSupprimee",           "INTEGER NOT NULL DEFAULT 0");
                AjouterCol(c, "Commandes", "MotifSuppression",       "TEXT NULL");
                AjouterCol(c, "Commandes", "DateSuppression",        "TEXT NULL");
                AjouterCol(c, "Commandes", "IdOperateurSuppression", "INTEGER NULL");
                AjouterCol(c, "Commandes", "NomOperateurSuppression","TEXT NULL");
                AjouterCol(c, "Commandes", "IdOperateurCreation",    "INTEGER NOT NULL DEFAULT 0");
                AjouterCol(c, "Commandes", "NomOperateurCreation",   "TEXT NOT NULL DEFAULT ''");
                AjouterCol(c, "Commandes", "DateCreation",           "TEXT NOT NULL DEFAULT '2000-01-01 00:00:00'");

                using (var upd = c.CreateCommand())
                {
                    upd.CommandText = "UPDATE \"Commandes\" SET \"DateCreation\" = \"DateDebut\" WHERE \"DateCreation\" = '2000-01-01 00:00:00'";
                    upd.ExecuteNonQuery();
                }
                CreerIndex(c, "CREATE INDEX IF NOT EXISTS \"IX_Commandes_EstSupprimee\" ON \"Commandes\" (\"EstSupprimee\")");
                CreerIndex(c, "CREATE INDEX IF NOT EXISTS \"IX_Commandes_IdOperateurCreation\" ON \"Commandes\" (\"IdOperateurCreation\")");
                CreerIndex(c, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Clients_Telephone_Unique\" ON \"Clients\" (\"Telephone\") WHERE \"Telephone\" IS NOT NULL AND \"Telephone\" != ''");

                // Retours champs reprise
                AjouterCol(c, "Retours", "CheminPhotoDefaut",  "TEXT NULL");
                AjouterCol(c, "Retours", "DateRdvReprise",     "TEXT NULL");
                AjouterCol(c, "Retours", "HeureDebutReprise",  "TEXT NULL");
                AjouterCol(c, "Retours", "HeureFinReprise",    "TEXT NULL");
                AjouterCol(c, "Retours", "IdCouturierReprise", "INTEGER NULL");
                CreerIndex(c, "CREATE INDEX IF NOT EXISTS \"IX_Retours_IdCouturierReprise\" ON \"Retours\" (\"IdCouturierReprise\")");

                // JournalAudit
                using var jCmd = c.CreateCommand();
                jCmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""JournalAudit"" (
                    ""IdJournal"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""DateHeureUtc"" TEXT NOT NULL,
                    ""IdOperateur"" INTEGER NOT NULL,
                    ""NomOperateur"" TEXT NOT NULL,
                    ""RoleOperateur"" TEXT NOT NULL,
                    ""TypeAction"" TEXT NOT NULL,
                    ""Entite"" TEXT NOT NULL,
                    ""IdEntite"" INTEGER NOT NULL,
                    ""ValeursAvant"" TEXT NULL,
                    ""ValeursApres"" TEXT NULL,
                    ""Motif"" TEXT NULL,
                    ""HashPrecedent"" TEXT NULL,
                    ""HashCourant"" TEXT NOT NULL,
                    ""AdresseIp"" TEXT NULL,
                    ""NotificationEnvoyee"" INTEGER NOT NULL DEFAULT 0
                )";
                jCmd.ExecuteNonQuery();
                CreerIndex(c, "CREATE INDEX IF NOT EXISTS \"IX_JournalAudit_DateHeureUtc\" ON \"JournalAudit\" (\"DateHeureUtc\")");
                CreerIndex(c, "CREATE INDEX IF NOT EXISTS \"IX_JournalAudit_TypeAction\" ON \"JournalAudit\" (\"TypeAction\")");
            });
        }

        /// <summary>
        /// Log le chemin de la base, les migrations appliquées et le PRAGMA table_info
        /// des tables critiques — pour diagnostiquer les erreurs "no such column" au démarrage.
        /// </summary>
        private static void LogDiagnosticSchema(ILogService log, ApplicationDbContext context, string etape)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[DIAG SCHEMA {etape}]");
                sb.AppendLine($"  Base       : {AppPaths.CheminBaseDeDonnees}");
                sb.AppendLine($"  Horodatage : {DateTime.Now:dd/MM/yyyy HH:mm:ss.fff}");

                var conn = context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) conn.Open();

                // Migrations appliquées
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
                    bool historyExists = cmd.ExecuteScalar() != null;
                    if (historyExists)
                    {
                        cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
                        using var r = cmd.ExecuteReader();
                        sb.AppendLine("  Migrations appliquées :");
                        while (r.Read()) sb.AppendLine($"    ✓ {r.GetString(0)}");
                    }
                    else
                    {
                        sb.AppendLine("  __EFMigrationsHistory : TABLE ABSENTE (base vide ou non migrée)");
                    }
                }

                // PRAGMA table_info pour les tables critiques
                foreach (var table in new[] { "Mesures", "Commandes", "Paiements", "Depenses", "MaterielsSupplements" })
                {
                    using var cmd2 = conn.CreateCommand();
                    cmd2.CommandText = $"PRAGMA table_info('{table}')";
                    using var r2 = cmd2.ExecuteReader();
                    var cols = new System.Collections.Generic.List<string>();
                    while (r2.Read()) cols.Add(r2.GetString(1));
                    if (cols.Count == 0)
                        sb.AppendLine($"  {table} : TABLE ABSENTE");
                    else
                        sb.AppendLine($"  {table} ({cols.Count} colonnes) : {string.Join(", ", cols)}");
                }

                log.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                log.LogError($"[DIAG SCHEMA] Erreur lors du diagnostic ({etape}) : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Vérifie que toutes les colonnes attendues existent dans la base.
        /// Retourne une chaîne vide si tout est correct, ou la liste des écarts sinon.
        /// </summary>
        private static string VerifierSchemaComplet(
            ApplicationDbContext context,
            Dictionary<string, string[]> tablesCritiques)
        {
            var erreurs = new System.Text.StringBuilder();
            try
            {
                var conn = context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) conn.Open();

                foreach (var (table, colonnesAttendues) in tablesCritiques)
                {
                    // Vérifier d'abord que la table existe
                    using var cmdTable = conn.CreateCommand();
                    cmdTable.CommandText =
                        $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
                    long tableExiste = (long)(cmdTable.ExecuteScalar() ?? 0L);

                    if (tableExiste == 0)
                    {
                        erreurs.AppendLine($"  Table '{table}' : ABSENTE");
                        continue;
                    }

                    // Lire les colonnes réelles
                    using var cmdCols = conn.CreateCommand();
                    cmdCols.CommandText = $"PRAGMA table_info('{table}')";
                    using var reader = cmdCols.ExecuteReader();
                    var colonnesReelles = new System.Collections.Generic.HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    while (reader.Read()) colonnesReelles.Add(reader.GetString(1));

                    foreach (var col in colonnesAttendues)
                    {
                        if (!colonnesReelles.Contains(col))
                            erreurs.AppendLine($"  Table '{table}' : colonne manquante '{col}'");
                    }
                }
            }
            catch (Exception ex)
            {
                erreurs.AppendLine($"  Exception lors de la vérification du schéma : {ex.Message}");
            }
            return erreurs.ToString();
        }

        /// <summary>
        /// Configure le système de détection et logging des erreurs de binding WPF.
        /// 
        /// Les erreurs de binding WPF sont normalement silencieuses (invisible dans l'app)
        /// et peuvent causer des bugs difficiles à diagnostiquer comme:
        /// - Des valeurs qui ne s'affichent pas
        /// - Des contrôles qui restent vides
        /// - Des crashes silencieux dans les ValidationRules
        /// - Des bindings circulaires
        /// 
        /// Cette méthode active un listener qui capture ces erreurs et les écrit dans:
        /// - Un fichier de log dédié: %LOCALAPPDATA%\GestionCoutureApp\Logs\BindingErrors_[date].log
        /// - La fenêtre Output de Visual Studio (pour le développement)
        /// </summary>
        private void ConfigurerBindingErrorLogging()
        {
            try
            {
                // Activer le tracing des erreurs de binding WPF
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;
                
                // Ajouter notre listener personnalisé
                var bindingErrorListener = new BindingErrorTraceListener();
                PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrorListener);
                
                // Logger l'activation (via le système de log une fois qu'il est disponible)
                // Note: On le fait après l'initialisation des services dans OnStartup
            }
            catch (Exception ex)
            {
                // Ne jamais crasher l'app à cause du logging
                Debug.WriteLine($"Erreur lors de la configuration du binding error logging: {ex.Message}");
            }
        }
    }
}
