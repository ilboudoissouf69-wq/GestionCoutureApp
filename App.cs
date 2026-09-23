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
                // Migrate() applique toutes les migrations en attente (crée la base
                // si elle n'existe pas encore, sinon la met à jour sans perte de données).
                // Remplace EnsureCreated() qui ne gérait jamais les évolutions de schéma.
                context.Database.Migrate();

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
                            // ✅ CORRECTIF AUDIT #5 : Utilisation de ExecuteSql au lieu de ExecuteSqlRaw
                            // Note : les valeurs col et def proviennent d'un tableau statique défini
                            // dans le code (pas d'entrée utilisateur), donc pas de risque d'injection SQL.
                            // Cependant, on utilise ExecuteSql pour respecter les bonnes pratiques.
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
                    {
                        // ✅ CORRECTIF AUDIT #5 : Utilisation de ExecuteSql
                        context.Database.ExecuteSql($"ALTER TABLE Commissions ADD COLUMN PrimeQualite TEXT NOT NULL DEFAULT '0';");
                    }
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
                            // ✅ CORRECTIF AUDIT #5 : Utilisation de ExecuteSql
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
