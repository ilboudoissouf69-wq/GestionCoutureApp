using System.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class CommandesView : Page
    {
        private readonly ICommandeService _commandeService;
        private readonly IClientService _clientService;
        private readonly ApplicationDbContext _context;
        private readonly IMaterielService _materielService;
        private readonly IWhatsAppService _whatsApp;
        private readonly ILanguageService _languageService;
        private readonly IEventAggregator _eventAggregator;
        private int _commandeSelectionneeId;
        private int? _pieceSelectionneeId; // null = aucune pièce sélectionnée
        // CORRECTIF (audit) : le motif saisi lors de l'exception Boss (ajout de
        // pièce après encaissement) doit survivre jusqu'à BtnSauvegarderPiece_Click,
        // qui est le seul endroit où AjouterPiece() est réellement appelé. Sans ce
        // champ, le motif capturé dans BtnAjouterPiece_Click se perdait et
        // AjouterPiece() finissait toujours par rejeter l'enregistrement.
        private string? _motifExceptionAjoutPiece;
        private decimal _prixBaseActuel;
        private List<TypeVetement> _typesVetement = new(); // ✅ FIX : Initialisation par défaut pour éviter null
        private bool _chargementEnCours = false;
        private string _cheminPhotoTemporaire = string.Empty;
        private string _roleUtilisateur;

        // Pièces chargées pour la commande sélectionnée
        private List<PieceCommande> _piecesCommande = new();

        // Buffer temporaire des matériaux saisis AVANT que la pièce soit sauvegardée en base
        // (mode création uniquement). Vidé et persisté lors de BtnSauvegarderPiece_Click.
        private readonly List<MaterielSupplement> _materiauxTemporaires = new();

        // Date de RDV par défaut calculée (maintenant + 24h, règle 08h-22h)
        private DateTime _dateRdvDefaut = DateTime.Today.AddDays(1);
        
        // ✅ PAGINATION
        private const int PAGE_SIZE = 15;
        private int _currentPage = 1;
        private string _currentSearch = "";

        public CommandesView()
        {
            InitializeComponent();
            
            try
            {
                _commandeService = App.Services.GetRequiredService<ICommandeService>();
                _clientService = App.Services.GetRequiredService<IClientService>();
                _materielService = App.Services.GetRequiredService<IMaterielService>();
                _whatsApp = App.Services.GetRequiredService<IWhatsAppService>();
                _languageService = App.Services.GetRequiredService<ILanguageService>();
                _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();

                var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                _context = contextFactory.CreateDbContext();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'initialisation des services : " + ex.Message,
                    "Erreur d'initialisation", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Unloaded += (s, e) =>
            {
                _context?.Dispose();
                // ✅ Se désabonner pour éviter les fuites mémoire
                _eventAggregator.Unsubscribe(SettingsChangedType.Language, OnLanguageChanged);
                _eventAggregator.Unsubscribe(SettingsChangedType.AccentColor, OnThemeChanged);
            };
            
            // ✅ S'abonner aux changements de langue et de thème
            _eventAggregator.Subscribe(SettingsChangedType.Language, OnLanguageChanged);
            _eventAggregator.Subscribe(SettingsChangedType.AccentColor, OnThemeChanged);

            // ===== RECUPERER LE ROLE =====
            var authService = App.Services.GetRequiredService<IAuthService>();
            _roleUtilisateur = authService.UtilisateurConnecte?.Role ?? "";

            // ===== SECRETAIRE : cacher modifier et supprimer =====
            if (_roleUtilisateur == "Secretaire")
            {
                BtnModifier.Visibility = Visibility.Collapsed;
                BtnSupprimer.Visibility = Visibility.Collapsed;
                BtnSupprimerPiece.Visibility = Visibility.Collapsed;
            }

            // ===== COUTURIER : cacher créer/supprimer commande + supprimer pièce =====
            if (_roleUtilisateur == "Couturier")
            {
                BtnCreer.Visibility = Visibility.Collapsed;
                BtnSupprimer.Visibility = Visibility.Collapsed;
                BtnSupprimerPiece.Visibility = Visibility.Collapsed;
                BtnAjouterPiece.Visibility = Visibility.Collapsed;
            }

            // ===== BOSS : voir forcer statut + supprimer pièce =====
            if (_roleUtilisateur == "Boss")
            {
                BtnForcerStatut.Visibility = Visibility.Visible;
                BtnSupprimerPiece.Visibility = Visibility.Visible;
            }

            CmbClient.ItemsSource = _clientService.ObtenirTous();
            CmbCouturier.ItemsSource = _context.Employes.Where(e => e.Statut == "Actif").ToList();

            // ✅ FIX : Protection complète autour du chargement des types de vêtements
            try
            {
                _typesVetement = _context.TypesVetements
                    .Include(t => t.MesuresRequises)
                    .Include(t => t.Descriptions)
                    .ToList();

                // ✅ FIX : Protection si aucun type de vêtement dans la base
                if (_typesVetement == null || _typesVetement.Count == 0)
                {
                    _typesVetement = new List<TypeVetement>();
                    MessageBox.Show("Aucun type de vêtement n'est enregistré dans la base de données. Veuillez d'abord créer des types de vêtements dans l'écran Types de vêtements.",
                        "Données manquantes", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _typesVetement = new List<TypeVetement>();
                MessageBox.Show("Erreur lors du chargement des types de vêtements : " + ex.Message,
                    "Erreur de chargement", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            CmbTypeVetement.ItemsSource = _typesVetement.Select(t => new
            {
                t.IdTypeVetement,
                DisplayText = t.Nom + " (" + t.PrixBase + " FCFA)"
            }).ToList();
            CmbTypeVetement.SelectedValuePath = "IdTypeVetement";

            for (int i = 0; i <= 20; i++)
                CmbAjustement.Items.Add(i * 500);
            CmbAjustement.SelectedIndex = 0;

            // Date et heure de RDV par défaut : now + 24h, contraint à 08h-22h
            var (dateRdvDef, heureRdvDef) = CalculerRdvParDefaut();
            TxtHeureDebut.Text = DateTime.Now.ToString("HH:mm");
            TxtHeureFin.Text   = heureRdvDef.ToString(@"hh\:mm");

            // Pré-remplir la date de RDV (+24h avec règle 08h-22h) — modifiable
            _dateRdvDefaut = dateRdvDef;

            _ = ChargerCommandes();

            // CORRECTIF : sans cet appel, les champs de la 1ere piece
            // (Type de vetement, Montant, etc.) restent invisibles tant
            // qu'aucune commande n'est encore selectionnee dans le tableau —
            // impossible de creer la toute premiere commande d'une base vide.
            ViderChamps();
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
        // Mettre à jour les traductions de CommandesView
        // ------------------------------------------------------------------
        private void UpdateTranslations()
        {
            // Pour l'instant, CommandesView n'a pas beaucoup de textes traduisibles
            // Les messages MessageBox restent en français pour l'instant
        }

        // ==================================================================
        // Chargement des commandes
        // ==================================================================
        private async Task ChargerCommandes()
        {
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                GridCommandes.IsEnabled = false;
                
                // ✅ OPTIMISATION : Utiliser la version légère pour l'affichage tableau
                var result = await _commandeService.ObtenirPageLightAsync(_currentPage, PAGE_SIZE);
                GridCommandes.ItemsSource = result.Items;
                
                // Mettre à jour les boutons de pagination
                BtnPagePrecedente.IsEnabled = result.HasPrevious;
                BtnPageSuivante.IsEnabled = result.HasNext;
                
                // Mettre à jour l'info de pagination
                int start = (result.Page - 1) * result.PageSize + 1;
                int end = Math.Min(result.Page * result.PageSize, result.TotalCount);
                TxtPaginationInfo.Text = $"{start}-{end} / {result.TotalCount} commandes";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du chargement des commandes : " + ex.Message, 
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                GridCommandes.IsEnabled = true;
            }
        }

        private async void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            string motCle = TxtRecherche.Text.Trim();
            _currentSearch = motCle;
            _currentPage = 1;
            
            if (string.IsNullOrEmpty(motCle))
            {
                var result = await _commandeService.ObtenirPageLightAsync(_currentPage, PAGE_SIZE);
                GridCommandes.ItemsSource = result.Items;
                BtnPagePrecedente.IsEnabled = result.HasPrevious;
                BtnPageSuivante.IsEnabled = result.HasNext;
                int start = (result.Page - 1) * result.PageSize + 1;
                int end = Math.Min(result.Page * result.PageSize, result.TotalCount);
                TxtPaginationInfo.Text = $"{start}-{end} / {result.TotalCount} commandes";
            }
            else
            {
                var result = await _commandeService.RechercherPageLightAsync(motCle, _currentPage, PAGE_SIZE);
                GridCommandes.ItemsSource = result.Items;
                BtnPagePrecedente.IsEnabled = result.HasPrevious;
                BtnPageSuivante.IsEnabled = result.HasNext;
                int start = (result.Page - 1) * result.PageSize + 1;
                int end = Math.Min(result.Page * result.PageSize, result.TotalCount);
                TxtPaginationInfo.Text = $"{start}-{end} / {result.TotalCount} commandes";
            }
        }
        
        private async void BtnPagePrecedente_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await ChargerCommandes();
            }
        }
        
        private async void BtnPageSuivante_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await ChargerCommandes();
        }

        // ==================================================================
        // Quand on choisit un type de vetement
        // ==================================================================
        private void CmbTypeVetement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // ✅ FIX : Protection si _typesVetement est null ou vide
                if (_typesVetement == null || _typesVetement.Count == 0)
                {
                    MessageBox.Show("Aucun type de vêtement disponible. Veuillez créer des types de vêtements d'abord.",
                        "Données manquantes", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (CmbTypeVetement.SelectedValue == null) 
                    return;

                // ✅ FIX : Conversion sécurisée de SelectedValue en int
                if (!int.TryParse(CmbTypeVetement.SelectedValue.ToString(), out int id))
                {
                    MessageBox.Show("Erreur : valeur du type de vêtement invalide.",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var type = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == id);
                if (type == null)
                {
                    MessageBox.Show("Type de vêtement introuvable dans la liste.",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _prixBaseActuel = type.PrixBase;
                TxtPrixBase.Text = "Prix de base : " + type.PrixBase + " FCFA";

                if (!_chargementEnCours)
                    TxtMontant.Text = type.PrixBase.ToString();
                
                // ✅ FIX : Protection contre null - éviter le crash si descriptions non chargées
                if (type.Descriptions != null && type.Descriptions.Any())
                    CmbDescription.ItemsSource = type.Descriptions.ToList();
                else
                    CmbDescription.ItemsSource = new List<DescriptionCourante>();

                if (!_chargementEnCours)
                    CmbDescription.Text = string.Empty;

                PanelMesuresDynamiques.Children.Clear();

                // ✅ FIX : Protection contre null pour MesuresRequises
                if (type.MesuresRequises != null && type.MesuresRequises.Any())
                {
                    TxtIndicationMesures.Text = type.MesuresRequises.Count + " mesure(s) requise(s) :";

                    foreach (var mesure in type.MesuresRequises)
                    {
                        // ✅ FIX : Protection contre null pour NomMesure
                        if (string.IsNullOrWhiteSpace(mesure.NomMesure))
                            continue;

                        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

                        var label = new TextBlock
                        {
                            Text = mesure.NomMesure,
                            Width = 160,
                            FontSize = 12,
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        var combo = new ComboBox
                        {
                            Width = 80,
                            FontSize = 12,
                            Tag = mesure.NomMesure,
                            IsEditable = true,
                            IsTextSearchEnabled = true
                        };

                        for (int i = 20; i <= 300; i++)
                            combo.Items.Add((i * 0.5).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " cm");
                        combo.SelectedIndex = 0;

                        row.Children.Add(label);
                        row.Children.Add(combo);
                        PanelMesuresDynamiques.Children.Add(row);
                    }
                }
                else
                {
                    TxtIndicationMesures.Text = "Aucune mesure requise pour ce type";
                }

                if (!_chargementEnCours)
                    CalculerPrixTotal();

                // Charger les mesures antérieures du client pour réutilisation
                try
                {
                    ChargerMesuresAnterieures();
                }
                catch (Exception innerEx)
                {
                    // ✅ FIX : Ne pas planter si erreur de chargement des mesures antérieures
                    System.Diagnostics.Debug.WriteLine($"Erreur ChargerMesuresAnterieures: {innerEx.Message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement du type de vêtement :\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CmbAjustement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CalculerPrixTotal();
        }

        private void CalculerPrixTotal()
        {
            if (CmbAjustement.SelectedItem == null) return;
            decimal ajustement = (int)CmbAjustement.SelectedItem;
            decimal total = _prixBaseActuel + ajustement;
            TxtPrixTotal.Text = "Prix total : " + total + " FCFA";
            TxtMontant.Text = total.ToString();
        }

        // ==================================================================
        // Sélection d'une commande dans le tableau
        // ==================================================================
        private void GridCommandes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridCommandes.SelectedItem is not Commande cmd) return;

            _chargementEnCours = true;

            try
            {
                _commandeSelectionneeId = cmd.IdCommande;
                _pieceSelectionneeId = null;

                // Charger les informations de la commande (niveau conteneur)
                CmbClient.SelectedValue = cmd.IdClient;
                TxtHeureDebut.Text = cmd.HeureDebut.ToString(@"hh\:mm");
                TxtHeureFin.Text = cmd.HeureFin?.ToString(@"hh\:mm") ?? "";
                DateFin.SelectedDate = cmd.DateFin;

                // Charger les pièces depuis le service (avec mesures et couturier)
                _piecesCommande = _commandeService.ObtenirPiecesCommande(cmd.IdCommande);
                RafraichirListePieces();

                if (_piecesCommande.Count == 0)
                {
                    // Aucune pièce : ouvrir formulaire création
                    AfficherFormulairePiece(true);
                }
                else
                {
                    // Au moins une pièce : afficher la première directement
                    // pour voir tous les détails sans clic supplémentaire
                    var premiere = _piecesCommande[0];
                    _pieceSelectionneeId = premiere.IdPieceCommande;

                    AfficherFormulairePiece(false);

                    // Remplir les champs de la première pièce
                    var typeMatch = _typesVetement.FirstOrDefault(t => t.Nom == premiere.TypeVetement);
                    if (typeMatch != null)
                        CmbTypeVetement.SelectedValue = typeMatch.IdTypeVetement;

                    TxtMontant.Text = premiere.MontantCouture.ToString();

                    if (premiere.IdCouturier.HasValue)
                        CmbCouturier.SelectedValue = premiere.IdCouturier.Value;
                    else
                        CmbCouturier.SelectedIndex = -1;

                    if (CmbDescription.ItemsSource != null)
                    {
                        var descMatch = CmbDescription.Items
                            .Cast<DescriptionCourante>()
                            .FirstOrDefault(d => d.Texte == premiere.DescriptionPrecision);
                        if (descMatch != null)
                            CmbDescription.SelectedItem = descMatch;
                        else
                            CmbDescription.Text = premiere.DescriptionPrecision ?? "";
                    }
                    else
                    {
                        CmbDescription.Text = premiere.DescriptionPrecision ?? "";
                    }

                    for (int i = 0; i < CmbStatut.Items.Count; i++)
                    {
                        var item = (ComboBoxItem)CmbStatut.Items[i];
                        if (item.Content.ToString() == premiere.Statut)
                        { CmbStatut.SelectedIndex = i; break; }
                    }

                    // Mesures
                    var mesures = _commandeService.ObtenirMesuresPiece(premiere.IdPieceCommande);
                    foreach (var child in PanelMesuresDynamiques.Children)
                    {
                        var row = (StackPanel)child;
                        var combo = (ComboBox)row.Children[1];
                        string nomMesure = combo.Tag?.ToString() ?? "";
                        var mesure = mesures.FirstOrDefault(m => m.NomMesure == nomMesure);
                        if (mesure != null)
                        {
                            bool trouve = false;
                            for (int j = 0; j < combo.Items.Count; j++)
                            {
                                string? item = combo.Items[j]?.ToString();
                                if (item == mesure.Valeur + " cm" || item?.StartsWith(mesure.Valeur + " ") == true)
                                { combo.SelectedIndex = j; trouve = true; break; }
                            }
                            if (!trouve) combo.Text = mesure.Valeur + " cm";
                        }
                    }

                    // Photo
                    _cheminPhotoTemporaire = premiere.CheminPhoto ?? string.Empty;
                    if (!string.IsNullOrEmpty(_cheminPhotoTemporaire) && System.IO.File.Exists(_cheminPhotoTemporaire))
                    {
                        ImgPhoto.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(_cheminPhotoTemporaire));
                        TxtPhotoPlaceholder.Visibility = Visibility.Collapsed;
                        BtnSupprimerPhoto.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        ImgPhoto.Source = null;
                        _cheminPhotoTemporaire = string.Empty;
                        TxtPhotoPlaceholder.Visibility = Visibility.Visible;
                        BtnSupprimerPhoto.Visibility = Visibility.Collapsed;
                    }

                    // Ajustement
                    if (typeMatch != null)
                    {
                        decimal ecart = premiere.MontantCouture - typeMatch.PrixBase;
                        int idx = (int)Math.Round(ecart / 500m);
                        CmbAjustement.SelectedIndex = (idx >= 0 && idx < CmbAjustement.Items.Count) ? idx : 0;
                    }

                    // Bouton forcer statut
                    BtnForcerStatut.Visibility = (_piecesCommande.Count > 1 && _roleUtilisateur == "Boss")
                        ? Visibility.Visible : Visibility.Collapsed;
                }

                // Cacher les boutons Créer/Modifier commande (on est en mode consultation)
                BtnCreer.Visibility = _roleUtilisateur == "Couturier"
                    ? Visibility.Collapsed : Visibility.Visible;
            }
            finally
            {
                _chargementEnCours = false;
            }

            // Si une pièce a été chargée (commande avec pièces), rafraîchir ses matériaux
            if (_pieceSelectionneeId.HasValue)
                RafraichirMateriaux();

            // Mettre à jour les boutons WhatsApp selon le statut de la commande
            MettreAJourBoutonsWhatsApp(cmd);
        }

        // ==================================================================
        // Gestion de la liste des pièces
        // ==================================================================
        private void RafraichirListePieces()
        {
            ListePieces.ItemsSource = null;
            ListePieces.ItemsSource = _piecesCommande;

            // Mettre à jour le total
            decimal total = _piecesCommande.Sum(p => p.MontantCouture);
            TxtTotalPieces.Text = total.ToString("N0") + " FCFA";
        }

        private void AfficherFormulairePiece(bool modeCreation)
        {
            // Afficher tous les contrôles de détail pièce
            LblDetailPiece.Visibility = Visibility.Visible;
            LblDetailPiece.Text = modeCreation ? "Nouvelle piece" : "Modifier la piece";
            SepDetailPiece.Visibility = Visibility.Visible;
            LblTypeVetement.Visibility = Visibility.Visible;
            CmbTypeVetement.Visibility = Visibility.Visible;
            LblCouturier.Visibility = Visibility.Visible;
            CmbCouturier.Visibility = Visibility.Visible;
            PanelPrix.Visibility = Visibility.Visible;
            LblDescription.Visibility = Visibility.Visible;
            CmbDescription.Visibility = Visibility.Visible;
            LblMontant.Visibility = Visibility.Visible;
            TxtMontant.Visibility = Visibility.Visible;
            LblStatut.Visibility = Visibility.Visible;
            CmbStatut.Visibility = Visibility.Visible;
            TxtIndicationMesures.Visibility = Visibility.Visible;
            SepMesures.Visibility = Visibility.Visible;
            LblPhoto.Visibility = Visibility.Visible;
            PanelBoutonsPhoto.Visibility = Visibility.Visible;
            PanelPhoto.Visibility = Visibility.Visible;
            SepPhoto.Visibility = Visibility.Visible;

            // Matériaux — section entière dans son propre Border
            PanelMateriaux.Visibility = Visibility.Visible;
            PanelAjoutMateriau.Visibility = Visibility.Collapsed;
            SepAvantFormulaireMat.Visibility = Visibility.Collapsed;

            if (modeCreation)
            {
                // En création : la pièce n'est pas encore en base mais l'utilisateur
                // peut déjà saisir des matériaux → buffer temporaire (_materiauxTemporaires).
                BtnAjouterMateriau.Visibility = Visibility.Visible;
                _materiauxTemporaires.Clear();
                ListeMateriaux.ItemsSource = null;
                PanelListeMateriaux.Visibility = Visibility.Collapsed;
                TxtAucunMateriau.Text = "Aucun matériau ajouté — facultatif.";
                TxtAucunMateriau.Visibility = Visibility.Visible;
                TxtTotalMateriaux.Text = "0 FCFA";
            }
            else
            {
                BtnAjouterMateriau.Visibility = Visibility.Visible;
            }

            BtnSauvegarderPiece.Visibility = Visibility.Visible;
            PanelActionsPiece.Visibility = modeCreation ? Visibility.Collapsed : Visibility.Visible;

            // Réutilisation des mesures : visible uniquement en création
            LblReutilisationMesures.Visibility = modeCreation ? Visibility.Visible : Visibility.Collapsed;
            CmbMesuresAnterieures.Visibility = modeCreation ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MasquerFormulairePiece()
        {
            LblDetailPiece.Visibility = Visibility.Collapsed;
            SepDetailPiece.Visibility = Visibility.Collapsed;
            LblTypeVetement.Visibility = Visibility.Collapsed;
            CmbTypeVetement.Visibility = Visibility.Collapsed;
            LblCouturier.Visibility = Visibility.Collapsed;
            CmbCouturier.Visibility = Visibility.Collapsed;
            PanelPrix.Visibility = Visibility.Collapsed;
            LblDescription.Visibility = Visibility.Collapsed;
            CmbDescription.Visibility = Visibility.Collapsed;
            LblMontant.Visibility = Visibility.Collapsed;
            TxtMontant.Visibility = Visibility.Collapsed;
            LblStatut.Visibility = Visibility.Collapsed;
            CmbStatut.Visibility = Visibility.Collapsed;
            TxtIndicationMesures.Visibility = Visibility.Collapsed;
            SepMesures.Visibility = Visibility.Collapsed;
            LblPhoto.Visibility = Visibility.Collapsed;
            PanelBoutonsPhoto.Visibility = Visibility.Collapsed;
            PanelPhoto.Visibility = Visibility.Collapsed;
            SepPhoto.Visibility = Visibility.Collapsed;
            // Matériaux — section entière
            PanelMateriaux.Visibility = Visibility.Collapsed;
            PanelAjoutMateriau.Visibility = Visibility.Collapsed;
            ListeMateriaux.ItemsSource = null;
            PanelActionsPiece.Visibility = Visibility.Collapsed;
            LblReutilisationMesures.Visibility = Visibility.Collapsed;
            CmbMesuresAnterieures.Visibility = Visibility.Collapsed;
            PanelMesuresDynamiques.Children.Clear();
        }

        // ==================================================================
        // Clic sur une pièce dans la liste
        // ==================================================================
        private void PieceItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not PieceCommande piece) return;

            _pieceSelectionneeId = piece.IdPieceCommande;
            _chargementEnCours = true;

            try
            {
                // Afficher le formulaire de modification
                AfficherFormulairePiece(false);

                // Remplir les champs depuis la pièce
                var typeMatch = _typesVetement.FirstOrDefault(t => t.Nom == piece.TypeVetement);
                if (typeMatch != null)
                    CmbTypeVetement.SelectedValue = typeMatch.IdTypeVetement;

                TxtMontant.Text = piece.MontantCouture.ToString();

                if (piece.IdCouturier.HasValue)
                    CmbCouturier.SelectedValue = piece.IdCouturier.Value;
                else
                    CmbCouturier.SelectedIndex = -1;

                // Description
                if (CmbDescription.ItemsSource != null)
                {
                    var descMatch = CmbDescription.Items
                        .Cast<DescriptionCourante>()
                        .FirstOrDefault(d => d.Texte == piece.DescriptionPrecision);
                    if (descMatch != null)
                        CmbDescription.SelectedItem = descMatch;
                    else
                        CmbDescription.Text = piece.DescriptionPrecision ?? "";
                }
                else
                {
                    CmbDescription.Text = piece.DescriptionPrecision ?? "";
                }

                // Statut
                for (int i = 0; i < CmbStatut.Items.Count; i++)
                {
                    var item = (ComboBoxItem)CmbStatut.Items[i];
                    if (item.Content.ToString() == piece.Statut)
                    { CmbStatut.SelectedIndex = i; break; }
                }

                // Mesures
                var mesuresExistantes = _commandeService.ObtenirMesuresPiece(piece.IdPieceCommande);
                foreach (var child in PanelMesuresDynamiques.Children)
                {
                    var row = (StackPanel)child;
                    var combo = (ComboBox)row.Children[1];
                    string nomMesure = combo.Tag?.ToString() ?? "";
                    var mesure = mesuresExistantes.FirstOrDefault(m => m.NomMesure == nomMesure);
                    if (mesure != null)
                    {
                        bool trouve = false;
                        for (int j = 0; j < combo.Items.Count; j++)
                        {
                            string? item = combo.Items[j]?.ToString();
                            if (item == mesure.Valeur + " cm" || item?.StartsWith(mesure.Valeur + " ") == true)
                            { combo.SelectedIndex = j; trouve = true; break; }
                        }
                        if (!trouve)
                            combo.Text = mesure.Valeur + " cm";
                    }
                }

                // Photo
                _cheminPhotoTemporaire = piece.CheminPhoto ?? string.Empty;
                if (!string.IsNullOrEmpty(_cheminPhotoTemporaire) && System.IO.File.Exists(_cheminPhotoTemporaire))
                {
                    ImgPhoto.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(_cheminPhotoTemporaire));
                    TxtPhotoPlaceholder.Visibility = Visibility.Collapsed;
                    BtnSupprimerPhoto.Visibility = Visibility.Visible;
                }
                else
                {
                    ImgPhoto.Source = null;
                    _cheminPhotoTemporaire = string.Empty;
                    TxtPhotoPlaceholder.Visibility = Visibility.Visible;
                    BtnSupprimerPhoto.Visibility = Visibility.Collapsed;
                }

                // Calculer l'ajustement à partir du montant
                if (typeMatch != null)
                {
                    decimal ecart = piece.MontantCouture - typeMatch.PrixBase;
                    int indexAjustement = (int)Math.Round(ecart / 500m);
                    if (indexAjustement >= 0 && indexAjustement < CmbAjustement.Items.Count)
                        CmbAjustement.SelectedIndex = indexAjustement;
                    else
                        CmbAjustement.SelectedIndex = 0;
                }

                // Boutons d'action
                BtnDupliquerPiece.Visibility = Visibility.Visible;
                BtnSupprimerPiece.Visibility = (_roleUtilisateur == "Boss" || _roleUtilisateur != "Secretaire")
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _chargementEnCours = false;
            }

            // Charger les matériaux de la pièce maintenant que _pieceSelectionneeId est défini
            RafraichirMateriaux();
        }

        // ==================================================================
        // Réutilisation des mesures
        // ==================================================================
        private void ChargerMesuresAnterieures()
        {
            try
            {
                CmbMesuresAnterieures.ItemsSource = null;
                CmbMesuresAnterieures.SelectedIndex = -1;

                // Besoin d'un client ET d'un type pour interroger les pièces antérieures
                if (CmbClient.SelectedValue == null || CmbTypeVetement.SelectedValue == null)
                {
                    LblReutilisationMesures.Text = "Reprendre les mesures — choisissez d'abord un type de vêtement";
                    return;
                }

                // ✅ FIX : Conversion sécurisée des valeurs
                if (!int.TryParse(CmbClient.SelectedValue.ToString(), out int idClient))
                {
                    LblReutilisationMesures.Text = "Erreur : client invalide";
                    return;
                }

                if (!int.TryParse(CmbTypeVetement.SelectedValue.ToString(), out int idType))
                {
                    LblReutilisationMesures.Text = "Erreur : type de vêtement invalide";
                    return;
                }

                var type = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idType);
                if (type == null) return;

                var piecesAnterieures = _commandeService.ObtenirPiecesAnterieuresClient(
                    idClient,
                    type.Nom,
                    // On n'exclut PAS la commande courante : ses pièces existantes
                    // (ex. un Boubou déjà dans le panier) sont les meilleures
                    // mesures de référence pour la même pièce à dupliquer.
                    // On passe null pour tout inclure.
                    null);

                if (piecesAnterieures.Count == 0)
                {
                    LblReutilisationMesures.Text =
                        $"Aucune pièce {type.Nom} enregistrée pour ce client";
                }
                else
                {
                    LblReutilisationMesures.Text =
                        $"Reprendre les mesures d'une pièce {type.Nom} existante ({piecesAnterieures.Count} trouvée(s))";
                }

                CmbMesuresAnterieures.ItemsSource = piecesAnterieures;
            }
            catch (Exception ex)
            {
                // ✅ FIX : Ne pas planter l'application si erreur de chargement des mesures antérieures
                LblReutilisationMesures.Text = "Erreur lors du chargement des mesures antérieures";
                System.Diagnostics.Debug.WriteLine("Erreur ChargerMesuresAnterieures: " + ex.Message);
            }
        }

        private void CmbMesuresAnterieures_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_chargementEnCours) return;
            if (CmbMesuresAnterieures.SelectedItem is not PieceCommande pieceAnterieure) return;

            // Recharger les mesures de la pièce antérieure
            var mesures = _commandeService.ObtenirMesuresPiece(pieceAnterieure.IdPieceCommande);

            foreach (var child in PanelMesuresDynamiques.Children)
            {
                var row = (StackPanel)child;
                var combo = (ComboBox)row.Children[1];
                string nomMesure = combo.Tag?.ToString() ?? "";
                var mesure = mesures.FirstOrDefault(m => m.NomMesure == nomMesure);
                if (mesure != null)
                {
                    bool trouve = false;
                    for (int j = 0; j < combo.Items.Count; j++)
                    {
                        string? item = combo.Items[j]?.ToString();
                        if (item == mesure.Valeur + " cm" || item?.StartsWith(mesure.Valeur + " ") == true)
                        { combo.SelectedIndex = j; trouve = true; break; }
                    }
                    if (!trouve)
                        combo.Text = mesure.Valeur + " cm";
                }
            }
        }

        // ==================================================================
        // Ajouter une pièce
        // ==================================================================
        private void BtnAjouterPiece_Click(object sender, RoutedEventArgs e)
        {
            // CORRECTIF (audit) : toujours repartir de zéro à chaque clic, pour ne
            // jamais réutiliser par erreur le motif d'une exception précédente sur
            // une autre commande/pièce.
            _motifExceptionAjoutPiece = null;

            if (_commandeSelectionneeId == 0)
            {
                MessageBox.Show("Selectionnez d'abord une commande.",
                    "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Vérifier si on peut ajouter
            if (!_commandeService.PeutAjouterPiece(_commandeSelectionneeId))
            {
                if (_roleUtilisateur == "Boss")
                {
                    var motif = MessageBox.Show(
                        "Un acompte a deja ete encaisse sur cette commande.\n\n" +
                        "En tant que Boss, vous pouvez ajouter une pièce avec motif obligatoire.\n\n" +
                        "Voulez-vous continuer ?",
                        "Ajout avec exception",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (motif != MessageBoxResult.Yes) return;

                    // Demander le motif
                    string? motifSaisi = DemanderMotif("Motif de l'ajout apres encaissement");
                    if (string.IsNullOrWhiteSpace(motifSaisi)) return;

                    // CORRECTIF (audit) : on mémorise le motif pour qu'il soit
                    // réellement transmis au service lors du clic sur
                    // "Sauvegarder la piece" (voir BtnSauvegarderPiece_Click).
                    _motifExceptionAjoutPiece = motifSaisi;
                }
                else
                {
                    MessageBox.Show(
                        "Impossible d'ajouter une pièce : un acompte a déjà été encaissé.\n" +
                        "Seul le Boss peut ajouter une pièce dans ce cas.",
                        "Ajout impossible",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Réinitialiser le formulaire pour une nouvelle pièce
            _pieceSelectionneeId = null;
            _materiauxTemporaires.Clear();
            CmbTypeVetement.SelectedIndex = -1;
            CmbCouturier.SelectedIndex = -1;
            CmbDescription.Text = "";
            CmbAjustement.SelectedIndex = 0;
            TxtPrixBase.Text = "Prix de base : -";
            TxtPrixTotal.Text = "Prix total : -";
            TxtMontant.Text = "";
            CmbStatut.SelectedIndex = 0;
            _prixBaseActuel = 0;
            PanelMesuresDynamiques.Children.Clear();
            TxtIndicationMesures.Text = "Selectionnez un type de vetement";
            _cheminPhotoTemporaire = string.Empty;
            ImgPhoto.Source = null;
            TxtPhotoPlaceholder.Visibility = Visibility.Visible;
            BtnSupprimerPhoto.Visibility = Visibility.Collapsed;
            CmbMesuresAnterieures.ItemsSource = null;

            AfficherFormulairePiece(true);
        }

        // ==================================================================
        // Sauvegarder une pièce (ajout ou modification)
        // ==================================================================
        private void BtnSauvegarderPiece_Click(object sender, RoutedEventArgs e)
        {
            if (CmbTypeVetement.SelectedValue == null)
            {
                MessageBox.Show("Selectionnez un type de vetement.",
                    "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!decimal.TryParse(TxtMontant.Text, out decimal montant) || montant <= 0)
            {
                MessageBox.Show("Le montant doit être positif.",
                    "Montant invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // SECRETAIRE : confirmation + mot de passe pour l'ajout de pièce
            if (_roleUtilisateur == "Secretaire" && _pieceSelectionneeId == null)
            {
                if (!DemanderMotDePasse()) return;
            }

            // ✅ FIX : Protection contre null pour _typesVetement
            if (CmbTypeVetement.SelectedValue == null)
            {
                MessageBox.Show("Veuillez sélectionner un type de vêtement.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(CmbTypeVetement.SelectedValue.ToString(), out int idTypeVetement))
            {
                MessageBox.Show("Type de vêtement invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var typeVetement = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idTypeVetement);
            if (typeVetement == null)
            {
                MessageBox.Show("Type de vêtement introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var piece = new PieceCommande
            {
                TypeVetement = typeVetement.Nom,
                DescriptionPrecision = CmbDescription.SelectedItem is DescriptionCourante dc
                    ? dc.Texte : CmbDescription.Text,
                IdCouturier = CmbCouturier.SelectedValue as int?,
                MontantCouture = montant,
                Statut = ((ComboBoxItem)CmbStatut.SelectedItem).Content?.ToString() ?? "A faire",
                CheminPhoto = _cheminPhotoTemporaire
            };

            var mesures = CollecterMesures();

            // ── Récapitulatif avant sauvegarde (uniquement en mode création de pièce) ──
            if (!_pieceSelectionneeId.HasValue && _commandeSelectionneeId > 0)
            {
                string nomClient = "—";
                try
                {
                    var client = _clientService.ObtenirTous()
                        .FirstOrDefault(c => c.IdClient == (int)(CmbClient.SelectedValue ?? 0));
                    if (client != null) nomClient = $"{client.Nom} {client.Prenom}".Trim();
                }
                catch { }

                // ✅ FIX : Protection contre null pour _typesVetement
                if (CmbTypeVetement.SelectedValue == null || !int.TryParse(CmbTypeVetement.SelectedValue.ToString(), out int idTypeVetement2))
                {
                    MessageBox.Show("Type de vêtement invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var typeVetementMatch = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idTypeVetement2);
                if (typeVetementMatch == null)
                {
                    MessageBox.Show("Type de vêtement introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                string typeVet = typeVetementMatch.Nom;
                string description = CmbDescription.SelectedItem is DescriptionCourante dcr2
                    ? dcr2.Texte : CmbDescription.Text;
                string couturier = "—";
                if (CmbCouturier.SelectedValue is int idCout2)
                {
                    var emp2 = _context.Employes.FirstOrDefault(e => e.IdEmploye == idCout2);
                    if (emp2 != null) couturier = $"{emp2.Prenom} {emp2.Nom}".Trim();
                }

                if (!AfficherRecapitulatif(nomClient, typeVet, description, couturier,
                        montant, DateFin.SelectedDate ?? DateTime.Today,
                        mesures, _materiauxTemporaires))
                    return; // L'utilisateur a annulé
            }

            try
            {
                if (_pieceSelectionneeId.HasValue)
                {
                    // Modification d'une pièce existante
                    piece.IdPieceCommande = _pieceSelectionneeId.Value;
                    _commandeService.ModifierPiece(piece, mesures);
                    MessageBox.Show("Piece modifiee avec succes !", "Succes",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (_commandeSelectionneeId > 0)
                {
                    // CORRECTIF (audit) : le motif capturé lors de l'exception Boss
                    // (BtnAjouterPiece_Click) est maintenant réellement transmis ici.
                    // S'il n'y a pas eu d'exception (cas normal, pas d'acompte encaissé),
                    // _motifExceptionAjoutPiece est null et AjouterPiece l'ignore.
                    _commandeService.AjouterPiece(
                        _commandeSelectionneeId, piece, mesures,
                        _roleUtilisateur == "Boss",
                        _motifExceptionAjoutPiece);
                    _motifExceptionAjoutPiece = null;

                    // Persister les matériaux du buffer temporaire (saisis avant la sauvegarde)
                    if (_materiauxTemporaires.Count > 0)
                    {
                        // La pièce vient d'être créée — on doit retrouver son ID
                        var piecesRechar = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                        var nouvellepiece = piecesRechar.LastOrDefault(p => p.TypeVetement == piece.TypeVetement);
                        if (nouvellepiece != null)
                        {
                            foreach (var mat in _materiauxTemporaires)
                            {
                                // Remettre IdMateriel à 0 pour que EF Core génère l'ID en base
                                mat.IdMateriel = 0;
                                mat.IdCommande = _commandeSelectionneeId;
                                mat.IdPieceCommande = nouvellepiece.IdPieceCommande;
                                _materielService.Ajouter(mat);
                            }
                        }
                        _materiauxTemporaires.Clear();
                    }

                    MessageBox.Show("Piece ajoutee avec succes !", "Succes",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Ne devrait pas arriver
                    return;
                }

                // Recharger les pièces et rafraîchir (avec matériaux pour RowDetailsTemplate)
                _piecesCommande = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                RafraichirListePieces();
                _ = ChargerCommandes();
                // Ne pas masquer le formulaire après modification — rester en mode édition
                // pour que l'utilisateur puisse enchaîner les changements
                if (!_pieceSelectionneeId.HasValue)
                    MasquerFormulairePiece();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Operation impossible",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ==================================================================
        // Boutons inline dans les lignes du DataGrid des pièces
        // (clic sur ✏️ Éditer — charge la pièce dans le formulaire)
        // ==================================================================
        private void BtnEditerPiece_Click(object sender, RoutedEventArgs e)
        {
            // Remonter jusqu'à la DataGridRow pour récupérer la pièce
            if (sender is Button btn && btn.DataContext is PieceCommande piece)
            {
                _pieceSelectionneeId = piece.IdPieceCommande;
                // Simuler un clic sur la ligne — réutilise la logique existante
                PieceItem_MouseLeftButtonUp(btn, new MouseButtonEventArgs(
                    Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            }
        }

        // Clic sur 🗑️ Supprimer inline dans la ligne du panier
        private void BtnSupprimerPieceLigne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PieceCommande piece)
            {
                _pieceSelectionneeId = piece.IdPieceCommande;
                BtnSupprimerPiece_Click(sender, e);
            }
        }

        // ==================================================================
        // MATÉRIAUX / SUPPLÉMENTS — gestion inline dans le formulaire pièce
        // ==================================================================

        // Charge et affiche les matériaux de la pièce sélectionnée
        private void RafraichirMateriaux()
        {
            if (!_pieceSelectionneeId.HasValue) return;

            var materiaux = _materielService.ObtenirParPiece(_pieceSelectionneeId.Value);
            ListeMateriaux.ItemsSource = null;
            ListeMateriaux.ItemsSource = materiaux;

            // Afficher / masquer la liste et le message vide
            bool aDesMateriaux = materiaux.Count > 0;
            PanelListeMateriaux.Visibility = aDesMateriaux ? Visibility.Visible : Visibility.Collapsed;
            TxtAucunMateriau.Text = "Aucun matériau ajouté pour cette pièce.";
            TxtAucunMateriau.Visibility = aDesMateriaux ? Visibility.Collapsed : Visibility.Visible;

            // Mettre à jour le total matériaux affiché dans l'en-tête de la section
            decimal totalMat = materiaux.Sum(m => m.Quantite * m.PrixUnitaire);
            TxtTotalMateriaux.Text = totalMat > 0
                ? $"{totalMat:N0} FCFA"
                : "0 FCFA";
        }

        // Ferme le formulaire d'ajout matériau sans rien enregistrer
        private void BtnAnnulerFormulaireMateriau_Click(object sender, RoutedEventArgs e)
        {
            PanelAjoutMateriau.Visibility = Visibility.Collapsed;
            SepAvantFormulaireMat.Visibility = Visibility.Collapsed;
            TxtMatDesignation.Text = string.Empty;
            TxtMatQuantite.Text = "1";
            TxtMatPrix.Text = string.Empty;
        }

        // Ouvre / ferme le mini-formulaire d'ajout matériau
        private void BtnAjouterMateriau_Click(object sender, RoutedEventArgs e)
        {
            bool visible = PanelAjoutMateriau.Visibility == Visibility.Visible;
            PanelAjoutMateriau.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            SepAvantFormulaireMat.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            if (!visible)
            {
                // Réinitialiser les champs à l'ouverture
                TxtMatDesignation.Text = string.Empty;
                TxtMatQuantite.Text = "1";
                TxtMatPrix.Text = string.Empty;
                TxtMatDesignation.Focus();
            }
        }

        // Valide et enregistre le matériau en base (ou dans le buffer si pièce pas encore sauvée)
        private void BtnConfirmerMateriau_Click(object sender, RoutedEventArgs e)
        {
            string designation = TxtMatDesignation.Text.Trim();
            if (string.IsNullOrEmpty(designation))
            {
                MessageBox.Show("La désignation du matériau est obligatoire.",
                    "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtMatDesignation.Focus();
                return;
            }

            // ✅ Validation de sécurité pour la désignation
            if (!Helpers.ValidationHelper.EstTexteSecurise(designation, out string erreur))
            {
                MessageBox.Show($"Désignation invalide : {erreur}",
                    "Erreur de validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtMatDesignation.Focus();
                return;
            }

            if (!int.TryParse(TxtMatQuantite.Text.Trim(), out int quantite) || quantite <= 0)
            {
                MessageBox.Show("La quantité doit être un entier positif.",
                    "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtMatQuantite.Focus();
                return;
            }

            if (!decimal.TryParse(TxtMatPrix.Text.Trim().Replace(" ", ""), out decimal prix) || prix < 0)
            {
                MessageBox.Show("Le prix unitaire doit être un nombre positif.",
                    "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtMatPrix.Focus();
                return;
            }

            var materiau = new MaterielSupplement
            {
                Designation = designation,
                Quantite = quantite,
                PrixUnitaire = prix
            };

            // Réinitialiser le formulaire
            TxtMatDesignation.Text = string.Empty;
            TxtMatQuantite.Text = "1";
            TxtMatPrix.Text = string.Empty;
            PanelAjoutMateriau.Visibility = Visibility.Collapsed;
            SepAvantFormulaireMat.Visibility = Visibility.Collapsed;

            if (_pieceSelectionneeId.HasValue)
            {
                // Pièce existante en base → persister directement
                try
                {
                    materiau.IdCommande = _commandeSelectionneeId;
                    materiau.IdPieceCommande = _pieceSelectionneeId.Value;
                    _materielService.Ajouter(materiau);
                    RafraichirMateriaux();
                    RafraichirTotalAvecMateriaux();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur : " + ex.Message, "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Nouvelle pièce pas encore sauvée → stocker dans le buffer temporaire
                _materiauxTemporaires.Add(materiau);
                RafraichirListeMatériauxTemporaires();
            }
        }

        // Supprime un matériau depuis la liste inline
        private void BtnSupprimerMateriau_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not int idMateriel) return;

            var confirmation = MessageBox.Show("Supprimer ce matériau ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes) return;

            if (idMateriel < 0)
            {
                // Mode buffer temporaire : ID négatif = index -(id+1)
                int idx = -(idMateriel + 1);
                if (idx >= 0 && idx < _materiauxTemporaires.Count)
                {
                    _materiauxTemporaires.RemoveAt(idx);
                    RafraichirListeMatériauxTemporaires();
                }
                return;
            }

            // Mode pièce existante en base
            try
            {
                _materielService.Supprimer(idMateriel);
                RafraichirMateriaux();
                RafraichirTotalAvecMateriaux();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Suppression impossible",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Rafraîchit la liste et les totaux depuis le buffer temporaire (mode création)
        private void RafraichirListeMatériauxTemporaires()
        {
            // Créer des copies pour l'affichage avec des IDs négatifs (pour identifier la suppression)
            // Sans toucher aux objets originaux du buffer (leurs IdMateriel restent à 0)
            var itemsAffichage = _materiauxTemporaires
                .Select((m, i) => new MaterielSupplement
                {
                    IdMateriel = -(i + 1),   // négatif = item buffer, pour BtnSupprimerMateriau_Click
                    Designation = m.Designation,
                    Quantite = m.Quantite,
                    PrixUnitaire = m.PrixUnitaire
                })
                .ToList();

            ListeMateriaux.ItemsSource = null;
            ListeMateriaux.ItemsSource = itemsAffichage;

            bool aDesMateriaux = _materiauxTemporaires.Count > 0;
            PanelListeMateriaux.Visibility = aDesMateriaux ? Visibility.Visible : Visibility.Collapsed;
            TxtAucunMateriau.Text = "Aucun matériau ajouté — facultatif.";
            TxtAucunMateriau.Visibility = aDesMateriaux ? Visibility.Collapsed : Visibility.Visible;

            decimal totalMat = _materiauxTemporaires.Sum(m => m.Quantite * m.PrixUnitaire);
            TxtTotalMateriaux.Text = totalMat > 0 ? $"{totalMat:N0} FCFA" : "0 FCFA";
        }

        // Met à jour TxtTotalPieces en incluant les matériaux de toutes les pièces
        private void RafraichirTotalAvecMateriaux()
        {
            decimal totalCouture = _piecesCommande.Sum(p => p.MontantCouture);
            decimal totalMateriaux = _commandeSelectionneeId > 0
                ? _materielService.TotalParCommande(_commandeSelectionneeId)
                : 0m;
            decimal total = totalCouture + totalMateriaux;

            TxtTotalPieces.Text = totalMateriaux > 0
                ? $"{total:N0} FCFA  (dont {totalMateriaux:N0} mat.)"
                : $"{total:N0} FCFA";
        }

        // ==================================================================
        // Dupliquer une pièce
        // ==================================================================
        private void BtnDupliquerPiece_Click(object sender, RoutedEventArgs e)
        {
            if (!_pieceSelectionneeId.HasValue) return;

            try
            {
                var nouvellePiece = _commandeService.DupliquerPiece(_pieceSelectionneeId.Value);

                // Recharger
                _piecesCommande = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                RafraichirListePieces();
                _ = ChargerCommandes();

                MessageBox.Show("Piece dupliquee avec succes !", "Succes",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Duplication impossible",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ==================================================================
        // Supprimer une pièce
        // ==================================================================
        private void BtnSupprimerPiece_Click(object sender, RoutedEventArgs e)
        {
            if (!_pieceSelectionneeId.HasValue) return;

            var r = MessageBox.Show(
                "Supprimer cette piece ?\n\nCette action est irreversible.",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (r == MessageBoxResult.Yes)
            {
                try
                {
                    _commandeService.SupprimerPiece(_pieceSelectionneeId.Value);

                    _piecesCommande = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                    RafraichirListePieces();
                    _ = ChargerCommandes();
                    MasquerFormulairePiece();
                    _pieceSelectionneeId = null;

                    MessageBox.Show("Piece supprimee.", "Succes",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Suppression impossible",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // ==================================================================
        // Forcer le statut de toutes les pièces
        // ==================================================================
        private void BtnForcerStatut_Click(object sender, RoutedEventArgs e)
        {
            if (_commandeSelectionneeId == 0) return;

            var dialog = new Window
            {
                Title = "Forcer le statut de toutes les pieces",
                Width = 350,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                SizeToContent = SizeToContent.Height
            };

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            panel.Children.Add(new TextBlock
            {
                Text = "Nouveau statut pour toutes les pieces :",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var cmb = new ComboBox
            {
                Height = 36,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            };
            cmb.Items.Add("A faire");
            cmb.Items.Add("En cours");
            cmb.Items.Add("Terminee");
            cmb.Items.Add("Livree");
            cmb.SelectedIndex = 2; // "Terminee" par défaut
            panel.Children.Add(cmb);

            var message = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Height = 18,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(message);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnOk = new Button
            {
                Content = "Appliquer",
                Width = 100,
                Height = 36,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var btnAnnuler = new Button
            {
                Content = "Annuler",
                Width = 90,
                Height = 36,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Cursor = Cursors.Hand
            };

            btnOk.Click += (s, ev) =>
            {
                string statut = cmb.SelectedItem?.ToString() ?? "";
                try
                {
                    _commandeService.ForcerStatutToutesPieces(_commandeSelectionneeId, statut);
                    dialog.DialogResult = true;
                    dialog.Close();

                    // Recharger
                    _piecesCommande = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                    RafraichirListePieces();
                    _ = ChargerCommandes();

                    MessageBox.Show("Statut de toutes les pieces mis a jour.",
                        "Succes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (InvalidOperationException ex)
                {
                    message.Text = ex.Message;
                }
            };

            btnAnnuler.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnAnnuler);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }

        // ==================================================================
        // Collecter les mesures
        // ==================================================================
        private List<Mesure> CollecterMesures()
        {
            var mesures = new List<Mesure>();
            foreach (var child in PanelMesuresDynamiques.Children)
            {
                var row = (StackPanel)child;
                var combo = (ComboBox)row.Children[1];
                string texte = combo.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(texte))
                {
                    string valeur = texte.Replace(" cm", "").Replace("cm", "").Trim();
                    mesures.Add(new Mesure
                    {
                        NomMesure = combo.Tag?.ToString() ?? "",
                        Valeur = valeur
                    });
                }
            }
            return mesures;
        }

        private TimeSpan? ParseHeure(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return null;
            if (TimeSpan.TryParse(texte, out var resultat)) return resultat;
            return null;
        }

        // ==================================================================
        // ===== BOUTONS CRUD COMMANDE =====
        // ==================================================================
        private void BtnCreer_Click(object sender, RoutedEventArgs e)
        {
            // Validation minimale : client et date fin
            if (CmbClient.SelectedValue == null)
            {
                MessageBox.Show("Selectionnez un client.", "Champs manquants",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (DateFin.SelectedDate == null)
            {
                MessageBox.Show("Selectionnez une date de fin prevue.", "Champs manquants",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ✅ CORRECTIF AUDIT #A2 : Validation date rendez-vous
            // Le rendez-vous ne peut pas être antérieur à la date de dépôt (aujourd'hui)
            if (DateFin.SelectedDate < DateTime.Today)
            {
                MessageBox.Show(
                    "La date de rendez-vous ne peut pas être antérieure à aujourd'hui.",
                    "Date invalide",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (CmbTypeVetement.SelectedValue == null)
            {
                MessageBox.Show("Selectionnez un type de vetement pour la premiere piece.",
                    "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!decimal.TryParse(TxtMontant.Text, out decimal montant) || montant <= 0)
            {
                MessageBox.Show("Le montant doit etre positif.",
                    "Montant invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Construire les objets (sans sauvegarder encore)
            var clientChoisi = _clientService.ObtenirTous()
                .FirstOrDefault(c => c.IdClient == (int)CmbClient.SelectedValue);
            string nomClient = clientChoisi != null
                ? $"{clientChoisi.Nom} {clientChoisi.Prenom}".Trim()
                : "—";

            // ✅ FIX : Protection contre null pour _typesVetement
            if (CmbTypeVetement.SelectedValue == null || !int.TryParse(CmbTypeVetement.SelectedValue.ToString(), out int idTypeVetement3))
            {
                MessageBox.Show("Type de vêtement invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var typeVetementObj = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idTypeVetement3);
            if (typeVetementObj == null)
            {
                MessageBox.Show("Type de vêtement introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            string typeVetement = typeVetementObj.Nom;

            string description = CmbDescription.SelectedItem is DescriptionCourante dcr
                ? dcr.Texte : CmbDescription.Text;

            string couturier = "—";
            if (CmbCouturier.SelectedValue is int idCout)
            {
                var emp = _context.Employes.FirstOrDefault(e => e.IdEmploye == idCout);
                if (emp != null) couturier = $"{emp.Prenom} {emp.Nom}".Trim();
            }

            var mesures = CollecterMesures();

            // ── Afficher le récapitulatif ──────────────────────────────────
            // Passer une copie de la liste pour que l'affichage soit stable
            if (!AfficherRecapitulatif(nomClient, typeVetement, description, couturier,
                montant, DateFin.SelectedDate!.Value, mesures,
                _materiauxTemporaires.ToList()))
                return;  // L'utilisateur a annulé

            // SECRETAIRE : confirmation + mot de passe
            if (_roleUtilisateur == "Secretaire")
            {
                if (!DemanderMotDePasse()) return;
            }

            var commande = new Commande
            {
                IdClient = (int)CmbClient.SelectedValue,
                DateDebut = DateTime.Now,
                DateFin = DateFin.SelectedDate ?? DateTime.Now.AddDays(7),
                HeureDebut = ParseHeure(TxtHeureDebut.Text) ?? DateTime.Now.TimeOfDay,
                HeureFin = ParseHeure(TxtHeureFin.Text),
                CheminPhoto = _cheminPhotoTemporaire
            };

            var piece = new PieceCommande
            {
                TypeVetement = typeVetement,
                DescriptionPrecision = description,
                IdCouturier = CmbCouturier.SelectedValue as int?,
                MontantCouture = montant,
                Statut = ((ComboBoxItem)CmbStatut.SelectedItem).Content?.ToString() ?? "A faire",
                CheminPhoto = _cheminPhotoTemporaire
            };

            try
            {
                _commandeService.Ajouter(commande, piece, mesures);

                // Persister les matériaux du buffer temporaire
                if (_materiauxTemporaires.Count > 0)
                {
                    var piecesCreees = _commandeService.ObtenirPiecesCommande(commande.IdCommande);
                    var premierePiece = piecesCreees.FirstOrDefault();
                    if (premierePiece != null)
                    {
                        foreach (var mat in _materiauxTemporaires)
                        {
                            // Remettre IdMateriel à 0 pour que EF Core génère l'ID en base
                            mat.IdMateriel = 0;
                            mat.IdCommande = commande.IdCommande;
                            mat.IdPieceCommande = premierePiece.IdPieceCommande;
                            _materielService.Ajouter(mat);
                        }
                    }
                    _materiauxTemporaires.Clear();
                }

                _ = ChargerCommandes();

                var autrePiece = MessageBox.Show(
                    "Commande creee avec succes !\n\nLe client a-t-il d'autres vetements a ajouter a cette meme commande ?",
                    "Piece supplementaire ?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (autrePiece == MessageBoxResult.Yes)
                {
                    // Sélectionner la commande dans le tableau pour déclencher
                    // GridCommandes_SelectionChanged et mettre à jour _commandeSelectionneeId
                    var itemsSource = GridCommandes.ItemsSource as IEnumerable<Commande>
                                      ?? GridCommandes.ItemsSource?.Cast<Commande>();
                    var commandeCreee = itemsSource?.FirstOrDefault(c => c.IdCommande == commande.IdCommande);

                    if (commandeCreee != null)
                    {
                        // Forcer _commandeSelectionneeId avant BtnAjouterPiece_Click
                        // au cas où SelectionChanged serait asynchrone
                        _commandeSelectionneeId = commande.IdCommande;
                        GridCommandes.SelectedItem = commandeCreee;
                        // Ouvrir le formulaire vierge pour la pièce suivante
                        BtnAjouterPiece_Click(this, new RoutedEventArgs());
                    }
                    else
                    {
                        // Fallback : sélectionner via l'ID directement
                        _commandeSelectionneeId = commande.IdCommande;
                        _piecesCommande = _commandeService.ObtenirPiecesCommande(commande.IdCommande);
                        RafraichirListePieces();
                        BtnAjouterPiece_Click(this, new RoutedEventArgs());
                    }
                }
                else
                {
                    ViderChamps();
                }
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Creation impossible",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Fenêtre de récapitulatif ───────────────────────────────────────
        private bool AfficherRecapitulatif(
            string client, string typeVetement, string description,
            string couturier, decimal montant, DateTime dateRdv,
            List<Mesure> mesures, List<MaterielSupplement> materiaux)
        {
            var dialog = new Window
            {
                Title = "Récapitulatif — Confirmer la commande",
                Width = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                SizeToContent = SizeToContent.Height
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 580
            };

            var root = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };

            // ── En-tête rouge ──
            root.Children.Add(new TextBlock
            {
                Text = "Récapitulatif de la commande",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            root.Children.Add(new Border
            {
                Height = 3, Width = 48, CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 18)
            });

            // ── Bloc info commande ──
            var blcCmd = CreerBlocRecap("📌  Commande");
            AjouterLigneRecap(blcCmd, "Client", client);
            AjouterLigneRecap(blcCmd, "Date RDV", dateRdv.ToString("dd/MM/yyyy"));
            root.Children.Add(blcCmd);

            // ── Bloc pièce ──
            var blcPiece = CreerBlocRecap("🧵  Pièce");
            AjouterLigneRecap(blcPiece, "Type", typeVetement);
            AjouterLigneRecap(blcPiece, "Couturier", couturier);
            AjouterLigneRecap(blcPiece, "Montant couture", $"{montant:N0} FCFA");
            if (!string.IsNullOrWhiteSpace(description))
                AjouterLigneRecap(blcPiece, "Description", description);
            root.Children.Add(blcPiece);

            // ── Bloc mesures ──
            if (mesures.Count > 0)
            {
                var blcMes = CreerBlocRecap("📐  Mesures");
                foreach (var m in mesures)
                    AjouterLigneRecap(blcMes, m.NomMesure, m.Valeur + " cm");
                root.Children.Add(blcMes);
            }

            // ── Bloc matériaux ──
            decimal totalMat = 0;
            var blcMat = CreerBlocRecap("📦  Matériaux & Suppléments");
            if (materiaux.Count > 0)
            {
                foreach (var mat in materiaux)
                {
                    AjouterLigneRecap(blcMat, mat.Designation,
                        $"{mat.Quantite} × {mat.PrixUnitaire:N0} F = {mat.Montant:N0} FCFA");
                    totalMat += mat.Montant;
                }
                AjouterLigneRecap(blcMat, "Sous-total matériaux",
                    $"{totalMat:N0} FCFA", gras: true);
            }
            else
            {
                // Afficher explicitement "aucun matériau" pour que la section soit visible
                var sp = (System.Windows.Controls.StackPanel)blcMat.Child;
                sp.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "Aucun matériau — 0 FCFA",
                    FontSize = 12,
                    FontStyle = System.Windows.FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF))
                });
            }
            root.Children.Add(blcMat);

            // ── Total général ──
            var totalGeneral = montant + totalMat;
            var blcTotal = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 8, 0, 20)
            };
            var spTotal = new StackPanel();
            // Décomposition si matériaux
            if (totalMat > 0)
            {
                var spDec = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                spDec.Children.Add(new TextBlock { Text = $"Couture : {montant:N0}  +  Matériaux : {totalMat:N0}  =",
                    FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)) });
                spTotal.Children.Add(spDec);
            }
            var rowTotal = new Grid();
            rowTotal.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowTotal.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lblTotal = new TextBlock
            {
                Text = "TOTAL GÉNÉRAL",
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xA8, 0xA4)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var valTotal = new TextBlock
            {
                Text = $"{totalGeneral:N0} FCFA",
                FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblTotal, 0);
            Grid.SetColumn(valTotal, 1);
            rowTotal.Children.Add(lblTotal);
            rowTotal.Children.Add(valTotal);
            spTotal.Children.Add(rowTotal);
            blcTotal.Child = spTotal;
            root.Children.Add(blcTotal);

            // ── Boutons ──
            var errMsg = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Height = 16,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(errMsg);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnAnnuler = new Button
            {
                Content = "✎  Corriger",
                Width = 110, Height = 38, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var btnConfirmer = new Button
            {
                Content = "✔  Confirmer",
                Width = 130, Height = 38, FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            btnAnnuler.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            btnConfirmer.Click += (s, ev) => { dialog.DialogResult = true; dialog.Close(); };

            btnRow.Children.Add(btnAnnuler);
            btnRow.Children.Add(btnConfirmer);
            root.Children.Add(btnRow);

            scroll.Content = root;
            dialog.Content = scroll;
            dialog.Owner = Window.GetWindow(this);

            return dialog.ShowDialog() == true;
        }

        private static Border CreerBlocRecap(string titre)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF5, 0xF3)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE0, 0xDC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = titre,
                FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x73, 0x55)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            border.Child = stack;
            return border;
        }

        private static void AjouterLigneRecap(Border bloc, string label, string valeur, bool gras = false)
        {
            var stack = (StackPanel)bloc.Child;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                VerticalAlignment = VerticalAlignment.Top
            };
            var val = new TextBlock
            {
                Text = valeur,
                FontSize = 12,
                FontWeight = gras ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                TextWrapping = TextWrapping.Wrap
            };

            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(val, 1);
            row.Children.Add(lbl);
            row.Children.Add(val);
            stack.Children.Add(row);
        }

        private void BtnModifier_Click(object sender, RoutedEventArgs e)
        {
            if (_commandeSelectionneeId == 0)
            {
                MessageBox.Show("Selectionnez une commande.", "Attention",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Modifier uniquement les infos au niveau commande (dates, client)
            // Les pièces se modifient individuellement via BtnSauvegarderPiece
            if (CmbClient.SelectedValue == null)
            {
                MessageBox.Show("Selectionnez un client.", "Champs manquants",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ✅ CORRECTIF AUDIT #A2 : Validation date rendez-vous en modification
            if (DateFin.SelectedDate != null && DateFin.SelectedDate < DateTime.Today)
            {
                MessageBox.Show(
                    "La date de rendez-vous ne peut pas être antérieure à aujourd'hui.",
                    "Date invalide",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var r = MessageBox.Show(
                "Modifier les informations de la commande ?\n" +
                "(Client, dates). Les pieces se modifient individuellement.",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (r != MessageBoxResult.Yes) return;

            using var context = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
            var existante = context.Commandes.FirstOrDefault(c => c.IdCommande == _commandeSelectionneeId);
            if (existante == null) return;

            existante.IdClient = (int)CmbClient.SelectedValue;
            existante.DateFin = DateFin.SelectedDate ?? existante.DateFin;
            existante.HeureDebut = ParseHeure(TxtHeureDebut.Text) ?? existante.HeureDebut;
            existante.HeureFin = ParseHeure(TxtHeureFin.Text);

            context.SaveChanges();

            _ = ChargerCommandes();
            MessageBox.Show("Commande modifiee avec succes !", "Succes",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (_commandeSelectionneeId == 0)
            {
                MessageBox.Show("Selectionnez une commande.", "Attention",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var r = MessageBox.Show("Supprimer cette commande ?\n\nToutes les pieces seront supprimees.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (r == MessageBoxResult.Yes)
            {
                try
                {
                    _commandeService.Supprimer(_commandeSelectionneeId);
                    _ = ChargerCommandes();
                    ViderChamps();
                    MessageBox.Show("Commande supprimee.", "Succes",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Suppression impossible",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnVider_Click(object sender, RoutedEventArgs e) { ViderChamps(); }

        // ==================================================================
        // WHATSAPP — Notifications client
        // ==================================================================

        /// <summary>
        /// Met à jour la visibilité et le label des boutons WhatsApp
        /// selon le statut de la commande sélectionnée.
        /// </summary>
        private void MettreAJourBoutonsWhatsApp(Commande? cmd)
        {
            bool aCommande = cmd != null;
            bool estTerminee = aCommande && cmd!.StatutGlobalAffiche == "Terminée";

            // Bouton pied (toujours visible si une commande est sélectionnée)
            BtnWhatsAppPied.Visibility = aCommande ? Visibility.Visible : Visibility.Collapsed;
            if (aCommande)
            {
                TxtWhatsAppPiedLabel.Text = estTerminee ? "Prête !" : "RDV";
                BtnWhatsAppPied.Background = System.Windows.Media.Brushes.Green;
                BtnWhatsAppPied.ToolTip = estTerminee
                    ? "Notifier le client : commande prête"
                    : "Envoyer rappel de RDV au client";
            }

            // Boutons dans l'en-tête (Prête + RDV)
            BtnWhatsAppPrete.Visibility = estTerminee ? Visibility.Visible : Visibility.Collapsed;
            BtnWhatsAppRdv.Visibility   = aCommande   ? Visibility.Visible : Visibility.Collapsed;
        }

        private Commande? ObtenirCommandeSelectionnee()
        {
            if (_commandeSelectionneeId == 0) return null;
            return _commandeService.ObtenirTous()
                .FirstOrDefault(c => c.IdCommande == _commandeSelectionneeId);
        }

        // Bouton dans le tableau (colonne 💬)
        private void BtnWhatsAppCommande_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Commande cmd) return;
            EnvoyerWhatsAppCommande(cmd);
        }

        // Bouton "💬 Prête !" dans l'en-tête du panneau
        private async void BtnWhatsAppPrete_Click(object sender, RoutedEventArgs e)
        {
            var cmd = ObtenirCommandeSelectionnee();
            if (cmd == null) return;
            try
            {
                await _whatsApp.NotifierCommandePreteAsync(cmd);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir WhatsApp :\n" + ex.Message,
                    "Erreur WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Bouton "⏰ RDV" dans l'en-tête du panneau
        private async void BtnWhatsAppRdv_Click(object sender, RoutedEventArgs e)
        {
            var cmd = ObtenirCommandeSelectionnee();
            if (cmd == null) return;
            try
            {
                await _whatsApp.NotifierRappelRdvAsync(cmd);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir WhatsApp :\n" + ex.Message,
                    "Erreur WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Bouton "💬 Notifier" dans le pied — envoie Prête si terminée, RDV sinon
        private async void BtnWhatsAppPied_Click(object sender, RoutedEventArgs e)
        {
            var cmd = ObtenirCommandeSelectionnee();
            if (cmd == null) return;
            try
            {
                if (cmd.StatutGlobalAffiche == "Terminée")
                    await _whatsApp.NotifierCommandePreteAsync(cmd);
                else
                    await _whatsApp.NotifierRappelRdvAsync(cmd);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir WhatsApp :\n" + ex.Message,
                    "Erreur WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void EnvoyerWhatsAppCommande(Commande cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd.Client?.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone.",
                    "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                // Si terminée → message "prête", sinon → rappel RDV
                if (cmd.StatutGlobalAffiche == "Terminée")
                    await _whatsApp.NotifierCommandePreteAsync(cmd);
                else
                    await _whatsApp.NotifierRappelRdvAsync(cmd);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir WhatsApp :\n" + ex.Message,
                    "Erreur WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================================================================
        // Import / Capture / Suppression photo
        // ==================================================================
        private static readonly string[] ExtensionsAutorisees = { ".jpg", ".jpeg", ".png", ".bmp" };
        private const long TailleMaxOctets = 5 * 1024 * 1024;

        private void BtnImporterPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selectionner une photo",
                Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var info = new System.IO.FileInfo(dialog.FileName);
                string ext = info.Extension.ToLowerInvariant();
                if (!ExtensionsAutorisees.Contains(ext))
                {
                    MessageBox.Show($"Format non autorise : {ext}\nFormats acceptes : JPG, PNG, BMP.",
                        "Fichier invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (info.Length > TailleMaxOctets)
                {
                    MessageBox.Show(
                        $"L'image est trop volumineuse ({info.Length / 1024 / 1024:N1} Mo).\nTaille max : 5 Mo.",
                        "Fichier trop grand", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!EstImageValide(dialog.FileName))
                {
                    MessageBox.Show("Le fichier n'est pas une image valide.",
                        "Fichier invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string dossierPhotos = GestionCoutureApp.Helpers.AppPaths.DossierPhotos;
                string suffixeUnique = Guid.NewGuid().ToString("N")[..8];
                string nomFichier = $"photo_{DateTime.Now:yyyyMMdd_HHmmss}_{suffixeUnique}{ext}";
                string cheminDestination = System.IO.Path.Combine(dossierPhotos, nomFichier);

                System.IO.File.Copy(dialog.FileName, cheminDestination, overwrite: true);

                // Compression JPEG 1024×768 / 70% à la source (après copie)
                GestionCoutureApp.Helpers.PhotoCompressor.Compresser(cheminDestination, cheminDestination);

                _cheminPhotoTemporaire = cheminDestination;
                ImgPhoto.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(cheminDestination));
                TxtPhotoPlaceholder.Visibility = Visibility.Collapsed;
                BtnSupprimerPhoto.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'import : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool EstImageValide(string chemin)
        {
            try
            {
                using var fs = new System.IO.FileStream(chemin, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                var header = new byte[8];
                int lu = fs.Read(header, 0, header.Length);
                if (lu < 3) return false;
                if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return true;
                if (lu >= 8 && header[0] == 0x89 && header[1] == 0x50 &&
                    header[2] == 0x4E && header[3] == 0x47) return true;
                if (header[0] == 0x42 && header[1] == 0x4D) return true;
                return false;
            }
            catch { return false; }
        }

        private void BtnPrendrePhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var webcamWindow = new WebcamCaptureWindow();
                webcamWindow.Owner = Window.GetWindow(this);
                if (webcamWindow.ShowDialog() == true &&
                    !string.IsNullOrEmpty(webcamWindow.CapturedFilePath))
                {
                    _cheminPhotoTemporaire = webcamWindow.CapturedFilePath;
                    ImgPhoto.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(_cheminPhotoTemporaire));
                    TxtPhotoPlaceholder.Visibility = Visibility.Collapsed;
                    BtnSupprimerPhoto.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur webcam : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSupprimerPhoto_Click(object sender, RoutedEventArgs e)
        {
            _cheminPhotoTemporaire = string.Empty;
            ImgPhoto.Source = null;
            TxtPhotoPlaceholder.Visibility = Visibility.Visible;
            BtnSupprimerPhoto.Visibility = Visibility.Collapsed;
        }

        // ==================================================================
        // Vider tous les champs
        // ==================================================================
        // ==================================================================
        // Calcul RDV par défaut : heure actuelle + 24h, contraint à 08h-22h
        // Règle métier : pas de RDV entre 22h01 et 07h59.
        //   - Si +24h tombe entre 22h01 et minuit  → lendemain à 08h00
        //   - Si +24h tombe entre 00h00 et 07h59   → même jour à 08h00
        //   - Sinon on garde l'heure calculée
        // ==================================================================
        private static (DateTime date, TimeSpan heure) CalculerRdvParDefaut()
        {
            var candidat = DateTime.Now.AddHours(24);
            var h = candidat.TimeOfDay;

            // Plage interdite : < 08h00 ou >= 22h00
            if (h >= new TimeSpan(22, 0, 0))
            {
                // Lendemain à 08h00
                return (candidat.Date.AddDays(1), new TimeSpan(8, 0, 0));
            }
            if (h < new TimeSpan(8, 0, 0))
            {
                // Même jour que le candidat à 08h00
                return (candidat.Date, new TimeSpan(8, 0, 0));
            }
            // Heure valide → on arrondit à la demi-heure supérieure
            int minutesArr = ((h.Minutes / 30) + 1) * 30;
            if (minutesArr >= 60)
                h = new TimeSpan(h.Hours + 1, 0, 0);
            else
                h = new TimeSpan(h.Hours, minutesArr, 0);

            return (candidat.Date, h);
        }

        private void ViderChamps()
        {
            _commandeSelectionneeId = 0;
            _pieceSelectionneeId = null;
            _piecesCommande = new List<PieceCommande>();
            _materiauxTemporaires.Clear();
            CmbClient.SelectedIndex = -1;
            CmbCouturier.SelectedIndex = -1;
            CmbTypeVetement.SelectedIndex = -1;
            CmbDescription.Text = "";
            CmbAjustement.SelectedIndex = 0;
            TxtPrixBase.Text = "Prix de base : -";
            TxtPrixTotal.Text = "Prix total : -";
            TxtMontant.Text = "";
            TxtHeureDebut.Text = DateTime.Now.ToString("HH:mm");
            // Recalculer le RDV par défaut (+24h, règle 08h-22h)
            var (dateRdv, heureRdv) = CalculerRdvParDefaut();
            _dateRdvDefaut          = dateRdv;
            DateFin.SelectedDate    = dateRdv;
            TxtHeureFin.Text        = heureRdv.ToString(@"hh\:mm");
            CmbStatut.SelectedIndex = 0;
            _prixBaseActuel = 0;
            PanelMesuresDynamiques.Children.Clear();
            TxtIndicationMesures.Text = "Selectionnez un type de vetement";
            GridCommandes.SelectedItem = null;
            ListePieces.ItemsSource = null;
            TxtTotalPieces.Text = "0 FCFA";

            _cheminPhotoTemporaire = string.Empty;
            ImgPhoto.Source = null;
            TxtPhotoPlaceholder.Visibility = Visibility.Visible;
            BtnSupprimerPhoto.Visibility = Visibility.Collapsed;

            MasquerFormulairePiece();

            // Réafficher le formulaire pour la première pièce (mode création)
            AfficherFormulairePiece(true);

            // Masquer les boutons WhatsApp quand aucune commande n'est sélectionnée
            MettreAJourBoutonsWhatsApp(null);
        }
        // ==================================================================
        private string? DemanderMotif(string titre)
        {
            string? resultat = null;

            var dialog = new Window
            {
                Title = titre,
                Width = 420,
                Height = 240,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                SizeToContent = SizeToContent.Height
            };

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            panel.Children.Add(new TextBlock
            {
                Text = "Motif :",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var txtMotif = new TextBox
            {
                Height = 70,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(txtMotif);

            var message = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Height = 18,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(message);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnOk = new Button
            {
                Content = "Confirmer",
                Width = 110,
                Height = 36,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var btnAnnuler = new Button
            {
                Content = "Annuler",
                Width = 100,
                Height = 36,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Cursor = Cursors.Hand
            };

            btnOk.Click += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(txtMotif.Text))
                {
                    message.Text = "Le motif est obligatoire.";
                    return;
                }
                resultat = txtMotif.Text.Trim();
                dialog.DialogResult = true;
                dialog.Close();
            };

            btnAnnuler.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnAnnuler);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();

            return resultat;
        }

        // ==================================================================
        // ===== FENETRE MOT DE PASSE (securite secretaire) =====
        // ==================================================================
        private bool DemanderMotDePasse()
        {
            var dialog = new Window
            {
                Title = "Verification d'identite",
                Width = 400,
                Height = 260,
                MinHeight = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                SizeToContent = SizeToContent.Height
            };

            var mainPanel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Pour des raisons de securite,",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 2)
            });
            mainPanel.Children.Add(new TextBlock
            {
                Text = "veuillez saisir votre mot de passe :",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 14)
            });

            var passwordBox = new PasswordBox
            {
                Height = 38,
                FontSize = 14,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            mainPanel.Children.Add(passwordBox);

            var message = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Margin = new Thickness(0, 0, 0, 16),
                Height = 18
            };
            mainPanel.Children.Add(message);

            mainPanel.Children.Add(new Separator
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Margin = new Thickness(0, 0, 0, 14)
            });

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnOk = new Button
            {
                Content = "Confirmer",
                Width = 110,
                Height = 38,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand
            };

            var btnAnnuler = new Button
            {
                Content = "Annuler",
                Width = 100,
                Height = 38,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Cursor = Cursors.Hand
            };

            btnOk.Click += (s, ev) =>
            {
                var authService = App.Services.GetRequiredService<IAuthService>();
                string mdp = passwordBox.Password.Trim();
                var user = authService.UtilisateurConnecte;
                bool mdpValide = user != null &&
                    (GestionCoutureApp.Helpers.PasswordHasher.EstAncienFormatSha256(user.MotDePasse)
                        ? user.MotDePasse == GestionCoutureApp.Helpers.PasswordHasher.HasherAncienSha256(mdp)
                        : GestionCoutureApp.Helpers.PasswordHasher.Verifier(mdp, user.MotDePasse));
                if (mdpValide)
                {
                    dialog.DialogResult = true;
                    dialog.Close();
                }
                else
                {
                    message.Text = "Mot de passe incorrect !";
                    passwordBox.Clear();
                    passwordBox.Focus();
                }
            };

            btnAnnuler.Click += (s, ev) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            passwordBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == System.Windows.Input.Key.Enter)
                    btnOk.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnAnnuler);
            mainPanel.Children.Add(btnPanel);

            dialog.Content = mainPanel;
            dialog.Owner = Window.GetWindow(this);

            return dialog.ShowDialog() == true;
        }

        // ✅ CORRECTIF AUDIT #A3 : Validation saisie montants (décimaux positifs uniquement)
        private void TxtMontant_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            Helpers.ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtMontant_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            Helpers.ValidationHelper.TextBox_Pasting(sender, e);
        }

        // ✅ Validation sécurisée pour la désignation de matériel
        private void TxtMatDesignation_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            try
            {
                Helpers.ValidationHelper.TextBox_PreviewTextInputTexteSecurise(sender, e);
            }
            catch
            {
                e.Handled = true;
            }
        }
    }
}
