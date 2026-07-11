using System.Windows;
using GestionCoutureApp.Data;

namespace GestionCoutureApp.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            // Cette méthode obligatoire charge les éléments graphiques dessinés dans le fichier .xaml
            InitializeComponent();
        }

        // Cette méthode se déclenche quand l'utilisateur clique sur le bouton "Se connecter"
        private void BtnConnexion_Click(object sender, RoutedEventArgs e)
        {
            // 1. Récupération des textes saisis par l'utilisateur
            // ".Trim()" supprime les espaces vides accidentels au début ou à la fin du texte
            string identifiant = TxtIdentifiant.Text.Trim();
            string password = TxtPassword.Password;

            // 2. Vérification de sécurité : est-ce qu'un champ est vide ?
            // "||" signifie "OU"
            if (string.IsNullOrEmpty(identifiant) || string.IsNullOrEmpty(password))
            {
                AfficherErreur("Veuillez remplir tous les champs.");
                return; // Arrête immédiatement l'exécution de la méthode
            }

            // 3. Connexion à la base de données pour vérification
            using (var context = new ApplicationDbContext())
            {
                // On cherche s'il existe un employé avec cet identifiant ET ce mot de passe
                // "&&" signifie "ET"
                // "u => ..." est une expression Lambda qui applique la condition à chaque ligne de la table
                var employe = context.Employes.FirstOrDefault(u => u.Identifiant == identifiant && u.MotDePasse == password);

                // Si "employe" n'est pas nul, cela veut dire qu'on a trouvé une correspondance exacte
                if (employe != null)
                {
                    // Vérification facultative : le compte est-il actif ?
                    if (employe.Statut != "Actif")
                    {
                        AfficherErreur("Ce compte a été désactivé par l'administrateur.");
                        return;
                    }

                    // Authentification réussie !
                    // On crée la fenêtre principale de l'application
                    MainWindow mainWin = new MainWindow();
                    mainWin.Show(); // On l'affiche à l'écran

                    this.Close(); // On ferme l'écran de login actuel qui ne sert plus à rien
                }
                else
                {
                    // Si "employe" est nul, c'est que les identifiants saisis sont incorrects
                    AfficherErreur("Identifiant ou mot de passe incorrect.");
                }
            }
        }

        // Petite méthode pratique pour factoriser l'affichage des messages d'erreur en rouge sur l'interface
        private void AfficherErreur(string message)
        {
            LblErreur.Text = message; // Assigne le texte du message
            LblErreur.Visibility = Visibility.Visible; // Rend le composant visuellement visible à l'écran
        }
    }
}