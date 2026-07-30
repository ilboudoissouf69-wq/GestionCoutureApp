using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    public partial class AlertesView : Page
    {
        private readonly IAlerteService _alerteService;
        private List<AlerteRendezVous> _alertesActuelles = new();
        private List<AlerteRendezVous> _tousRendezVous = new();
        // Empêche AppliquerFiltre de s'exécuter avant la fin du chargement initial
        private bool _chargementTermine = false;

        public AlertesView()
        {
            InitializeComponent();

            _alerteService = App.Services.GetRequiredService<IAlerteService>();

            Loaded += async (s, e) =>
            {
                try
                {
                    await ChargerDonnees();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur de chargement : " + ex.Message,
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }

        private async Task ChargerDonnees()
        {
            try
            {
                _chargementTermine = false;

                // Chargement séquentiel (pas de Task.WhenAll pour éviter
                // les conflits DbContext SQLite concurrents)
                _tousRendezVous = await _alerteService.ObtenirTousRendezVousAVenir();
                _alertesActuelles = await _alerteService.ObtenirAlertesActuelles();

                // Calculer les statistiques
                var maintenant = DateTime.Now;
                var aujourdhui = _tousRendezVous
                    .Count(a => a.DateRendezVous.Date == maintenant.Date);
                var finSemaine = maintenant
                    .AddDays(7 - (int)maintenant.DayOfWeek);
                var cetteSemaine = _tousRendezVous
                    .Count(a => a.DateRendezVous.Date <= finSemaine.Date);

                TxtNbAlertes.Text = _alertesActuelles.Count.ToString();
                TxtAujourdhui.Text = aujourdhui.ToString();
                TxtSemaine.Text = cetteSemaine.ToString();

                _chargementTermine = true;
                AppliquerFiltre();
            }
            catch (Exception ex)
            {
                TxtMessage.Text = "Erreur : " + ex.Message;
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void Filtre_Changed(object sender, RoutedEventArgs e)
        {
            // Ignoré tant que les données ne sont pas chargées
            if (!_chargementTermine) return;
            AppliquerFiltre();
        }

        private void AppliquerFiltre()
        {
            List<AlerteRendezVous> afficher;

            if (RbAlertes.IsChecked == true)
            {
                afficher = _alertesActuelles;
            }
            else if (RbUrgentes.IsChecked == true)
            {
                afficher = _tousRendezVous
                    .Where(a => a.EstUrgent)
                    .ToList();
            }
            else
            {
                afficher = _tousRendezVous;
            }

            GridAlertes.ItemsSource = afficher;

            if (afficher.Count == 0)
            {
                TxtMessage.Text = "Aucun rendez-vous trouve pour ce filtre.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                TxtMessage.Text = $"{afficher.Count} rendez-vous trouve(s).";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Green;
            }
        }

        private async void BtnActualiser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ChargerDonnees();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}