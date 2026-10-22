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

        // Verrou d'instance unique : empêche de lancer deux fenêtres en parallèle
        // (sinon deux minuteries de sauvegarde/alertes tournent sur la même base
        // SQLite → « database is locked » et sauvegardes en double).
        // Conservé en champ statique pour ne pas être libéré par le GC.
        private static Mutex? _instanceUnique;

        protected override void OnStartup(StartupEventArgs e)
        {
            // ── Instance unique ───────────────────────────────────────────────
            // Un Mutex nommé (portée session) est détenu jusqu'à la fermeture du
            // processus. Un second lancement ne peut pas le créer → on prévient
            // l'utilisateur et on quitte proprement (aucune fenêtre, aucun timer).
            _instanceUnique = new Mutex(initiallyOwned: true,
                name: @"Local\GestionCoutureApp_InstanceUnique_v1",
                createdNew: out bool premierLancement);

            if (!premierLancement)
            {
                MessageBox.Show(
                    "L'application Gestion Couture est déjà ouverte.\n\n" +
                    "Une seule instance est autorisée, pour protéger la base de données " +
                    "(sauvegardes et alertes en double sur la même base).",
                    "Gestion Couture", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // Filet de sécurité pour les exceptions non gérées sur les threads
            // d'arrière-plan (non-Dispatcher). L'handler Dispatcher n'intercepte
            // que les exceptions du thread UI ; AppDomain couvre le reste.
            AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;

            // Les erreurs de binding WPF sont silencieuses par défaut.
            // Cette configuration les écrit dans un fichier dédié pour faciliter le diagnostic.
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
            // Logs structurés (sortie debug VS)
            services.AddLogging(logging =>
            {
                logging.AddDebug();
                // En production, on limite aux avertissements et erreurs pour éviter
                // de noyer les logs avec des informations de debug.
                logging.SetMinimumLevel(LogLevel.Warning);
            });

            // La base est stockée dans %LOCALAPPDATA% (voir Helpers/AppPaths.cs)
            // pour garantir les droits d'écriture quelle que soit l'installation.
            services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseSqlite(AppPaths.ChaineConnexionSqlite,
                    // C2 : SplitQuery global — évite l'explosion cartésienne sur les Include
                    // à 4-5 collections (Paiements, MaterielSupplements, Pieces→Mesures,
                    // Pieces→MaterielSupplements). EF Core émet plusieurs SELECT ciblés au
                    // lieu d'un produit cartésien. Aucune migration nécessaire.
                    sqliteOpts => sqliteOpts.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

            // Services
            services.AddSingleton<ILogService, LogService>();  // Service de logging
            services.AddSingleton<IClock, SystemClock>();       // Phase 2 : horloge injectable
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IClientService, ClientService>();
            services.AddSingleton<IWhatsAppService, WhatsAppService>();
            services.AddSingleton<IParametresService, ParametresService>();
            // AuditService enregistré tôt : CommandeService en a besoin (Étape 10 — obligatoire)
            services.AddSingleton<IAuditService, AuditService>();
            services.AddSingleton<ICommandeService, CommandeService>();
            services.AddSingleton<IPaiementService, PaiementService>();
            services.AddSingleton<ICommissionService, CommissionService>();
            services.AddSingleton<ITypeVetementService, TypeVetementService>();
            // RetourService dépend de IAuditService — déclaré après ce dernier
            services.AddSingleton<IRetourService, RetourService>();
            services.AddSingleton<IAlerteService, AlerteService>();
            services.AddSingleton<IDepenseService, DepenseService>();
            services.AddSingleton<IMaterielService, MaterielService>();
            services.AddSingleton<ITresorerieService, TresorerieService>();
            services.AddSingleton<IMesureClientService, MesureClientService>();
            services.AddSingleton<IBrouillonCommandeService, BrouillonCommandeService>();
            services.AddSingleton<IRetardAlerteService, RetardAlerteService>();
            
            // EventAggregator pour notifications globales entre vues
            services.AddSingleton<IEventAggregator, EventAggregator>();
            
            // LanguageService pour multilinguisme dynamique
            services.AddSingleton<ILanguageService, LanguageService>();
            
            // ThemeService pour gestion dynamique des couleurs
            services.AddSingleton<IThemeService, ThemeService>();
            
            // ReceiptService pour synchronisation des informations de reçus
            services.AddSingleton<IReceiptService, ReceiptService>();

            // Sauvegarde automatique
            services.AddSingleton<BackupService>();
            services.AddSingleton<GoogleDriveBackupService>();

            Services = services.BuildServiceProvider();

            // ── Chronomètre global démarrage ─────────────────────────────
            var swDemarrage = System.Diagnostics.Stopwatch.StartNew();

            // Initialiser le service de logging
            var logService = Services.GetRequiredService<ILogService>();

            // Configure PerfLogger avec le logger Microsoft de l'app
            var perfLoggerFactory = Services.GetRequiredService<ILoggerFactory>();
            Helpers.PerfLogger.Configure(perfLoggerFactory.CreateLogger("PerfLogger"));
            logService.LogInfo("═══════════════════════════════════════════════════");
            logService.LogInfo("Application Gestion Couture démarrée");
            logService.LogInfo($"Version: 2.0 - Retouche Choco");
            logService.LogInfo($"Date de démarrage: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            logService.LogInfo("═══════════════════════════════════════════════════");

            // Démarre la sauvegarde automatique dès le lancement
            Services.GetRequiredService<BackupService>();

            // ====== Créer la base ======
            bool configurationInitialeRequise = false;
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
                                           "EstSupprimee", "IdOperateurCreation",
                                           "EstAnnulee", "DateImpressionRecu" },
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
                        "Contactez le support technique en fournissant le dossier des sauvegardes :\n" +
                        AppPaths.DossierBackups + "\n\n" +
                        "⚠️ Ne supprimez pas le fichier .db sans avoir d'abord " +
                        "sauvegardé vos données — vous perdriez toutes vos commandes et paiements.";

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

                // Mode WAL + foreign_keys.
                // journal_mode=WAL est persistant mais on le réapplique après restauration.
                // foreign_keys : réglage propre à CHAQUE connexion SQLite. Le PRAGMA
                // ci-dessous ne couvre que la connexion de ce contexte de démarrage ;
                // les connexions créées ensuite par la factory ont malgré tout les FK
                // actives car Microsoft.Data.Sqlite active foreign_keys par défaut à
                // l'ouverture (True). Le test Phase3_SchemaTests.SYS02_PRAGMA_ForeignKeys
                // verrouille ce comportement.
                context.Database.ExecuteSql($"PRAGMA journal_mode=WAL;");
                context.Database.ExecuteSql($"PRAGMA foreign_keys=ON;");

                // ── Configuration initiale (Phase 1) ─────────────────────────────
                // Au premier lancement (base vide), on affiche une fenêtre de configuration
                // qui demande l'identifiant et le mot de passe choisis par le propriétaire.
                // Aucun mot de passe par défaut n'est plus utilisé.
                configurationInitialeRequise = !context.Employes.Any();

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

                // Le jeu de données de démonstration n'est inséré que si l'application
                // est lancée avec l'argument "--demo" (raccourci dédié formation/demo).
                // Ne jamais activer ce mode sur un poste client réel.
                bool demandeDemoExplicite = e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
                if (demandeDemoExplicite)
                {
                    DemoDataSeeder.Seeder(context);
                }
            }
            catch (Exception ex)
            {
                // Une erreur d'initialisation de la base (fichier verrouillé, migration
                // corrompue, disque plein, droits insuffisants...) doit arrêter
                // proprement l'application plutôt que de laisser l'utilisateur
                // se connecter pour voir tout planter à la première lecture/écriture.
                MessageBox.Show(
                    "Erreur critique lors de l'initialisation de la base de données :\n\n" + ex.Message +
                    "\n\nL'application va se fermer. Si le problème persiste, vérifiez qu'aucune " +
                    "autre instance de l'application n'est ouverte et que le dossier\n" +
                    AppPaths.DossierApplication + "\nest accessible en écriture.",
                    "Erreur critique", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            // ── Configuration initiale ou connexion ──────────────────────────────
            if (configurationInitialeRequise)
            {
                // Première installation : aucun compte en base. On demande au
                // propriétaire de choisir son identifiant et son mot de passe.
                // Aucun mot de passe par défaut n'est jamais utilisé.
                var configWindow = new ConfigurationInitialeWindow();
                configWindow.ShowDialog();

                if (!configWindow.ConfigurationReussie || configWindow.EmployeCree == null)
                {
                    // L'utilisateur a fermé la fenêtre sans configurer (impossible par
                    // design, mais on se défend quand même).
                    Shutdown(0);
                    return;
                }

                // Persister le compte Boss créé
                try
                {
                    var contextFactory = Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                    using var ctx = contextFactory.CreateDbContext();
                    ctx.Employes.Add(configWindow.EmployeCree);
                    ctx.SaveChanges();

                    var logSvc = Services.GetService<ILogService>();
                    logSvc?.LogInfo(
                        $"[CONFIG-INITIALE] Compte Boss '{configWindow.EmployeCree.Identifiant}' créé.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Impossible de créer le compte administrateur :\n\n" + ex.Message,
                        "Erreur de configuration", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(-1);
                    return;
                }
            }

            swDemarrage.Stop();
            logService.LogInfo($"[PERF] Démarrage application complet — {swDemarrage.ElapsedMilliseconds} ms");
            System.Diagnostics.Debug.WriteLine($"[PERF] Démarrage application — {swDemarrage.ElapsedMilliseconds} ms");

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
                EnregistrerSiColonneExiste("20261010000000_Phase1_VerrouillageProgressif",         "Employes", "NbEchecConnexion");
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
            // qui vient d'exécuter Migrate() et peut avoir des locks résiduels.
            // IMPORTANT : on retire Cache=Shared de la chaîne de connexion.
            // Cache=Shared fait que SQLite partage le cache entre connexions du même
            // processus — ce qui peut cacher les colonnes fraîchement ajoutées par
            // Migrate() quand on ouvre une deuxième connexion juste après.
            string connStr = AppPaths.ChaineConnexionSqlite
                .Replace(";Cache=Shared", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Cache=Shared;", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Cache=Shared",  "", StringComparison.OrdinalIgnoreCase);
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
            // ── 20260925000000_ConsolidationFinale ────────────────────────────
            // Cette migration est un no-op EF Core (Up() vide) donc elle est toujours
            // marquée "appliquée" dans __EFMigrationsHistory dès que Migrate() tourne.
            // AppliquerSi() la sauterait immédiatement. On applique donc TOUJOURS les
            // colonnes de façon inconditionnelle — AjouterCol / CreateTable IF NOT EXISTS
            // / CreateIndex IF NOT EXISTS sont idempotents et sans risque.
            {
                log.LogInfo("[MIGRATION MANUELLE] Application inconditionnelle ConsolidationFinale...");

                // Commissions
                AjouterCol(conn, "Commissions", "PrimeQualite", "TEXT NOT NULL DEFAULT '0'");

                // Paiements : recréer avec IdOperateur NOT NULL seulement si encore nullable
                try
                {
                    using var chkPai = conn.CreateCommand();
                    chkPai.CommandText = "PRAGMA table_info(\"Paiements\")";
                    using var rPai = chkPai.ExecuteReader();
                    bool idOpNotNull = false;
                    while (rPai.Read())
                        if (rPai.GetString(1) == "IdOperateur" && rPai.GetInt32(3) == 1)
                            idOpNotNull = true;

                    if (!idOpNotNull)
                    {
                        using var chk = conn.CreateCommand();
                        chk.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Paiements_V2'";
                        long exists = (long)(chk.ExecuteScalar() ?? 0L);
                        if (exists == 0)
                        {
                            using var cr = conn.CreateCommand();
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
                        using (var ins = conn.CreateCommand())
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
                        using (var drop = conn.CreateCommand()) { drop.CommandText = "DROP TABLE \"Paiements\""; drop.ExecuteNonQuery(); }
                        using (var ren = conn.CreateCommand()) { ren.CommandText = "ALTER TABLE \"Paiements_V2\" RENAME TO \"Paiements\""; ren.ExecuteNonQuery(); }
                        log.LogInfo("[MIGRATION MANUELLE] Paiements.IdOperateur migré NOT NULL.");
                    }
                }
                catch (Exception ex) { log.LogError("[MIGRATION MANUELLE] Erreur Paiements : " + ex.Message, ex); }

                CreerIndex(conn, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Paiements_RecuNumero\" ON \"Paiements\" (\"RecuNumero\")");

                // Employes
                AjouterCol(conn, "Employes", "DerniereModificationMotDePasse", "TEXT NULL");

                // Depenses
                AjouterCol(conn, "Depenses", "Categorie",        "TEXT NOT NULL DEFAULT 'Divers'");
                AjouterCol(conn, "Depenses", "StatutValidation",  "TEXT NOT NULL DEFAULT 'Validee'");
                AjouterCol(conn, "Depenses", "IdOperateur",       "INTEGER NOT NULL DEFAULT 0");

                // MaterielsSupplements
                AjouterCol(conn, "MaterielsSupplements", "IdOperateur",  "INTEGER NOT NULL DEFAULT 0");
                AjouterCol(conn, "MaterielsSupplements", "NomOperateur", "TEXT NOT NULL DEFAULT ''");

                // Commandes
                AjouterCol(conn, "Commandes", "EstSupprimee",            "INTEGER NOT NULL DEFAULT 0");
                AjouterCol(conn, "Commandes", "MotifSuppression",        "TEXT NULL");
                AjouterCol(conn, "Commandes", "DateSuppression",         "TEXT NULL");
                AjouterCol(conn, "Commandes", "IdOperateurSuppression",  "INTEGER NULL");
                AjouterCol(conn, "Commandes", "NomOperateurSuppression", "TEXT NULL");
                AjouterCol(conn, "Commandes", "IdOperateurCreation",     "INTEGER NOT NULL DEFAULT 0");
                AjouterCol(conn, "Commandes", "NomOperateurCreation",    "TEXT NOT NULL DEFAULT ''");
                AjouterCol(conn, "Commandes", "DateCreation",            "TEXT NOT NULL DEFAULT '2000-01-01 00:00:00'");

                try
                {
                    using var upd = conn.CreateCommand();
                    upd.CommandText = "UPDATE \"Commandes\" SET \"DateCreation\" = \"DateDebut\" WHERE \"DateCreation\" = '2000-01-01 00:00:00'";
                    upd.ExecuteNonQuery();
                }
                catch { }

                CreerIndex(conn, "CREATE INDEX IF NOT EXISTS \"IX_Commandes_EstSupprimee\" ON \"Commandes\" (\"EstSupprimee\")");
                CreerIndex(conn, "CREATE INDEX IF NOT EXISTS \"IX_Commandes_IdOperateurCreation\" ON \"Commandes\" (\"IdOperateurCreation\")");
                CreerIndex(conn, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Clients_Telephone_Unique\" ON \"Clients\" (\"Telephone\") WHERE \"Telephone\" IS NOT NULL AND \"Telephone\" != ''");

                // Retours champs reprise
                AjouterCol(conn, "Retours", "CheminPhotoDefaut", "TEXT NULL");
                AjouterCol(conn, "Retours", "DateRdvReprise",    "TEXT NULL");
                AjouterCol(conn, "Retours", "HeureDebutReprise", "TEXT NULL");
                AjouterCol(conn, "Retours", "HeureFinReprise",   "TEXT NULL");
                AjouterCol(conn, "Retours", "IdCouturierReprise","INTEGER NULL");
                CreerIndex(conn, "CREATE INDEX IF NOT EXISTS \"IX_Retours_IdCouturierReprise\" ON \"Retours\" (\"IdCouturierReprise\")");

                // JournalAudit
                try
                {
                    using var jCmd = conn.CreateCommand();
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
                    CreerIndex(conn, "CREATE INDEX IF NOT EXISTS \"IX_JournalAudit_DateHeureUtc\" ON \"JournalAudit\" (\"DateHeureUtc\")");
                    CreerIndex(conn, "CREATE INDEX IF NOT EXISTS \"IX_JournalAudit_TypeAction\" ON \"JournalAudit\" (\"TypeAction\")");
                }
                catch (Exception ex) { log.LogError("[MIGRATION MANUELLE] Erreur JournalAudit : " + ex.Message, ex); }

                log.LogInfo("[MIGRATION MANUELLE] ConsolidationFinale appliquée.");
            }

            // Migration DateTerminee / IdOperateurTerminee sur PiecesCommande.
            // Appliquée séparément (hors ConsolidationFinale) pour rester idempotente.
            AppliquerSi("20261005000001_DateTermineePieceCommande", c =>
            {
                // 1. Ajouter les colonnes
                AjouterCol(c, "PiecesCommande", "DateTerminee",        "TEXT NULL");
                AjouterCol(c, "PiecesCommande", "IdOperateurTerminee", "INTEGER NULL");

                CreerIndex(c,
                    "CREATE INDEX IF NOT EXISTS \"IX_PiecesCommande_DateTerminee\" " +
                    "ON \"PiecesCommande\" (\"DateTerminee\")");

                // 2. Rattrapage : pour les pièces déjà Terminee/Livree sans DateTerminee.
                //
                // Décision documentée (à valider avec le propriétaire) :
                //   Source préférée : journal d'audit, entrée TypeAction='STATUT_PIECE_MODIFIE'
                //   avec ValeursApres contenant "Terminee" ou "Livree" pour cette pièce.
                //   Fallback : Commande.DateFin (date de RDV du jour de terminaison).
                //   Raison : avant cette migration, aucune date de terminaison réelle n'existait.
                //   La date de RDV est une approximation conservative : elle peut être antérieure
                //   à la date réelle. Elle ne sera jamais postérieure au travail réel car les
                //   couturiers terminent en général avant ou le jour du RDV.
                //   Impact : les pièces historiques pourraient être commissionnées dans une
                //   période légèrement différente de la réalité, mais c'est inévitable sans
                //   donnée réelle. Le propriétaire est informé de ce point.
                //
                //   Note : le journal d'audit stocke DateHeureUtc en TEXT ISO-8601.
                //   SQLite peut comparer des dates TEXT en ISO-8601 directement.
                try
                {
                    using var upd = c.CreateCommand();
                    upd.CommandText = @"
                        UPDATE ""PiecesCommande""
                        SET ""DateTerminee"" = COALESCE(
                            -- Priorité 1 : date du journal d'audit (la plus précise)
                            (
                                SELECT j.""DateHeureUtc""
                                FROM ""JournalAudit"" j
                                WHERE j.""Entite"" = 'PieceCommande'
                                  AND j.""IdEntite"" = ""PiecesCommande"".""IdPieceCommande""
                                  AND j.""TypeAction"" = 'STATUT_PIECE_MODIFIE'
                                  AND (
                                    j.""ValeursApres"" LIKE '%Terminee%'
                                    OR j.""ValeursApres"" LIKE '%Livree%'
                                  )
                                ORDER BY j.""DateHeureUtc"" ASC
                                LIMIT 1
                            ),
                            -- Priorité 2 : DateFin de la commande parente (approximation)
                            (
                                SELECT c.""DateFin""
                                FROM ""Commandes"" c
                                WHERE c.""IdCommande"" = ""PiecesCommande"".""IdCommande""
                            )
                        )
                        WHERE (""Statut"" = 'Terminee' OR ""Statut"" = 'Livree')
                          AND ""DateTerminee"" IS NULL;
                    ";
                    int nb = upd.ExecuteNonQuery();
                    log.LogInfo($"[MIGRATION T1] {nb} pièce(s) Terminee/Livree rétro-renseignées avec DateTerminee.");
                }
                catch (Exception ex)
                {
                    log.LogError($"[MIGRATION T1] Erreur rattrapage DateTerminee : {ex.Message}", ex);
                }
            });

            // ── Phase1_VerrouillageProgressif ─────────────────────────────────
            // Ajout de NbEchecConnexion et DateVerrouJusqua sur Employes pour
            // le verrouillage progressif persisté en base (voir AuthService).
            AppliquerSi("20261010000000_Phase1_VerrouillageProgressif", c =>
            {
                AjouterCol(c, "Employes", "NbEchecConnexion", "INTEGER NOT NULL DEFAULT 0");
                AjouterCol(c, "Employes", "DateVerrouJusqua", "TEXT NULL");
            });

            // ── Étape 2 — Verrouillage suppression / modification des commandes ──
            // EstAnnulee : annulation rapide par la Secrétaire (distincte d'une
            // suppression Boss). DateImpressionRecu : première impression du
            // reçu / ticket — verrouille le prix pour la Secrétaire.
            AppliquerSi("20261015000000_Etape2_VerrouillageCommandes", c =>
            {
                AjouterCol(c, "Commandes", "EstAnnulee",         "INTEGER NOT NULL DEFAULT 0");
                AjouterCol(c, "Commandes", "DateImpressionRecu", "TEXT NULL");
            });

            // ── Étape 4 — MesuresClient : fiche de mesures réutilisables ─────────
            AppliquerSi("20261015000000_AjoutMesuresClient", c =>
            {
                // 1. Créer la table
                using var createCmd = c.CreateCommand();
                createCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ""MesuresClient"" (
                        ""IdMesureClient""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""IdClient""                INTEGER NOT NULL,
                        ""TypeVetement""            TEXT    NOT NULL,
                        ""NomMesure""               TEXT    NOT NULL,
                        ""Valeur""                  TEXT    NOT NULL,
                        ""DateMiseAJour""            TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        ""IdOperateurMiseAJour""     INTEGER NOT NULL DEFAULT 0,
                        ""NomOperateurMiseAJour""    TEXT    NOT NULL DEFAULT '',
                        FOREIGN KEY (""IdClient"") REFERENCES ""Clients""(""IdClient"") ON DELETE CASCADE
                    )";
                createCmd.ExecuteNonQuery();

                // 2. Index unique (IdClient, TypeVetement, NomMesure)
                CreerIndex(c,
                    "CREATE UNIQUE INDEX IF NOT EXISTS " +
                    "\"IX_MesuresClient_IdClient_TypeVetement_NomMesure\" " +
                    "ON \"MesuresClient\" (\"IdClient\", \"TypeVetement\", \"NomMesure\")");

                // 3. Migration des données existantes :
                //    Pour chaque (client, type de vêtement, nom de mesure), prendre
                //    la valeur de la pièce la plus récente (par DateCreation de la commande).
                //    INSERT OR IGNORE → si une valeur existe déjà (relance), on la laisse.
                using var migCmd = c.CreateCommand();
                migCmd.CommandText = @"
                    INSERT OR IGNORE INTO ""MesuresClient""
                        (""IdClient"", ""TypeVetement"", ""NomMesure"", ""Valeur"",
                         ""DateMiseAJour"", ""IdOperateurMiseAJour"", ""NomOperateurMiseAJour"")
                    SELECT
                        cmd.""IdClient"",
                        p.""TypeVetement"",
                        m.""NomMesure"",
                        m.""Valeur"",
                        -- Prendre la date de la commande comme date de MAJ initiale
                        COALESCE(cmd.""DateCreation"", cmd.""DateDebut""),
                        COALESCE(cmd.""IdOperateurCreation"", 0),
                        COALESCE(cmd.""NomOperateurCreation"", 'Migration automatique')
                    FROM ""Mesures"" m
                    -- Joindre sur la pièce la plus récente pour ce (client, type)
                    INNER JOIN ""PiecesCommande"" p ON p.""IdPieceCommande"" = m.""IdPieceCommande""
                    INNER JOIN ""Commandes"" cmd ON cmd.""IdCommande"" = p.""IdCommande""
                    WHERE cmd.""EstSupprimee"" = 0
                      AND m.""IdPieceCommande"" IS NOT NULL
                      -- Sous-requête : pièce la plus récente pour ce (client, type, mesure)
                      AND p.""IdPieceCommande"" = (
                            SELECT p2.""IdPieceCommande""
                            FROM ""PiecesCommande"" p2
                            INNER JOIN ""Commandes"" c2 ON c2.""IdCommande"" = p2.""IdCommande""
                            INNER JOIN ""Mesures"" m2 ON m2.""IdPieceCommande"" = p2.""IdPieceCommande""
                            WHERE c2.""IdClient"" = cmd.""IdClient""
                              AND p2.""TypeVetement"" = p.""TypeVetement""
                              AND m2.""NomMesure"" = m.""NomMesure""
                              AND c2.""EstSupprimee"" = 0
                            ORDER BY COALESCE(c2.""DateCreation"", c2.""DateDebut"") DESC,
                                     p2.""IdPieceCommande"" DESC
                            LIMIT 1
                      )";
                int nb = migCmd.ExecuteNonQuery();
                log.LogInfo($"[MIGRATION MANUELLE] AjoutMesuresClient : {nb} mesure(s) migrées vers MesuresClient.");
            });

            // ── Étape 6 — NumeroRetour + RetourCompteur + renommage Pret→Termine ─
            AppliquerSi("20261015000001_Etape6_RetourNumeroStatut", c =>
            {
                // 1. Table RetourCompteur (compteur annuel)
                using var createComp = c.CreateCommand();
                createComp.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ""RetourCompteurs"" (
                        ""Annee""          INTEGER NOT NULL PRIMARY KEY,
                        ""DernierNumero""  INTEGER NOT NULL DEFAULT 0
                    )";
                createComp.ExecuteNonQuery();

                // 2. Colonne NumeroRetour sur Retours
                AjouterCol(c, "Retours", "NumeroRetour", "TEXT NOT NULL DEFAULT ''");

                // 3. Index UNIQUE sur NumeroRetour (ignore les vides — WHEREclause)
                CreerIndex(c,
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Retours_NumeroRetour\" " +
                    "ON \"Retours\" (\"NumeroRetour\") " +
                    "WHERE \"NumeroRetour\" != ''");

                // 4. Colonnes retour arrière de statut
                AjouterCol(c, "Retours", "MotifRetourStatut",         "TEXT NULL");
                AjouterCol(c, "Retours", "DateRetourStatut",          "TEXT NULL");
                AjouterCol(c, "Retours", "NomOperateurRetourStatut",  "TEXT NULL");

                // 5. Migration Pret → Termine sur les données existantes
                using var migStatut = c.CreateCommand();
                migStatut.CommandText = "UPDATE \"Retours\" SET \"Statut\" = 'Termine' WHERE \"Statut\" = 'Pret'";
                int nbStatuts = migStatut.ExecuteNonQuery();
                log.LogInfo($"[MIGRATION MANUELLE] Etape6 : {nbStatuts} retour(s) migrés Pret→Termine.");

                // 6. Attribution des NumeroRetour aux retours existants (sans numéro)
                //    Ordre : DateSignalement ASC, puis IdRetour ASC.
                //    On regroupe par année et incrémente le compteur.
                //    INSERT OR REPLACE dans RetourCompteurs pour chaque année touchée.
                using var selCmd = c.CreateCommand();
                selCmd.CommandText = @"
                    SELECT ""IdRetour"", ""DateSignalement""
                    FROM ""Retours""
                    WHERE ""NumeroRetour"" = '' OR ""NumeroRetour"" IS NULL
                    ORDER BY ""DateSignalement"" ASC, ""IdRetour"" ASC";

                var aNumeroter = new List<(int IdRetour, DateTime Date)>();
                using (var rdr = selCmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        int id = rdr.GetInt32(0);
                        DateTime dt = DateTime.Parse(rdr.GetString(1));
                        aNumeroter.Add((id, dt));
                    }
                }

                // Compteurs par année
                var compteurs = new System.Collections.Generic.Dictionary<int, int>();
                foreach (var (idRet, dtSig) in aNumeroter)
                {
                    int annee = dtSig.Year;
                    if (!compteurs.ContainsKey(annee))
                    {
                        // Lire le max existant depuis RetourCompteurs (en cas de relance partielle)
                        using var rdMax = c.CreateCommand();
                        rdMax.CommandText = $"SELECT \"DernierNumero\" FROM \"RetourCompteurs\" WHERE \"Annee\" = {annee}";
                        var existant = rdMax.ExecuteScalar();
                        compteurs[annee] = existant == null ? 0 : Convert.ToInt32(existant);
                    }
                    compteurs[annee]++;
                    string numero = $"RET-{annee}-{compteurs[annee]:D5}";

                    using var updNum = c.CreateCommand();
                    updNum.CommandText = $"UPDATE \"Retours\" SET \"NumeroRetour\" = '{numero}' WHERE \"IdRetour\" = {idRet}";
                    updNum.ExecuteNonQuery();
                }

                // Persister les compteurs
                foreach (var kvp in compteurs)
                {
                    using var upsComp = c.CreateCommand();
                    upsComp.CommandText =
                        $"INSERT OR REPLACE INTO \"RetourCompteurs\" (\"Annee\", \"DernierNumero\") " +
                        $"VALUES ({kvp.Key}, {kvp.Value})";
                    upsComp.ExecuteNonQuery();
                }

                log.LogInfo($"[MIGRATION MANUELLE] Etape6 : {aNumeroter.Count} retour(s) numérotés.");
            });

            AppliquerSi("20261016000000_AjoutBrouillonCommande", c =>
            {
                using var createBrouillon = c.CreateCommand();
                createBrouillon.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ""BrouillonsCommande"" (
                        ""IdBrouillon""    INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""IdEmploye""      INTEGER NOT NULL,
                        ""ContenuJson""    TEXT    NOT NULL,
                        ""DateMiseAJour""  TEXT    NOT NULL
                    )";
                createBrouillon.ExecuteNonQuery();
                CreerIndex(c,
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_BrouillonsCommande_IdEmploye\" " +
                    "ON \"BrouillonsCommande\" (\"IdEmploye\")");
            });

            AppliquerSi("20261016120000_AjoutSuiviAlerteRetard", c =>
            {
                using var createSuivi = c.CreateCommand();
                createSuivi.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ""SuivisAlerteRetard"" (
                        ""IdSuivi""                     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""TypeCible""                   TEXT    NOT NULL,
                        ""IdCible""                     INTEGER NOT NULL,
                        ""IdClient""                    INTEGER NOT NULL DEFAULT 0,
                        ""DateRdvTraite""               TEXT    NOT NULL,
                        ""NbPreventions""               INTEGER NOT NULL DEFAULT 0,
                        ""NbReports""                   INTEGER NOT NULL DEFAULT 0,
                        ""DateDernierMessage""          TEXT    NULL,
                        ""IdOperateurDernierMessage""   INTEGER NULL,
                        ""NomOperateurDernierMessage""  TEXT    NULL,
                        ""AncienneDateDernierReport""   TEXT    NULL,
                        ""NouvelleDateDernierReport""   TEXT    NULL,
                        ""DateDernierReport""           TEXT    NULL
                    )";
                createSuivi.ExecuteNonQuery();
                CreerIndex(c,
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SuivisAlerteRetard_TypeCible_IdCible\" " +
                    "ON \"SuivisAlerteRetard\" (\"TypeCible\", \"IdCible\")");
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
