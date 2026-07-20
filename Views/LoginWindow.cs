using System.Windows;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class LoginWindow : Window
    {
        private readonly IAuthService _authService;

        public LoginWindow()
        {
            InitializeComponent();
            _authService = App.Services.GetRequiredService<IAuthService>();
        }

        private void BtnConnexion_Click(object sender, RoutedEventArgs e)
        {
            string identifiant = TxtIdentifiant.Text.Trim();
            // CORRECTIF : un mot de passe ne doit JAMAIS être modifié (ex: .Trim())
            // avant vérification ou hachage. Tronquer les espaces en début/fin
            // change silencieusement la valeur effective du mot de passe : un
            // utilisateur ayant choisi un mot de passe avec un espace volontaire
            // ne pourrait plus jamais se reconnecter avec la valeur exacte qu'il
            // a définie, et la politique de mots de passe se retrouve affaiblie
            // sans qu'aucun message n'explique pourquoi.
            string motDePasse = TxtPassword.Password;

            if (string.IsNullOrEmpty(identifiant) || string.IsNullOrEmpty(motDePasse))
            {
                TxtErreur.Text = "Veuillez remplir tous les champs.";
                TxtErreur.Visibility = Visibility.Visible;
                return;
            }

            Employe? employe;
            try
            {
                employe = _authService.Authentifier(identifiant, motDePasse);
            }
            catch (CompteVerrouilleException ex)
            {
                TxtErreur.Text = $"Trop de tentatives échouées. Réessayez dans " +
                                  $"{Math.Ceiling(ex.TempsRestant.TotalSeconds)} secondes.";
                TxtErreur.Visibility = Visibility.Visible;
                return;
            }

            if (employe != null)
            {
                TxtErreur.Visibility = Visibility.Collapsed;

                try
                {
                    var mainWindow = new GestionCoutureApp.Views.MainWindow(employe);
                    mainWindow.Show();
                    this.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("ERREUR ouverture MainWindow :\n" + ex.ToString(),
                                    "Erreur critique", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                TxtErreur.Text = "Identifiant ou mot de passe incorrect.";
                TxtErreur.Visibility = Visibility.Visible;
            }
        }

        private void BtnFermer_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}