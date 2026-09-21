using System.Windows;
using System.Windows.Controls;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class ClientsView : Page
    {
        private readonly IClientService _clientService;
        private readonly IWhatsAppService _whatsApp;
        private readonly ILanguageService _languageService;
        private readonly IEventAggregator _eventAggregator;
        private int _clientSelectionneId;
        
        // ✅ PAGINATION
        private const int PAGE_SIZE = 20;
        private int _currentPage = 1;
        private string _currentSearch = "";

        public ClientsView()
        {
            InitializeComponent();
            _clientService = App.Services.GetRequiredService<IClientService>();
            _whatsApp = App.Services.GetRequiredService<IWhatsAppService>();
            _languageService = App.Services.GetRequiredService<ILanguageService>();
            _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();
            
            // ✅ S'abonner aux changements de langue et de thème
            _eventAggregator.Subscribe(SettingsChangedType.Language, OnLanguageChanged);
            _eventAggregator.Subscribe(SettingsChangedType.AccentColor, OnThemeChanged);
            
            ChargerClientsPage();
            
            // Se désabonner à la fermeture
            Unloaded += (s, e) =>
            {
                _eventAggregator.Unsubscribe(SettingsChangedType.Language, OnLanguageChanged);
                _eventAggregator.Unsubscribe(SettingsChangedType.AccentColor, OnThemeChanged);
            };
        }
        
        // ------------------------------------------------------------------
        // Gestionnaire de changement de langue
        // ------------------------------------------------------------------
        private void OnLanguageChanged(SettingsChangedEvent evt)
        {
            Dispatcher.Invoke(() => UpdateTranslations());
        }
        
        // ------------------------------------------------------------------
        // Gestionnaire de changement de thème
        // ------------------------------------------------------------------
        private void OnThemeChanged(SettingsChangedEvent evt)
        {
            // Les couleurs utilisent DynamicResource, donc elles se mettent à jour automatiquement
        }
        
        // ------------------------------------------------------------------
        // Mettre à jour les traductions de ClientsView
        // ------------------------------------------------------------------
        private void UpdateTranslations()
        {
            // Pour l'instant, ClientsView n'a pas beaucoup de textes traduisibles
            // Les messages MessageBox restent en français pour l'instant
        }

        // ==================================================================
        // Chargement
        // ==================================================================
        private async Task ChargerClientsPage()
        {
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                GridClients.IsEnabled = false;
                
                PagedResult<Client> result;
                if (string.IsNullOrWhiteSpace(_currentSearch))
                {
                    result = await _clientService.ObtenirPageAsync(_currentPage, PAGE_SIZE);
                }
                else
                {
                    result = await _clientService.RechercherPageAsync(_currentSearch, _currentPage, PAGE_SIZE);
                }
                
                GridClients.ItemsSource = result.Items;
                
                // Mettre à jour les boutons de pagination
                BtnPagePrecedente.IsEnabled = result.HasPrevious;
                BtnPageSuivante.IsEnabled = result.HasNext;
                
                // Mettre à jour l'info de pagination
                int start = (result.Page - 1) * result.PageSize + 1;
                int end = Math.Min(result.Page * result.PageSize, result.TotalCount);
                TxtPaginationInfo.Text = $"{start}-{end} / {result.TotalCount} clients";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du chargement des clients : " + ex.Message, 
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                GridClients.IsEnabled = true;
            }
        }

        private void ChargerClients()
        {
            // Pour compatibilité avec le code existant
            _currentPage = 1;
            _currentSearch = "";
            _ = ChargerClientsPage();
        }

        private async void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentSearch = TxtRecherche.Text.Trim();
            _currentPage = 1;
            await ChargerClientsPage();
        }
        
        private async void BtnPagePrecedente_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await ChargerClientsPage();
            }
        }
        
        private async void BtnPageSuivante_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await ChargerClientsPage();
        }

        private void GridClients_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridClients.SelectedItem is Client client)
            {
                _clientSelectionneId = client.IdClient;
                TxtNom.Text       = client.Nom;
                TxtPrenom.Text    = client.Prenom;
                TxtTelephone.Text = client.Telephone;
            }
        }

        // ==================================================================
        // CRUD
        // ==================================================================
        private void BtnAjouter_Click(object sender, RoutedEventArgs e)
        {
            if (ChampsInvalides()) return;
            var client = new Client
            {
                Nom       = TxtNom.Text.Trim(),
                Prenom    = TxtPrenom.Text.Trim(),
                Telephone = TxtTelephone.Text.Trim()
            };
            _clientService.Ajouter(client);
            ChargerClients();
            ViderChamps();
            MessageBox.Show("Client ajouté avec succès !", "Succès",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnModifier_Click(object sender, RoutedEventArgs e)
        {
            if (_clientSelectionneId == 0)
            {
                MessageBox.Show("Sélectionnez un client dans le tableau.",
                    "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (ChampsInvalides()) return;

            var client = new Client
            {
                IdClient  = _clientSelectionneId,
                Nom       = TxtNom.Text.Trim(),
                Prenom    = TxtPrenom.Text.Trim(),
                Telephone = TxtTelephone.Text.Trim()
            };
            _clientService.Modifier(client);
            ChargerClients();
            ViderChamps();
            MessageBox.Show("Client modifié avec succès !", "Succès",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (_clientSelectionneId == 0)
            {
                MessageBox.Show("Sélectionnez un client dans le tableau.",
                    "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var r = MessageBox.Show("Supprimer ce client ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                _clientService.Supprimer(_clientSelectionneId);
                ChargerClients();
                ViderChamps();
                MessageBox.Show("Client supprimé.", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Suppression impossible",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnVider_Click(object sender, RoutedEventArgs e) => ViderChamps();

        // ==================================================================
        // WhatsApp — bouton dans la ligne du tableau
        // ==================================================================
        private void BtnWhatsAppTableau_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Client client) return;
            EnvoyerWhatsAppClient(client);
        }

        // ==================================================================
        // WhatsApp — bouton dans le panneau détail
        // ==================================================================
        private void BtnWhatsAppPanneau_Click(object sender, RoutedEventArgs e)
        {
            string tel = TxtTelephone.Text.Trim();
            if (string.IsNullOrEmpty(tel))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone.",
                    "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Construire un client temporaire depuis les champs saisis
            var client = new Client
            {
                IdClient  = _clientSelectionneId,
                Nom       = TxtNom.Text.Trim(),
                Prenom    = TxtPrenom.Text.Trim(),
                Telephone = tel
            };
            EnvoyerWhatsAppClient(client);
        }

        // ==================================================================
        // Logique partagée WhatsApp
        // ==================================================================
        private async void EnvoyerWhatsAppClient(Client client)
        {
            if (string.IsNullOrWhiteSpace(client.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone.",
                    "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await _whatsApp.ContacterClientAsync(client);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir WhatsApp :\n" + ex.Message,
                    "Erreur WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================================================================
        // Helpers
        // ==================================================================
        private bool ChampsInvalides()
        {
            if (string.IsNullOrWhiteSpace(TxtNom.Text) ||
                string.IsNullOrWhiteSpace(TxtPrenom.Text))
            {
                MessageBox.Show("Le nom et le prénom sont obligatoires.",
                    "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }
            return false;
        }

        private void ViderChamps()
        {
            _clientSelectionneId = 0;
            TxtNom.Text       = "";
            TxtPrenom.Text    = "";
            TxtTelephone.Text = "";
            GridClients.SelectedItem = null;
        }
    }
}
