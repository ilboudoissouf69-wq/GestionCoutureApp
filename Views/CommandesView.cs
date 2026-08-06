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
        private int _commandeSelectionneeId;
        private int? _pieceSelectionneeId; // null = aucune pièce sélectionnée
        // CORRECTIF (audit) : le motif saisi lors de l'exception Boss (ajout de
        // pièce après encaissement) doit survivre jusqu'à BtnSauvegarderPiece_Click,
        // qui est le seul endroit où AjouterPiece() est réellement appelé. Sans ce
        // champ, le motif capturé dans BtnAjouterPiece_Click se perdait et
        // AjouterPiece() finissait toujours par rejeter l'enregistrement.
        private string? _motifExceptionAjoutPiece;
        private decimal _prixBaseActuel;
        private List<TypeVetement> _typesVetement;
        private bool _chargementEnCours = false;
        private string _cheminPhotoTemporaire = string.Empty;
        private string _roleUtilisateur;

        // Pièces chargées pour la commande sélectionnée
        private List<PieceCommande> _piecesCommande = new();

        public CommandesView()
        {
            InitializeComponent();
            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _clientService = App.Services.GetRequiredService<IClientService>();

            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();
            Unloaded += (s, e) => _context.Dispose();

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

            _typesVetement = _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .Include(t => t.Descriptions)
                .ToList();

            CmbTypeVetement.ItemsSource = _typesVetement.Select(t => new
            {
                t.IdTypeVetement,
                DisplayText = t.Nom + " (" + t.PrixBase + " FCFA)"
            }).ToList();
            CmbTypeVetement.SelectedValuePath = "IdTypeVetement";

            for (int i = 0; i <= 20; i++)
                CmbAjustement.Items.Add(i * 500);
            CmbAjustement.SelectedIndex = 0;

            // Heure debut auto
            TxtHeureDebut.Text = DateTime.Now.ToString("HH:mm");

            ChargerCommandes();

            // CORRECTIF : sans cet appel, les champs de la 1ere piece
            // (Type de vetement, Montant, etc.) restent invisibles tant
            // qu'aucune commande n'est encore selectionnee dans le tableau —
            // impossible de creer la toute premiere commande d'une base vide.
            ViderChamps();
        }

        // ==================================================================
        // Chargement des commandes
        // ==================================================================
        private void ChargerCommandes()
        {
            GridCommandes.ItemsSource = null;
            GridCommandes.ItemsSource = _commandeService.ObtenirTous();
        }

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            string motCle = TxtRecherche.Text.Trim();
            if (string.IsNullOrEmpty(motCle)) ChargerCommandes();
            else GridCommandes.ItemsSource = _commandeService.Rechercher(motCle);
        }

        // ==================================================================
        // Quand on choisit un type de vetement
        // ==================================================================
        private void CmbTypeVetement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTypeVetement.SelectedValue == null) return;
            int id = (int)CmbTypeVetement.SelectedValue;
            var type = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == id);
            if (type == null) return;

            _prixBaseActuel = type.PrixBase;
            TxtPrixBase.Text = "Prix de base : " + type.PrixBase + " FCFA";

            if (!_chargementEnCours)
                TxtMontant.Text = type.PrixBase.ToString();

            CmbDescription.ItemsSource = type.Descriptions.ToList();

            if (!_chargementEnCours)
                CmbDescription.Text = string.Empty;

            PanelMesuresDynamiques.Children.Clear();
            TxtIndicationMesures.Text = type.MesuresRequises.Count + " mesure(s) requise(s) :";

            foreach (var mesure in type.MesuresRequises)
            {
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

            if (!_chargementEnCours)
                CalculerPrixTotal();

            // Charger les mesures antérieures du client pour réutilisation
            ChargerMesuresAnterieures();
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

                // Afficher les champs de pièce en mode "ajout" (première pièce vide)
                // ou afficher la première pièce si la commande a des pièces
                if (_piecesCommande.Count == 0)
                {
                    AfficherFormulairePiece(true);
                }
                else
                {
                    // Ne pas afficher le formulaire de pièce par défaut :
                    // l'utilisateur doit cliquer sur une pièce pour la modifier
                    MasquerFormulairePiece();

                    // Afficher le bouton forcer statut si plusieurs pièces
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
            BtnSauvegarderPiece.Visibility = Visibility.Collapsed;
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
        }

        // ==================================================================
        // Réutilisation des mesures
        // ==================================================================
        private void ChargerMesuresAnterieures()
        {
            CmbMesuresAnterieures.ItemsSource = null;
            CmbMesuresAnterieures.SelectedIndex = -1;

            if (CmbClient.SelectedValue == null || CmbTypeVetement.SelectedValue == null) return;

            int idClient = (int)CmbClient.SelectedValue;
            int idType = (int)CmbTypeVetement.SelectedValue;
            var type = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idType);
            if (type == null) return;

            var piecesAnterieures = _commandeService.ObtenirPiecesAnterieuresClient(
                idClient, type.Nom, _commandeSelectionneeId > 0 ? _commandeSelectionneeId : (int?)null);

            CmbMesuresAnterieures.ItemsSource = piecesAnterieures;
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

            var piece = new PieceCommande
            {
                TypeVetement = _typesVetement.First(t => t.IdTypeVetement == (int)CmbTypeVetement.SelectedValue).Nom,
                DescriptionPrecision = CmbDescription.SelectedItem is DescriptionCourante dc
                    ? dc.Texte : CmbDescription.Text,
                IdCouturier = CmbCouturier.SelectedValue as int?,
                MontantCouture = montant,
                Statut = ((ComboBoxItem)CmbStatut.SelectedItem).Content?.ToString() ?? "A faire",
                CheminPhoto = _cheminPhotoTemporaire
            };

            var mesures = CollecterMesures();

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
                    MessageBox.Show("Piece ajoutee avec succes !", "Succes",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Ne devrait pas arriver
                    return;
                }

                // Recharger les pièces et rafraîchir
                _piecesCommande = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                RafraichirListePieces();
                ChargerCommandes();
                MasquerFormulairePiece();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Operation impossible",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
                ChargerCommandes();

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
                    ChargerCommandes();
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
                    ChargerCommandes();

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

            // Vérifier qu'au moins les champs pièce de base sont remplis
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

            // SECRETAIRE : confirmation + mot de passe
            if (_roleUtilisateur == "Secretaire")
            {
                var confirm = MessageBox.Show(
                    "Voulez-vous reellement enregistrer cette commande ?\n\nCette action est irreversible.",
                    "Confirmation d'enregistrement",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
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
                TypeVetement = _typesVetement.First(t => t.IdTypeVetement == (int)CmbTypeVetement.SelectedValue).Nom,
                DescriptionPrecision = CmbDescription.SelectedItem is DescriptionCourante dc1
                    ? dc1.Texte : CmbDescription.Text,
                IdCouturier = CmbCouturier.SelectedValue as int?,
                MontantCouture = decimal.Parse(TxtMontant.Text),
                Statut = ((ComboBoxItem)CmbStatut.SelectedItem).Content?.ToString() ?? "A faire",
                CheminPhoto = _cheminPhotoTemporaire
            };

            try
            {
                _commandeService.Ajouter(commande, piece, CollecterMesures());
                ChargerCommandes();

                // NOUVEAU : methode professionnelle en 2 temps.
                // - 1 seul vetement -> la commande est deja complete, rien d'autre a faire.
                // - Plusieurs vetements -> on repart directement sur la meme commande,
                //   deja selectionnee, avec un formulaire "nouvelle piece" vierge ouvert,
                //   pret a recevoir le couturier/montant/mesures du vetement suivant.
                var autrePiece = MessageBox.Show(
                    "Commande creee avec succes !\n\nLe client a-t-il d'autres vetements a ajouter a cette meme commande ?",
                    "Piece supplementaire ?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (autrePiece == MessageBoxResult.Yes)
                {
                    var commandeCreee = ((List<Commande>)GridCommandes.ItemsSource)
                        ?.FirstOrDefault(c => c.IdCommande == commande.IdCommande);

                    if (commandeCreee != null)
                    {
                        GridCommandes.SelectedItem = commandeCreee; // charge la commande + ses pieces
                        BtnAjouterPiece_Click(this, new RoutedEventArgs()); // ouvre le formulaire vierge
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

            ChargerCommandes();
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
                    ChargerCommandes();
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
        private void ViderChamps()
        {
            _commandeSelectionneeId = 0;
            _pieceSelectionneeId = null;
            _piecesCommande = new List<PieceCommande>();
            CmbClient.SelectedIndex = -1;
            CmbCouturier.SelectedIndex = -1;
            CmbTypeVetement.SelectedIndex = -1;
            CmbDescription.Text = "";
            CmbAjustement.SelectedIndex = 0;
            TxtPrixBase.Text = "Prix de base : -";
            TxtPrixTotal.Text = "Prix total : -";
            TxtMontant.Text = "";
            TxtHeureDebut.Text = DateTime.Now.ToString("HH:mm");
            TxtHeureFin.Text = "";
            DateFin.SelectedDate = null;
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
        }

        // ==================================================================
        // Demander un motif (pour les exceptions Boss)
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
    }
}
