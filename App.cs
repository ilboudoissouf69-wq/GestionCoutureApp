using System.Windows; // Contient les bases de l'interface graphique WPF (comme Application et MessageBox)
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp
{
    public partial class App : Application
    {
        // "OnStartup" est la méthode déclenchée dès que l'application démarre
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); // Exécute le démarrage standard de WPF

            try // "try" veut dire : "Essaie de faire ce code, et s'il y a un bug, ne plante pas, va dans le bloc catch"
            {
                // "using (var context...)" crée une connexion temporaire à la BDD et la referme proprement dès qu'on a fini
                using (var context = new ApplicationDbContext())
                {
                    // LIGNE MAGIQUE : Elle vérifie si le fichier "couture.db" existe. 
                    // S'il n'existe pas, elle crée le fichier et toutes les tables automatiquement !
                    context.Database.EnsureCreated();

                    // "if (!context.Employes.Any())" : Si la table des employés est complètement vide...
                    if (!context.Employes.Any())
                    {
                        // ...alors on crée un compte "Boss" par défaut pour que tu ne restes pas bloqué dehors.
                        context.Employes.Add(new Employe
                        {
                            Nom = "Super",
                            Prenom = "Boss",
                            Identifiant = "admin",
                            MotDePasse = "admin123", // Tes identifiants de test
                            Role = "Boss",
                            Statut = "Actif"
                        });

                        // IMPORTANT : .Add() prépare le compte, mais c'est .SaveChanges() qui l'écrit physiquement dans le fichier couture.db
                        context.SaveChanges();
                    }
                }
            }
            catch (Exception ex) // Si un bug survient dans le bloc try, il est "attrapé" ici dans la variable "ex"
            {
                // On affiche une jolie boîte de dialogue avec le message exact de l'erreur pour pouvoir déboguer
                MessageBox.Show($"Erreur d'initialisation de la base de données : {ex.Message}",
                                "Erreur critique",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }
    }
}