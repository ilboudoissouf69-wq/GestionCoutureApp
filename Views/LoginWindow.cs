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

                // ── Changement de mot de passe obligatoire ──────────────────
                // La colonne DoitChangerMotDePasse a été supprimée (migration
                // SupprimerDoitChangerMotDePasse), mais le risque reste entier :
                // un compte Boss dont le mot de passe n'a jamais été changé
                // depuis la création utilise encore "boss123", affiché en clair
                // dans la boîte de dialogue du premier démarrage.
                //
                // On détecte ce cas directement au login, sans colonne en base :
                // si le mot de passe qui vient de fonctionner correspond encore
                // au hash de "boss123", on impose le changement AVANT d'ouvrir
                // MainWindow. L'utilisateur ne peut pas contourner cette étape
                // (ChangerMotDePasseWindow bloque Alt+F4 et la croix système
                // tant que ChangementReussi est false — voir son code-behind).
                //
                // Ce contrôle est volontairement limité au rôle Boss : un
                // Couturier ou une Secrétaire ne dispose pas du mot de passe
                // par défaut connu publiquement.
                if (employe.Role == "Boss" &&
                    GestionCoutureApp.Helpers.PasswordHasher.Verifier("boss123", employe.MotDePasse))
                {
                    var changerMdp = new ChangerMotDePasseWindow(employe);
                    changerMdp.ShowDialog();

                    if (!changerMdp.ChangementReussi)
                    {
                        // L'utilisateur a cliqué "Se déconnecter" sans changer
                        // son mot de passe : on reste sur l'écran de connexion.
                        TxtErreur.Text = "Vous devez changer votre mot de passe avant de continuer.";
                        TxtErreur.Visibility = Visibility.Visible;
                        TxtPassword.Clear();
                        TxtIdentifiant.Focus();
                        return;
                    }

                    // Changement réussi : on recharge l'employé depuis la base
                    // pour que MainWindow dispose du hash à jour (et non de
                    // l'ancien "boss123"), puis on continue normalement.
                    employe = _authService.Authentifier(employe.Identifiant,
                        changerMdp.NouveauMotDePasse) ?? employe;
                }
                // ────────────────────────────────────────────────────────────

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