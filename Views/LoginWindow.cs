using System.Windows;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class LoginWindow : Window
    {
        private readonly IAuthService _authService;
        private readonly ILogService _logService;

        public LoginWindow()
        {
            InitializeComponent();
            _authService = App.Services.GetRequiredService<IAuthService>();
            _logService = App.Services.GetRequiredService<ILogService>();
        }

        private void BtnConnexion_Click(object sender, RoutedEventArgs e)
        {
            string identifiant = TxtIdentifiant.Text.Trim();
            // CORRECTIF : un mot de passe ne doit JAMAIS être modifié (ex: .Trim())
            // avant vérification ou hachage.
            string motDePasse = TxtPassword.Password;

            if (string.IsNullOrEmpty(identifiant) || string.IsNullOrEmpty(motDePasse))
            {
                AfficherErreur("Veuillez remplir tous les champs.");
                return;
            }

            Employe? employe;
            try
            {
                employe = _authService.Authentifier(identifiant, motDePasse);
            }
            catch (CompteVerrouilleException ex)
            {
                AfficherErreur($"Trop de tentatives échouées. Réessayez dans " +
                               $"{Math.Ceiling(ex.TempsRestant.TotalSeconds)} secondes.");
                return;
            }

            if (employe != null)
            {
                MasquerErreur();

                // Logger la connexion réussie
                _logService.LogAction("Connexion réussie", 
                    $"Utilisateur: {employe.Nom} {employe.Prenom} ({employe.Role})", 
                    employe.Nom);

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
                    AfficherErreur("Vous devez changer votre mot de passe avant de continuer.");
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

                // ✅ CORRECTIF AUDIT #11 : Rappel périodique changement mot de passe (90 jours)
                if (employe.Role == "Boss" && employe.DerniereModificationMotDePasse.HasValue)
                {
                    var joursDepuis = (DateTime.Now - employe.DerniereModificationMotDePasse.Value).TotalDays;
                    if (joursDepuis > 90)
                    {
                        var rappel = MessageBox.Show(
                            $"Votre mot de passe date de {(int)joursDepuis} jours.\n\n" +
                            "Pour votre sécurité, il est recommandé de le changer régulièrement " +
                            "(tous les 90 jours).\n\n" +
                            "Voulez-vous le changer maintenant ?",
                            "Rappel de sécurité",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (rappel == MessageBoxResult.Yes)
                        {
                            var changerMdp = new ChangerMotDePasseWindow(employe);
                            changerMdp.ShowDialog();

                            if (changerMdp.ChangementReussi)
                            {
                                // Recharger avec le nouveau mot de passe
                                employe = _authService.Authentifier(employe.Identifiant,
                                    changerMdp.NouveauMotDePasse) ?? employe;
                            }
                        }
                    }
                }

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
                // Logger la tentative de connexion échouée
                _logService.LogWarning($"Tentative de connexion échouée pour l'identifiant: {identifiant}");
                AfficherErreur("Identifiant ou mot de passe incorrect.");
            }
        }

        // ------------------------------------------------------------------
        // Helpers affichage erreur — synchronisent TxtErreur + BorderErreur
        // ------------------------------------------------------------------
        private void AfficherErreur(string message)
        {
            TxtErreur.Text          = message;
            TxtErreur.Visibility    = Visibility.Visible;
            BorderErreur.Visibility = Visibility.Visible;
        }

        private void MasquerErreur()
        {
            TxtErreur.Visibility    = Visibility.Collapsed;
            BorderErreur.Visibility = Visibility.Collapsed;
        }

        private void BtnFermer_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}