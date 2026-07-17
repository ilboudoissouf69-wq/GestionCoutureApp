using System.Windows;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.ViewModels
{
    /// <summary>
    /// ViewModel de la page de connexion.
    /// Toute la logique est ici (rien dans le code-behind XAML).
    /// </summary>
    public class LoginViewModel : ViewModelBase
    {
        private readonly IAuthService _authService;

        // --- Propriétés liées à l'interface ---
        private string _identifiant = string.Empty;
        public string Identifiant
        {
            get => _identifiant;
            set
            {
                SetProperty(ref _identifiant, value);
                // Notifie que CanExecute a changé pour activer/désactiver le bouton
                ((RelayCommand)ConnexionCommand).RaiseCanExecuteChanged();
            }
        }

        private string _motDePasse = string.Empty;
        public string MotDePasse
        {
            get => _motDePasse;
            set
            {
                SetProperty(ref _motDePasse, value);
                ((RelayCommand)ConnexionCommand).RaiseCanExecuteChanged();
            }
        }

        // --- Commande bouton "Se connecter" ---
        public RelayCommand ConnexionCommand { get; }

        public LoginViewModel(IAuthService authService)
        {
            _authService = authService;
            // Le bouton est grisé tant que les 2 champs sont vides
            ConnexionCommand = new RelayCommand(ExecuterConnexion, PeutConnexion);
        }

        // Condition pour que le bouton soit cliquable
        private bool PeutConnexion(object? param)
        {
            return !string.IsNullOrWhiteSpace(Identifiant) && !string.IsNullOrWhiteSpace(MotDePasse);
        }

        private void ExecuterConnexion(object? param)
        {
            var employe = _authService.Authentifier(Identifiant, MotDePasse);

            if (employe == null)
            {
                MessageErreur = "Identifiant ou mot de passe incorrect.";
                return;
            }

            MessageErreur = string.Empty;

            // Ouvre la fenêtre principale et ferme le login
            var mainWindow = new GestionCoutureApp.Views.MainWindow(employe);
            mainWindow.Show();
            Application.Current.Windows[0]?.Close();
        }
    }
}