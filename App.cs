using System.Windows;
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

            services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseSqlite("Data Source=gestion_couture.db;Cache=Shared"));

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
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Erreur lors de l'initialisation de la base :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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