using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using GestionCoutureApp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class ClientsView : Page
    {
        private readonly IClientService _clientService;
        private readonly IWhatsAppService _whatsApp;
        private readonly ILanguageService _languageService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IMesureClientService _mesureClientService;

        // ── Sélection ────────────────────────────────────────────────────
        private int _clientSelectionneId;

        // ── Pagination ───────────────────────────────────────────────────
        private const int PAGE_SIZE = 20;
        private int _currentPage = 1;
        private string _currentSearch = "";

        // ── Modale ───────────────────────────────────────────────────────
        // true  = on modifie le client dont l'id est _clientSelectionneId
        // false = on crée un nouveau client
        private bool _modeEditionModal = false;

        // Positionné à true dans les seuls blocs de succès de Ajouter/Modifier
        // pour que BtnSauvegarderClient_Click sache si la modale peut se fermer.
        private bool _operationReussie = false;

        // ─────────────────────────────────────────────────────────────────
        public ClientsView()
        {
            InitializeComponent();
            _clientService   = App.Services.GetRequiredService<IClientService>();
            _whatsApp        = App.Services.GetRequiredService<IWhatsAppService>();
            _languageService = App.Services.GetRequiredService<ILanguageService>();
            _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();
            _mesureClientService = App.Services.GetRequiredService<IMesureClientService>();

            // ✅ S'abonner aux changements de langue et de thème
            _eventAggregator.Subscribe(SettingsChangedType.Language, OnLanguageChanged);
            _eventAggregator.Subscribe(SettingsChangedType.AccentColor, OnThemeChanged);

            ChargerClientsPage();

            // Se désabonner à la fermeture de la page
            Unloaded += (s, e) =>
            {
                _eventAggregator.Unsubscribe(SettingsChangedType.Language, OnLanguageChanged);
                _eventAggregator.Unsubscribe(SettingsChangedType.AccentColor, OnThemeChanged);
            };
        }

        // ==================================================================
        // Gestionnaires langue / thème
        // ==================================================================
        private void OnLanguageChanged(SettingsChangedEvent evt)
            => Dispatcher.Invoke(() => UpdateTranslations());

        private void OnThemeChanged(SettingsChangedEvent evt)
        {
            // Les couleurs de la charte globale utilisent DynamicResource.
            // Les couleurs locales Cli* sont fixes dans Page.Resources.
        }

        private void UpdateTranslations()
        {
            // ClientsView n'a pas encore de textes traduits dynamiquement.
        }

        // ==================================================================
        // Touche Échap : ferme la modale si elle est ouverte
        // ==================================================================
        private void Page_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && ModalClient.Visibility == Visibility.Visible)
            {
                FermerModal();
                e.Handled = true;
            }
        }

        // ==================================================================
        // Enter dans un champ de la modale → Enregistrer
        // ==================================================================
        private void TxtChamp_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnSauvegarderClient_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // ==================================================================
        // Chargement paginé
        // ==================================================================
        private async Task ChargerClientsPage()
        {
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                GridClients.IsEnabled = false;

                PagedResult<Client> result;
                if (string.IsNullOrWhiteSpace(_currentSearch))
                    result = await _clientService.ObtenirPageAsync(_currentPage, PAGE_SIZE);
                else
                    result = await _clientService.RechercherPageAsync(_currentSearch, _currentPage, PAGE_SIZE);

                // ── Enrichissement NbCommandes / DerniereCommande (sans N+1) ──
                if (result.Items.Count > 0)
                {
                    var ids = result.Items.Select(c => c.IdClient);
                    var stats = _clientService.ObtenirStatistiquesCommandes(ids);
                    foreach (var client in result.Items)
                    {
                        if (stats.TryGetValue(client.IdClient, out var s))
                        {
                            client.NbCommandes       = s.NbCommandes;
                            client.DerniereCommande  = s.DerniereCommande;
                        }
                        else
                        {
                            client.NbCommandes      = 0;
                            client.DerniereCommande = null;
                        }
                    }
                }

                GridClients.ItemsSource = result.Items;

                // ── Contrôle de l'état vide ──
                bool vide = result.Items.Count == 0;
                GridClients.Visibility    = vide ? Visibility.Collapsed : Visibility.Visible;
                PanelEtatVide.Visibility  = vide ? Visibility.Visible   : Visibility.Collapsed;
                if (vide)
                    TxtEtatVide.Text = string.IsNullOrWhiteSpace(_currentSearch)
                        ? "Aucun client enregistré"
                        : $"Aucun résultat pour « {_currentSearch} »";

                // ── Pagination ──
                BtnPagePrecedente.IsEnabled = result.HasPrevious;
                BtnPageSuivante.IsEnabled   = result.HasNext;
                int start = result.TotalCount == 0 ? 0 : (result.Page - 1) * result.PageSize + 1;
                int end   = Math.Min(result.Page * result.PageSize, result.TotalCount);
                TxtPaginationInfo.Text = result.TotalCount == 0
                    ? "Aucun client"
                    : $"{start}–{end} / {result.TotalCount} client{(result.TotalCount > 1 ? "s" : "")}";

                // ── Compteur en-tête ──
                TxtNbClients.Text = result.TotalCount == 0
                    ? "Aucun client"
                    : $"{result.TotalCount} client{(result.TotalCount > 1 ? "s" : "")} enregistré{(result.TotalCount > 1 ? "s" : "")}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du chargement des clients : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                GridClients.IsEnabled       = true;
            }
        }

        // Réinitialise la pagination et recharge depuis la page 1
        private void ChargerClients()
        {
            _currentPage   = 1;
            _currentSearch = "";
            _ = ChargerClientsPage();
        }

        // ==================================================================
        // Barre de recherche — placeholder masqué dès qu'il y a du texte
        // ==================================================================
        private async void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Masquer / afficher le placeholder
            TxtRecherchePlaceholder.Visibility =
                string.IsNullOrEmpty(TxtRecherche.Text) ? Visibility.Visible : Visibility.Collapsed;

            _currentSearch = TxtRecherche.Text.Trim();
            _currentPage   = 1;
            await ChargerClientsPage();
        }

        // ==================================================================
        // Pagination
        // ==================================================================
        private async void BtnPagePrecedente_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1) { _currentPage--; await ChargerClientsPage(); }
        }

        private async void BtnPageSuivante_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await ChargerClientsPage();
        }

        // ==================================================================
        // Sélection dans le DataGrid
        // ==================================================================
        private void GridClients_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridClients.SelectedItem is Client client)
            {
                // ── Alimentation des champs (logique existante) ──
                _clientSelectionneId = client.IdClient;
                TxtNom.Text          = client.Nom;
                TxtPrenom.Text       = client.Prenom;
                TxtTelephone.Text    = client.Telephone;

                // ── Alimentation de la fiche détaillée ──
                PanelAucuneSelection.Visibility = Visibility.Collapsed;
                PanelFicheDetaillee.Visibility  = Visibility.Visible;

                TxtFicheNomComplet.Text = client.NomComplet;
                TxtFicheIdClient.Text   = $"Client #{client.IdClient}";
                TxtFicheTelephone.Text  = string.IsNullOrWhiteSpace(client.Telephone)
                    ? "—"
                    : client.Telephone;

                // Chargement asynchrone de l'historique + mesures
                _ = ChargerFicheAsync(client.IdClient);
            }
            else
            {
                PanelFicheDetaillee.Visibility  = Visibility.Collapsed;
                PanelAucuneSelection.Visibility = Visibility.Visible;
            }
        }

        // ==================================================================
        // Chargement de la fiche détaillée (historique + mesures)
        // ==================================================================
        private async Task ChargerFicheAsync(int idClient)
        {
            // ── Onglet Historique ─────────────────────────────────────────
            var historique = _clientService.ObtenirHistoriqueCommandes(idClient, 20);

            if (historique.Count == 0)
            {
                ItemsHistoriqueCommandes.ItemsSource = null;
                TxtAucuneCommande.Visibility         = Visibility.Visible;
            }
            else
            {
                TxtAucuneCommande.Visibility = Visibility.Collapsed;
                ItemsHistoriqueCommandes.ItemsSource = historique.Select(h => new
                {
                    TitreCommande      = $"CMD #{h.IdCommande}",
                    StatutAffiche      = h.StatutAffiche,
                    ResumePieces       = $"{h.ResumePieces} — début {h.DateDebut:dd/MM/yyyy}",
                    ResteAPayerAffiche = h.ResteAPayer > 0
                        ? $"Reste : {h.ResteAPayer:N0} FCFA"
                        : "",
                    ResteAPayerVisible = h.ResteAPayer > 0
                        ? Visibility.Visible
                        : Visibility.Collapsed
                }).ToList();
            }

            // ── Onglet Mesures ────────────────────────────────────────────
            ChargerOngletMesures(idClient);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Charge (ou recharge) l'onglet Mesures pour le client donné.
        /// Génère dynamiquement un groupe par type de vêtement avec des TextBox éditables.
        /// </summary>
        private void ChargerOngletMesures(int idClient)
        {
            PanelGroupesMesures.Children.Clear();
            BtnSauvegarderMesures.Visibility = Visibility.Collapsed;

            List<FicheMesuresParType> groupes;
            try { groupes = _mesureClientService.ObtenirFicheMesures(idClient); }
            catch { groupes = new List<FicheMesuresParType>(); }

            if (groupes.Count == 0)
            {
                TxtAucuneMesureFiche.Visibility = Visibility.Visible;
                return;
            }

            TxtAucuneMesureFiche.Visibility = Visibility.Collapsed;

            foreach (var groupe in groupes)
            {
                // ── En-tête du groupe ──────────────────────────────────────
                var headerBorder = new Border
                {
                    Background      = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)), // ambre clair
                    CornerRadius    = new CornerRadius(8, 8, 0, 0),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(0xFC, 0xD3, 0x4D)),
                    BorderThickness = new Thickness(1, 1, 1, 0),
                    Padding         = new Thickness(10, 6, 10, 6),
                    Margin          = new Thickness(0, 8, 0, 0)
                };
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var lblType = new TextBlock
                {
                    Text       = groupe.TypeVetement,
                    FontSize   = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lblType, 0);

                var lblDate = new TextBlock
                {
                    Text       = groupe.LabelDate,
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x5A, 0x09)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin     = new Thickness(8, 0, 0, 0)
                };
                if (!string.IsNullOrEmpty(groupe.NomOperateurDerniereMaj))
                    lblDate.ToolTip = $"Mis à jour par : {groupe.NomOperateurDerniereMaj}";
                Grid.SetColumn(lblDate, 1);

                headerGrid.Children.Add(lblType);
                headerGrid.Children.Add(lblDate);
                headerBorder.Child = headerGrid;

                // ── Corps : une ligne par mesure ──────────────────────────
                var bodyBorder = new Border
                {
                    CornerRadius    = new CornerRadius(0, 0, 8, 8),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(0xFC, 0xD3, 0x4D)),
                    BorderThickness = new Thickness(1),
                    Padding         = new Thickness(10, 8, 10, 8),
                    Margin          = new Thickness(0, 0, 0, 0)
                };
                var bodyPanel = new StackPanel();

                foreach (var mesure in groupe.Mesures)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var lblNom = new TextBlock
                    {
                        Text              = mesure.NomMesure,
                        FontSize          = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground        = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69))
                    };
                    Grid.SetColumn(lblNom, 0);

                    var txtVal = new TextBox
                    {
                        Text              = mesure.Valeur,
                        FontSize          = 11,
                        Height            = 28,
                        Padding           = new Thickness(6, 0, 6, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        BorderBrush       = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                        BorderThickness   = new Thickness(1),
                        // Tag : encode "idClient|typeVetement|nomMesure" pour SaveMesures
                        Tag               = $"{idClient}|{groupe.TypeVetement}|{mesure.NomMesure}"
                    };
                    // Afficher le bouton Enregistrer dès qu'une valeur change
                    txtVal.TextChanged += (s, e) =>
                        BtnSauvegarderMesures.Visibility = Visibility.Visible;

                    Grid.SetColumn(txtVal, 1);

                    row.Children.Add(lblNom);
                    row.Children.Add(txtVal);
                    bodyPanel.Children.Add(row);
                }

                bodyBorder.Child = bodyPanel;
                PanelGroupesMesures.Children.Add(headerBorder);
                PanelGroupesMesures.Children.Add(bodyBorder);
            }
        }

        // ==================================================================
        // Sauvegarde des mesures éditées depuis la fiche client
        // ==================================================================
        private void BtnSauvegarderMesures_Click(object sender, RoutedEventArgs e)
        {
            if (_clientSelectionneId == 0) return;

            var authService = App.Services.GetRequiredService<IAuthService>();
            var op = authService.UtilisateurConnecte;
            int idOp     = op?.IdEmploye ?? 0;
            string nomOp = op != null ? $"{op.Prenom} {op.Nom}".Trim() : "Inconnu";

            // Collecter toutes les TextBox du panel et regrouper par type
            var parType = new Dictionary<string, List<(string Nom, string Valeur)>>();

            foreach (var child in PanelGroupesMesures.Children)
            {
                // On cherche les bodyBorder (ceux qui ont un StackPanel)
                if (child is not Border bodyBorder) continue;
                if (bodyBorder.Child is not StackPanel bodyPanel) continue;

                foreach (var rowEl in bodyPanel.Children)
                {
                    if (rowEl is not Grid row) continue;
                    foreach (UIElement el in row.Children)
                    {
                        if (el is not TextBox txt) continue;
                        // Tag format : "idClient|TypeVetement|NomMesure"
                        var parts = (txt.Tag?.ToString() ?? "").Split('|');
                        if (parts.Length != 3) continue;
                        string typeVet = parts[1];
                        string nomMes  = parts[2];
                        string valeur  = txt.Text.Trim().Replace(" cm", "").Replace("cm", "").Trim();
                        if (string.IsNullOrEmpty(valeur)) continue;

                        if (!parType.ContainsKey(typeVet))
                            parType[typeVet] = new List<(string, string)>();
                        parType[typeVet].Add((nomMes, valeur));
                    }
                }
            }

            if (parType.Count == 0)
            {
                MessageBox.Show("Aucune mesure à enregistrer.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                foreach (var (type, mesures) in parType)
                    _mesureClientService.UpsertMesures(_clientSelectionneId, type, mesures, idOp, nomOp);

                BtnSauvegarderMesures.Visibility = Visibility.Collapsed;
                // Recharger pour mettre à jour les labels de date
                ChargerOngletMesures(_clientSelectionneId);
                MessageBox.Show("Mesures enregistrées.", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'enregistrement :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================================================================
        // CRUD — logique existante INCHANGÉE
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

            try
            {
                _clientService.Ajouter(client);
                _operationReussie = true;
                ChargerClients();
                ViderChamps();
                MessageBox.Show("Client ajouté avec succès !", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (DuplicatClientException ex)
            {
                // ── Doublon détecté : proposer la fiche existante ────────
                var existant = ex.ClientExistant;
                string detail = $"Nom  : {existant.Nom} {existant.Prenom}\n" +
                                $"Tél  : {(string.IsNullOrWhiteSpace(existant.Telephone) ? "—" : existant.Telephone)}\n" +
                                $"Id   : #{existant.IdClient}";

                var choix = MessageBox.Show(
                    $"Ce client existe peut-être déjà :\n\n{detail}\n\n" +
                    "Voulez-vous sélectionner cette fiche existante ?\n\n" +
                    "• Oui → sélectionner la fiche existante\n" +
                    "• Non → créer quand même (si c'est une personne différente)",
                    "Doublon possible",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (choix == MessageBoxResult.Yes)
                {
                    // Passer en mode édition dans la modale avec la fiche existante
                    TxtNom.Text          = existant.Nom;
                    TxtPrenom.Text       = existant.Prenom;
                    TxtTelephone.Text    = existant.Telephone;
                    _clientSelectionneId = existant.IdClient;
                    _modeEditionModal    = true;
                    TxtModalTitre.Text   = $"Modifier le client — {existant.Prenom} {existant.Nom}";
                    ChargerClients();
                    // La modale reste ouverte
                }
                else if (choix == MessageBoxResult.No)
                {
                    // Forcer la création malgré le doublon détecté
                    try
                    {
                        using var ctx = App.Services
                            .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Data.ApplicationDbContext>>()
                            .CreateDbContext();
                        var ctxValidation = new System.ComponentModel.DataAnnotations.ValidationContext(client);
                        var errors = new System.Collections.Generic.List<System.ComponentModel.DataAnnotations.ValidationResult>();
                        if (!System.ComponentModel.DataAnnotations.Validator
                            .TryValidateObject(client, ctxValidation, errors, validateAllProperties: true))
                        {
                            MessageBox.Show(string.Join("\n", errors.Select(err => err.ErrorMessage)),
                                "Données invalides", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        ctx.Clients.Add(client);
                        ctx.SaveChanges();
                        _operationReussie = true;
                        ChargerClients();
                        ViderChamps();
                        MessageBox.Show("Client créé (doublon confirmé par l'opérateur).",
                            "Créé", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (DuplicatClientException dupEx)
                    {
                        var existant2 = dupEx.ClientExistant;
                        MessageBox.Show(
                            $"Impossible de créer ce client : le numéro de téléphone " +
                            $"({client.Telephone}) est déjà utilisé par " +
                            $"{existant2.Prenom} {existant2.Nom} (#{existant2.IdClient}).\n\n" +
                            "Corrigez le numéro ou sélectionnez la fiche existante.",
                            "Téléphone déjà utilisé",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (Exception innerEx)
                    {
                        var logService = App.Services.GetService<ILogService>();
                        logService?.LogError("Erreur lors de la création forcée d'un client", innerEx);
                        MessageBox.Show(
                            "Une erreur technique est survenue lors de la création.\n\n" +
                            "Détail : " + (innerEx.InnerException?.Message ?? innerEx.Message),
                            "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                // Cancel → ne rien faire, la modale reste ouverte
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Données invalides",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
            _operationReussie = true;
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
        // ENVELOPPES MODALE — réutilisent la logique existante
        // ==================================================================

        /// <summary>
        /// Bouton « + Nouveau Client » : ouvre la modale en mode Ajout.
        /// </summary>
        private void BtnOuvrirNouveauClient_Click(object sender, RoutedEventArgs e)
        {
            _modeEditionModal  = false;
            TxtModalTitre.Text = "Ajouter un Nouveau Client";
            ViderChamps();
            OuvrirModal();
            TxtPrenom.Focus();
        }

        /// <summary>
        /// Bouton ✏️ dans la ligne du DataGrid : ouvre la modale en mode Édition.
        /// </summary>
        private void BtnModifierClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Client client) return;

            _clientSelectionneId = client.IdClient;
            TxtNom.Text          = client.Nom;
            TxtPrenom.Text       = client.Prenom;
            TxtTelephone.Text    = client.Telephone;
            _modeEditionModal    = true;
            TxtModalTitre.Text   = $"Modifier le client — {client.Prenom} {client.Nom}";

            OuvrirModal();
            TxtPrenom.Focus();
        }

        /// <summary>
        /// Bouton 🗑️ dans la ligne du DataGrid : positionne l'id puis appelle la suppression.
        /// </summary>
        private void BtnSupprimerClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Client client) return;
            _clientSelectionneId = client.IdClient;
            BtnSupprimer_Click(sender, e);
        }

        /// <summary>
        /// Bouton « Enregistrer » de la modale : délègue à Ajouter ou Modifier existant.
        /// La modale ne se ferme QUE si l'opération a réussi.
        /// </summary>
        private void BtnSauvegarderClient_Click(object sender, RoutedEventArgs e)
        {
            _operationReussie = false;

            if (_modeEditionModal)
                BtnModifier_Click(sender, e);
            else
                BtnAjouter_Click(sender, e);

            if (_operationReussie)
                FermerModal();
        }

        /// <summary>
        /// Bouton ✕ / Annuler de la modale.
        /// </summary>
        private void BtnFermerModal_Click(object sender, RoutedEventArgs e)
            => FermerModal();

        /// <summary>
        /// WhatsApp depuis la fiche droite (client sélectionné dans le tableau).
        /// </summary>
        private void BtnFicheWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (_clientSelectionneId == 0) return;

            var client = new Client
            {
                IdClient  = _clientSelectionneId,
                Nom       = TxtFicheNomComplet.Text,
                Telephone = TxtFicheTelephone.Text == "—" ? "" : TxtFicheTelephone.Text
            };

            if (string.IsNullOrWhiteSpace(client.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone.",
                    "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            EnvoyerWhatsAppClient(client);
        }

        // ==================================================================
        // WhatsApp — bouton dans la ligne du tableau (logique existante)
        // ==================================================================
        private void BtnWhatsAppTableau_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Client client) return;
            EnvoyerWhatsAppClient(client);
        }

        // ==================================================================
        // WhatsApp — bouton dans le panneau (modale) — logique existante
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
        // Logique partagée WhatsApp (inchangée)
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

            // ✅ Validation de sécurité pour le nom
            if (!ValidationHelper.EstTexteSecurise(TxtNom.Text.Trim(), out string erreurNom))
            {
                MessageBox.Show($"Nom invalide : {erreurNom}",
                    "Erreur de validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }

            // ✅ Validation de sécurité pour le prénom
            if (!ValidationHelper.EstTexteSecurise(TxtPrenom.Text.Trim(), out string erreurPrenom))
            {
                MessageBox.Show($"Prénom invalide : {erreurPrenom}",
                    "Erreur de validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }

            return false;
        }

        private void ViderChamps()
        {
            _clientSelectionneId     = 0;
            TxtNom.Text              = "";
            TxtPrenom.Text           = "";
            TxtTelephone.Text        = "";
            GridClients.SelectedItem = null;
        }

        // ── Gestion de la modale ─────────────────────────────────────────

        private void OuvrirModal()
            => ModalClient.Visibility = Visibility.Visible;

        private void FermerModal()
        {
            ModalClient.Visibility = Visibility.Collapsed;
            if (!_modeEditionModal)
                ViderChamps();
        }

        // ==================================================================
        // Validation sécurisée des champs texte (gestionnaires existants)
        // ==================================================================

        // ✅ Validation sécurisée pour le nom du client
        private void TxtNom_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try { ValidationHelper.TextBox_PreviewTextInputTexteSecurise(sender, e); }
            catch { e.Handled = true; }
        }

        // ✅ Validation sécurisée pour le prénom du client
        private void TxtPrenom_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try { ValidationHelper.TextBox_PreviewTextInputTexteSecurise(sender, e); }
            catch { e.Handled = true; }
        }
    }
}
