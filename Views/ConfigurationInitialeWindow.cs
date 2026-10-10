using System.ComponentModel;
using System.Windows;
using GestionCoutureApp.Models;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Fenêtre de configuration initiale : apparaît UNE SEULE FOIS, au premier
    /// lancement lorsque la base est vide. Crée le compte Boss avec un identifiant
    /// et un mot de passe choisis par le propriétaire.
    ///
    /// La fermeture via la croix ou Alt+F4 est bloquée tant que la configuration
    /// n'est pas terminée (ShutdownMode=OnExplicitShutdown dans App.xaml).
    /// </summary>
    public partial class ConfigurationInitialeWindow : Window
    {
        /// <summary>Vrai si la configuration a été complétée avec succès.</summary>
        public bool ConfigurationReussie { get; private set; } = false;

        /// <summary>Objet Employe créé, disponible après ConfigurationReussie = true.</summary>
        public Employe? EmployeCree { get; private set; }

        public ConfigurationInitialeWindow()
        {
            InitializeComponent();
            // Bloquer la fermeture via la croix : l'application ne peut pas démarrer
            // sans configuration initiale.
            this.Closing += ConfigurationInitialeWindow_Closing;
        }

        private bool _fermetureAutorisee = false;

        private void ConfigurationInitialeWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_fermetureAutorisee)
            {
                e.Cancel = true;
                // Informer l'utilisateur au lieu de disparaître silencieusement
                MessageBox.Show(
                    "Vous devez créer le compte administrateur avant de continuer.\n\n" +
                    "Cliquez sur « CRÉER LE COMPTE ADMINISTRATEUR » pour terminer la configuration.",
                    "Configuration requise",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BtnValider_Click(object sender, RoutedEventArgs e)
        {
            string identifiant  = TxtIdentifiant.Text.Trim();
            string motDePasse   = TxtMotDePasse.Password;
            string confirmation = TxtConfirmation.Password;

            // ── Validation identifiant ───────────────────────────────────
            if (string.IsNullOrWhiteSpace(identifiant))
            {
                AfficherErreur("L'identifiant est obligatoire.");
                return;
            }

            if (identifiant.Length < 3 || identifiant.Length > 50)
            {
                AfficherErreur("L'identifiant doit contenir entre 3 et 50 caractères.");
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(identifiant, @"^[a-zA-Z0-9_\-\.]+$"))
            {
                AfficherErreur("L'identifiant ne peut contenir que des lettres, chiffres, _ - .");
                return;
            }

            // ── Validation mot de passe ──────────────────────────────────
            if (string.IsNullOrEmpty(motDePasse) || string.IsNullOrEmpty(confirmation))
            {
                AfficherErreur("Tous les champs sont obligatoires.");
                return;
            }

            if (motDePasse != confirmation)
            {
                AfficherErreur("Le mot de passe et sa confirmation ne correspondent pas.");
                return;
            }

            try
            {
                AuthService.ValiderForceMotDePasse(motDePasse);
            }
            catch (InvalidOperationException ex)
            {
                AfficherErreur(ex.Message);
                return;
            }

            // ── Création du compte Boss ──────────────────────────────────
            EmployeCree = new Employe
            {
                Nom    = "Admin",
                Prenom = identifiant, // affiché dans les logs
                Identifiant = identifiant,
                MotDePasse  = PasswordHasher.Hasher(motDePasse),
                Role   = "Boss",
                Statut = "Actif",
                DerniereModificationMotDePasse = DateTime.Now,
            };

            ConfigurationReussie = true;
            _fermetureAutorisee  = true;
            this.Close();
        }

        private void AfficherErreur(string message)
        {
            TxtErreur.Text          = message;
            BorderErreur.Visibility = Visibility.Visible;
        }
    }
}
