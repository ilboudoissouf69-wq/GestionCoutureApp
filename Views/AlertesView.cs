using System.Windows;
using System.Windows.Controls;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class AlertesView : Page
    {
        private readonly IAlerteService      _alerteService;
        private readonly IWhatsAppService    _whatsAppService;
        private readonly IParametresService  _parametresService;

        private List<AlerteRendezVous> _alertesProduction = new();
        private List<AlerteRendezVous> _alertesRetrait    = new();

        public AlertesView()
        {
            InitializeComponent();

            _alerteService     = App.Services.GetRequiredService<IAlerteService>();
            _whatsAppService   = App.Services.GetRequiredService<IWhatsAppService>();
            _parametresService = App.Services.GetRequiredService<IParametresService>();

            Loaded += async (s, e) => await ChargerDonnees();
        }

        // ==================================================================
        // Chargement
        // ==================================================================
        private async Task ChargerDonnees()
        {
            try
            {
                _alertesProduction = await _alerteService.ObtenirAlertesActuelles();
                _alertesRetrait    = await _alerteService.ObtenirRendezVousSemaine();

                MettreAJourBadges();
                AfficherProduction();
                AfficherRetrait();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur de chargement : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================================================================
        // Badges KPI
        // ==================================================================
        private void MettreAJourBadges()
        {
            TxtNbProdUrgent.Text    = _alertesProduction.Count(a => a.EstUrgent).ToString();
            TxtNbProdSurveiller.Text = _alertesProduction.Count(a => !a.EstUrgent).ToString();
            TxtNbPrets.Text          = _alertesRetrait.Count(a => a.PiecePrete).ToString();
            TxtNbRdvAVenir.Text      = _alertesRetrait.Count(a => !a.PiecePrete).ToString();
        }

        // ==================================================================
        // Section 1 — Production (ItemsControl)
        // ==================================================================
        private void AfficherProduction()
        {
            if (_alertesProduction.Count == 0)
            {
                ListeProduction.Visibility   = Visibility.Collapsed;
                TxtVideProduction.Visibility = Visibility.Visible;
            }
            else
            {
                ListeProduction.Visibility   = Visibility.Visible;
                TxtVideProduction.Visibility = Visibility.Collapsed;
                ListeProduction.ItemsSource  = _alertesProduction
                    .OrderByDescending(a => a.EstUrgent)
                    .ThenBy(a => a.DateRendezVous)
                    .ToList();
            }
        }

        // ==================================================================
        // Section 2 — Retrait (ItemsControl)
        // ==================================================================
        private void AfficherRetrait()
        {
            if (_alertesRetrait.Count == 0)
            {
                ListeRetrait.Visibility   = Visibility.Collapsed;
                TxtVideRetrait.Visibility = Visibility.Visible;
            }
            else
            {
                ListeRetrait.Visibility   = Visibility.Visible;
                TxtVideRetrait.Visibility = Visibility.Collapsed;
                ListeRetrait.ItemsSource  = _alertesRetrait;
            }
        }

        // ==================================================================
        // Actualiser
        // ==================================================================
        private async void BtnActualiser_Click(object sender, RoutedEventArgs e)
        {
            try { await ChargerDonnees(); }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================================================================
        // WhatsApp — bouton dans ListeRetrait
        // ==================================================================
        private async void BtnContacterWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not AlerteRendezVous alerte) return;

            if (string.IsNullOrWhiteSpace(alerte.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone enregistré.",
                    "Numéro manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string modele    = await _parametresService.ObtenirMsgCommandePrete();
                string nomAtelier = await _parametresService.ObtenirNomAtelier();

                string message = modele
                    .Replace("{Nom}",      $"*{alerte.NomClient}*")
                    .Replace("{Commande}", alerte.IdCommande.ToString())
                    .Replace("{Pieces}",   alerte.TypeVetement)
                    .Replace("{Reste}",    "—")
                    .Replace("{Atelier}",  nomAtelier)
                    .Replace("{Date}",     alerte.DateRendezVous.ToString("dd/MM/yyyy"))
                    .Replace("{Heure}",    alerte.HeureRendezVous);

                _whatsAppService.OuvrirConversation(alerte.Telephone, message);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur WhatsApp : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
