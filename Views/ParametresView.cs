using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    public partial class ParametresView : Page
    {
        private readonly IParametresService _parametresService;

        public ParametresView()
        {
            var authService = App.Services.GetRequiredService<IAuthService>();
            if (authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            InitializeComponent();

            _parametresService = App.Services.GetRequiredService<IParametresService>();

            Loaded += async (s, e) => await ChargerParametres();
        }

        private async Task ChargerParametres()
        {
            TxtDelaiAlerte.Text = (await _parametresService.ObtenirDelaiAlerteRendezVousHeures()).ToString();
            TxtFrequenceSync.Text = (await _parametresService.ObtenirFrequenceSyncHeures()).ToString();
            TxtSalaireSecretaire.Text = (await _parametresService.ObtenirSalaireMensuelSecretaire()).ToString();
        }

        private async void BtnEnregistrer_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtDelaiAlerte.Text, out int delaiAlerte) || delaiAlerte <= 0)
            {
                TxtMessage.Text = "Saisissez un délai d'alerte valide (heures).";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            if (!int.TryParse(TxtFrequenceSync.Text, out int frequenceSync) || frequenceSync < 0)
            {
                TxtMessage.Text = "Saisissez une fréquence de synchronisation valide (0 ou plus).";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            if (!decimal.TryParse(TxtSalaireSecretaire.Text, out decimal salaire) || salaire < 0)
            {
                TxtMessage.Text = "Saisissez un salaire valide.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            try
            {
                await _parametresService.DefinirDelaiAlerteRendezVousHeures(delaiAlerte);
                await _parametresService.DefinirFrequenceSyncHeures(frequenceSync);
                await _parametresService.DefinirSalaireMensuelSecretaire(salaire);

                TxtMessage.Text = "Paramètres enregistrés.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                TxtMessage.Text = "Erreur : " + ex.Message;
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
    }
}