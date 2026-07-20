using System.Windows;
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
                logging.SetMinimumLevel(LogLevel.Information);
            });

            // CORRECTIF : la base est désormais stockée dans %LOCALAPPDATA%
            // (voir Helpers/AppPaths.cs) au lieu d'un chemin relatif, qui
            // dépendait du répertoire de lancement et posait des problèmes
            // de droits d'écriture une fois l'app installée dans Program Files.
            services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseSqlite(AppPaths.ChaineConnexionSqlite));

            // Services
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IClientService, ClientService>();
            services.AddSingleton<ICommandeService, CommandeService>();
            services.AddSingleton<IPaiementService, PaiementService>();
            services.AddSingleton<ICommissionService, CommissionService>();
            services.AddSingleton<ITypeVetementService, TypeVetementService>();
            // Sauvegarde automatique
            services.AddSingleton<BackupService>();
            // NavigationService non enregistré : son constructeur exige un Frame,
            // qui n'existe qu'une fois MainWindow ouverte (pas au démarrage de l'app),
            // et n'est pas lui-même enregistré dans ce conteneur DI. Tel quel,
            // GetRequiredService<INavigationService>() planterait avec un message
            // DI peu clair. Ce service semble prévu pour une future vraie migration
            // MVVM (voir NavigationService.cs) — à enregistrer proprement (probablement
            // en résolvant/assignant le Frame après la construction de MainWindow)
            // le jour où il sera effectivement utilisé quelque part.
            // services.AddSingleton<INavigationService, NavigationService>();

            Services = services.BuildServiceProvider();

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
            MessageBox.Show(
                "Erreur inattendue :\n" + e.Exception.Message,
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    "Erreur critique :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}