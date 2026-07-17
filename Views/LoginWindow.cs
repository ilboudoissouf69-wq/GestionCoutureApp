using System.Windows;
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
            string motDePasse = TxtPassword.Password.Trim();

            if (string.IsNullOrEmpty(identifiant) || string.IsNullOrEmpty(motDePasse))
            {
                TxtErreur.Text = "Veuillez remplir tous les champs.";
                TxtErreur.Visibility = Visibility.Visible;
                return;
            }

            var employe = _authService.Authentifier(identifiant, motDePasse);

            if (employe != null)
            {
                TxtErreur.Visibility = Visibility.Collapsed;

                try
                {
                    // Crée et montre la fenêtre principale AVANT de fermer le login
                    var mainWindow = new GestionCoutureApp.Views.MainWindow(employe);
                    mainWindow.Show();

                    // Ferme le login après que la MainWindow est affichée
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