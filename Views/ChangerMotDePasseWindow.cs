using System.ComponentModel;
using System.Windows;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Fenêtre modale et bloquante affichée quand Employe.DoitChangerMotDePasse
    /// est vrai (typiquement : le compte "boss" par défaut créé au premier
    /// démarrage — voir App.cs). Tant que le mot de passe n'a pas été changé
    /// avec succès, l'utilisateur ne peut qu'annuler et revenir à l'écran de
    /// connexion : il ne peut PAS accéder à MainWindow avec le mot de passe
    /// par défaut.
    /// </summary>
    public partial class ChangerMotDePasseWindow : Window
    {
        private readonly IAuthService _authService;
        private readonly Employe _employe;

        /// <summary>Vrai si le mot de passe a été changé avec succès avant fermeture.</summary>
        public bool ChangementReussi { get; private set; } = false;

        public ChangerMotDePasseWindow(Employe employe)
        {
            InitializeComponent();
            _employe = employe;
            _authService = App.Services.GetRequiredService<IAuthService>();

            // Empêche de fermer cette fenêtre via Alt+F4 / la croix système
            // tant que le changement n'a pas été effectué : sinon on se
            // retrouverait avec une fenêtre invisible mais l'application
            // toujours "ouverte" sur l'ancien LoginWindow fermé entre-temps.
            this.Closing += ChangerMotDePasseWindow_Closing;
        }

        private void ChangerMotDePasseWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!ChangementReussi)
            {
                // On autorise seulement la fermeture explicite via le bouton
                // "Se déconnecter" (qui appelle Application.Current.Shutdown()
                // avant de fermer cette fenêtre) ; toute autre tentative de
                // fermeture (croix, Alt+F4) est bloquée.
                if (!_fermetureAutorisee)
                    e.Cancel = true;
            }
        }

        private bool _fermetureAutorisee = false;

        private void BtnValider_Click(object sender, RoutedEventArgs e)
        {
            string ancien = TxtAncien.Password;
            string nouveau = TxtNouveau.Password;
            string confirmation = TxtConfirmation.Password;

            if (string.IsNullOrWhiteSpace(ancien) || string.IsNullOrWhiteSpace(nouveau)
                || string.IsNullOrWhiteSpace(confirmation))
            {
                AfficherErreur("Tous les champs sont obligatoires.");
                return;
            }

            if (nouveau != confirmation)
            {
                AfficherErreur("Le nouveau mot de passe et sa confirmation ne correspondent pas.");
                return;
            }

            if (nouveau == ancien)
            {
                AfficherErreur("Le nouveau mot de passe doit être différent de l'ancien.");
                return;
            }

            try
            {
                _authService.ChangerMotDePasse(_employe.IdEmploye, ancien, nouveau);
                ChangementReussi = true;
                _fermetureAutorisee = true;

                MessageBox.Show(
                    "Mot de passe changé avec succès. Veuillez vous reconnecter avec votre nouveau mot de passe.",
                    "Succès", MessageBoxButton.OK, MessageBoxImage.Information);

                this.Close();
            }
            catch (System.InvalidOperationException ex)
            {
                AfficherErreur(ex.Message);
            }
        }

        private void BtnDeconnexion_Click(object sender, RoutedEventArgs e)
        {
            _fermetureAutorisee = true;
            this.Close();
        }

        private void AfficherErreur(string message)
        {
            TxtErreur.Text = message;
            TxtErreur.Visibility = Visibility.Visible;
        }
    }
}
