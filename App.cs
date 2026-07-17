using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;
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

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite("Data Source=gestion_couture.db;Cache=Shared"),
                ServiceLifetime.Singleton);

            // Services
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IClientService, ClientService>();
            services.AddSingleton<ICommandeService, CommandeService>();
            services.AddSingleton<IPaiementService, PaiementService>();
            services.AddSingleton<ITypeVetementService, TypeVetementService>();
            services.AddSingleton<INavigationService, NavigationService>();

            Services = services.BuildServiceProvider();

            // ====== Créer la base ======
            try
            {
                var context = Services.GetRequiredService<ApplicationDbContext>();
                context.Database.EnsureCreated();

                // Compte Boss par défaut
                var boss = context.Employes.FirstOrDefault(e => e.Identifiant == "boss");
                if (boss == null)
                {
                    boss = new Employe
                    {
                        Nom = "Admin",
                        Prenom = "Boss",
                        Identifiant = "boss",
                        MotDePasse = HashMotDePasse("boss123"),
                        Role = "Boss",
                        Statut = "Actif"
                    };
                    context.Employes.Add(boss);
                }
                else
                {
                    boss.MotDePasse = HashMotDePasse("boss123");
                    boss.Statut = "Actif";
                }
                context.SaveChanges();

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

        private static string HashMotDePasse(string motDePasse)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(motDePasse);
                var hash = sha.ComputeHash(bytes);
                var builder = new System.Text.StringBuilder();
                foreach (byte b in hash)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
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