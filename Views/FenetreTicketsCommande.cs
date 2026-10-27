using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Fenêtre d'impression des tickets commande 80 mm (entièrement en code-behind).
    /// Deux onglets :
    ///   – ÉTIQUETTE COLIS : fiche interne de l'atelier (mesures, couturier). Aucun prix.
    ///   – BON CLIENT      : justificatif client (montants : total, acompte, solde).
    ///
    /// Trois modes d'impression sélectionnables avant d'imprimer :
    ///   – Étiquette seule | Bon client seul | Étiquette + Bon client
    ///
    /// Le mode par défaut est calculé par <see cref="TicketsCommandeLogique.ModeParDefaut"/>.
    /// Chaque impression est tracée dans le journal d'audit (COMMANDE_IMPRESSION).
    ///
    /// IMPORTANT : ne jamais appeler le constructeur directement depuis du code async.
    /// Utiliser la fabrique <see cref="OuvrirAsync"/> qui évite tout .Result sur le thread UI.
    /// </summary>
    public class FenetreTicketsCommande : Window
    {
        // ── Constantes visuelles — impression noir/blanc (économie d'encre) ────
        private const string SEP  = "================================";
        private const string SEP2 = "--------------------------------";
        private const string SEP_APERCU = "✂ - - - - -";

        private static readonly Brush Noir   = Brushes.Black;
        private static readonly Brush Gris   = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        private static readonly Brush Rouge  = Brushes.Black;   // → noir à l'impression
        private static readonly Brush Vert   = Brushes.Black;   // → noir à l'impression
        private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        private static readonly Brush Bleu   = Brushes.Black;   // → noir à l'impression
        private static readonly Brush Marque = Brushes.Black;   // → noir à l'impression

        // ── Données injectées ────────────────────────────────────────────
        private readonly Commande _commande;
        private readonly ICommandeService _commandeService;
        private readonly IAuditService _auditService;
        private readonly ReceiptInfo _receiptInfo;
        private readonly int _idOperateur;
        private readonly string _nomOperateur;
        private readonly string _roleOperateur;
        private readonly ILogService? _logService;

        // ── Panels de contenu (un par onglet, un par pièce pour l'étiquette) ─
        // Étiquette : soit un seul panel (toutes pièces) soit N panels (une par pièce)
        private readonly List<StackPanel> _panelsEtiquette = new();
        private readonly StackPanel _panelBonClient;

        // ── État UI ──────────────────────────────────────────────────────
        private bool _ongletEtiquetteActif = true;
        private ModeImpression _mode;
        private Button _btnImprimer = null!;

        // ── Contrôles de mode ────────────────────────────────────────────
        private RadioButton _rbEtiquetteBon  = null!;
        private RadioButton _rbEtiquette     = null!;
        private RadioButton _rbBon           = null!;
        private CheckBox    _cbUneParPiece   = null!;
        private ScrollViewer _scrollViewer   = null!;

        // ═════════════════════════════════════════════════════════════════
        // FABRIQUE ASYNCHRONE — point d'entrée unique depuis l'extérieur
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Charge la commande complète et les services puis ouvre la fenêtre.
        /// N'utilise jamais .Result — aucun risque de blocage du thread UI.
        /// </summary>
        public static async Task OuvrirAsync(int idCommande, Window owner)
        {
            try
            {
                var commandeService = App.Services.GetRequiredService<ICommandeService>();
                var receiptService  = App.Services.GetRequiredService<IReceiptService>();
                var auditService    = App.Services.GetRequiredService<IAuditService>();
                var authService     = App.Services.GetRequiredService<IAuthService>();

                // Chargement async — pas de .Result
                var receiptInfo = await receiptService.GetReceiptInfoAsync();

                // ObtenirParId est synchrone mais léger — acceptable sur le thread UI
                var commande = commandeService.ObtenirParId(idCommande);
                if (commande == null)
                {
                    MessageBox.Show("Commande introuvable.", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var op = authService.UtilisateurConnecte;
                int idOp    = op?.IdEmploye ?? 0;
                string nomOp  = op != null ? $"{op.Prenom} {op.Nom}".Trim() : "Inconnu";
                string roleOp = op?.Role ?? "Inconnu";

                var fenetre = new FenetreTicketsCommande(
                    commande, commandeService, auditService,
                    receiptInfo, idOp, nomOp, roleOp);

                fenetre.Owner = owner;
                fenetre.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur ouverture tickets :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // CONSTRUCTEUR — reçoit des données déjà chargées, ne fait rien d'async
        // ═════════════════════════════════════════════════════════════════

        public FenetreTicketsCommande(
            Commande commande,
            ICommandeService commandeService,
            IAuditService auditService,
            ReceiptInfo receiptInfo,
            int idOperateur,
            string nomOperateur,
            string roleOperateur)
        {
            _commande        = commande        ?? throw new ArgumentNullException(nameof(commande));
            _commandeService = commandeService ?? throw new ArgumentNullException(nameof(commandeService));
            _auditService    = auditService    ?? throw new ArgumentNullException(nameof(auditService));
            _receiptInfo     = receiptInfo     ?? new ReceiptInfo();
            _idOperateur     = idOperateur;
            _nomOperateur    = nomOperateur;
            _roleOperateur   = roleOperateur;
            _logService      = App.Services.GetService<ILogService>();

            var visibilite = TicketsCommandeLogique.VisibiliteSelonRole(roleOperateur);

            // Mode par défaut
            _mode = visibilite.OngletBonClientVisible
                ? TicketsCommandeLogique.ModeParDefaut(commande.MontantEncaisse)
                : ModeImpression.EtiquetteSeule;

            Title                   = $"Tickets Commande — CMD #{commande.IdCommande}";
            Width                   = 860;
            Height                  = 780;
            MinWidth                = 740;
            WindowStartupLocation   = WindowStartupLocation.CenterOwner;
            ResizeMode              = ResizeMode.CanResize;
            Background              = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
            UseLayoutRounding       = true;
            SnapsToDevicePixels     = true;

            // ── Panels de contenu ─────────────────────────────────────────
            _panelBonClient = new StackPanel { Width = 290, Margin = new Thickness(0, 15, 0, 15) };
            // Les panels étiquette seront créés dans ConstruireEtiquettes()

            // ── Grille principale ─────────────────────────────────────────
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // onglets
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // barre mode
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // aperçu
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // boutons

            // ── Barre d'onglets ───────────────────────────────────────────
            var tabPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background  = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B))
            };

            var btnTabEtiquette = CréerOnglet("📦  Étiquette colis", true);
            var btnTabBon       = CréerOnglet("🧾  Bon client",       false);
            btnTabBon.Visibility = visibilite.OngletBonClientVisible ? Visibility.Visible : Visibility.Collapsed;

            tabPanel.Children.Add(btnTabEtiquette);
            tabPanel.Children.Add(btnTabBon);
            Grid.SetRow(tabPanel, 0);
            mainGrid.Children.Add(tabPanel);

            // ── Barre de mode ─────────────────────────────────────────────
            var barreMode = ConstruireBarreMode();
            Grid.SetRow(barreMode, 1);
            mainGrid.Children.Add(barreMode);

            // ── Zone d'aperçu ─────────────────────────────────────────────
            _scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var borderApercu = new Border
            {
                Background   = Brushes.White,
                Margin       = new Thickness(20, 10, 20, 6),
                CornerRadius = new CornerRadius(4),
                Child        = _scrollViewer
            };
            Grid.SetRow(borderApercu, 2);
            mainGrid.Children.Add(borderApercu);

            // ── Boutons du bas ────────────────────────────────────────────
            _btnImprimer = CréerBouton($"🖨️  Imprimer : {TicketsCommandeLogique.LibelleMode(_mode)}", "#2E86C1");
            _btnImprimer.Width = 260;
            var btnFermer = CréerBouton("Fermer", "#95A5A6");

            _btnImprimer.Click += BtnImprimer_Click;
            btnFermer.Click    += (_, _) => Close();

            var btnPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 4, 0, 18)
            };
            btnPanel.Children.Add(_btnImprimer);
            btnPanel.Children.Add(btnFermer);
            Grid.SetRow(btnPanel, 3);
            mainGrid.Children.Add(btnPanel);

            Content = mainGrid;

            // ── Construction des tickets ──────────────────────────────────
            ConstruireEtiquettes();
            ConstruireBonClient();

            // ── Afficher l'étiquette par défaut ───────────────────────────
            BasculerApercu();

            // ── Logique onglets ───────────────────────────────────────────
            btnTabEtiquette.Click += (_, _) =>
            {
                _ongletEtiquetteActif = true;
                BasculerApercu();
                btnTabEtiquette.Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                btnTabBon.Background       = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            };
            btnTabBon.Click += (_, _) =>
            {
                _ongletEtiquetteActif = false;
                BasculerApercu();
                btnTabBon.Background       = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                btnTabEtiquette.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            };
        }

        // ═════════════════════════════════════════════════════════════════
        // BARRE DE MODE
        // ═════════════════════════════════════════════════════════════════

        private Border ConstruireBarreMode()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background  = new SolidColorBrush(Color.FromRgb(0x2D, 0x3A, 0x4A)),
                Margin      = new Thickness(0)
            };

            // Label
            panel.Children.Add(new TextBlock
            {
                Text              = "Mode :",
                Foreground        = Brushes.White,
                FontSize          = 11,
                FontWeight        = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(14, 0, 10, 0)
            });

            // RadioButtons
            _rbEtiquetteBon = CréerRadio("Étiquette + Bon client", _mode == ModeImpression.EtiquetteEtBonClient);
            _rbEtiquette    = CréerRadio("Étiquette seule",         _mode == ModeImpression.EtiquetteSeule);
            _rbBon          = CréerRadio("Bon client seul",         _mode == ModeImpression.BonClientSeul);
            var visibilite = TicketsCommandeLogique.VisibiliteSelonRole(_roleOperateur);
            _rbBon.Visibility = visibilite.ModeBonClientSeulVisible ? Visibility.Visible : Visibility.Collapsed;
            _rbEtiquetteBon.Visibility = visibilite.ModeEtiquetteEtBonVisible ? Visibility.Visible : Visibility.Collapsed;

            _rbEtiquetteBon.Checked += (_, _) => ModeChange(ModeImpression.EtiquetteEtBonClient);
            _rbEtiquette.Checked    += (_, _) => ModeChange(ModeImpression.EtiquetteSeule);
            _rbBon.Checked          += (_, _) => ModeChange(ModeImpression.BonClientSeul);

            panel.Children.Add(_rbEtiquetteBon);
            panel.Children.Add(_rbEtiquette);
            panel.Children.Add(_rbBon);

            // Séparateur
            panel.Children.Add(new Border
            {
                Width           = 1,
                Background      = new SolidColorBrush(Color.FromRgb(0x4A, 0x5A, 0x6A)),
                Margin          = new Thickness(10, 6, 10, 6),
                VerticalAlignment = VerticalAlignment.Stretch
            });

            // CheckBox une étiquette par pièce
            _cbUneParPiece = new CheckBox
            {
                Content           = "Une étiquette par pièce",
                Foreground        = Brushes.White,
                FontSize          = 11,
                IsChecked         = false,
                IsEnabled         = _commande.Pieces.Count > 1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 14, 0)
            };
            _cbUneParPiece.Checked   += (_, _) => { RebuildEtiquettes(); BasculerApercu(); };
            _cbUneParPiece.Unchecked += (_, _) => { RebuildEtiquettes(); BasculerApercu(); };
            panel.Children.Add(_cbUneParPiece);

            return new Border
            {
                Child      = panel,
                Height     = 38,
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x3A, 0x4A))
            };
        }

        private void ModeChange(ModeImpression nouveau)
        {
            _mode = nouveau;
            if (_btnImprimer != null)
                _btnImprimer.Content = $"🖨️  Imprimer : {TicketsCommandeLogique.LibelleMode(_mode)}";
        }

        private void BasculerApercu()
        {
            if (_ongletEtiquetteActif)
            {
                // Si plusieurs panels étiquette (une par pièce), les empiler dans un wrapper
                if (_panelsEtiquette.Count == 1)
                {
                    _scrollViewer.Content = _panelsEtiquette[0];
                }
                else
                {
                    var wrapper = new StackPanel { Width = 290, Margin = new Thickness(0, 8, 0, 8) };
                    foreach (var p in _panelsEtiquette)
                    {
                        wrapper.Children.Add(p);
                        if (p != _panelsEtiquette.Last())
                            Ligne(wrapper, SEP_APERCU, 10, TextAlignment.Center, Gris, FontWeights.Bold);
                    }
                    _scrollViewer.Content = wrapper;
                }
            }
            else
            {
                _scrollViewer.Content = _panelBonClient;
            }
        }

        private void RebuildEtiquettes()
        {
            _panelsEtiquette.Clear();
            ConstruireEtiquettes();
        }

        // ═════════════════════════════════════════════════════════════════
        // ÉTIQUETTE(S) COLIS — fiche interne atelier (aucun prix)
        // ═════════════════════════════════════════════════════════════════

        private void ConstruireEtiquettes()
        {
            bool uneParPiece = _cbUneParPiece?.IsChecked == true && _commande.Pieces.Count > 1;

            if (!uneParPiece || _commande.Pieces.Count == 0)
            {
                // Une seule étiquette avec toutes les pièces
                var panel = new StackPanel { Width = 290, Margin = new Thickness(0, 15, 0, 15) };
                ConstruireContenuEtiquette(panel, _commande.Pieces, indexPiece: null, totalPieces: _commande.Pieces.Count);
                _panelsEtiquette.Add(panel);
            }
            else
            {
                // N étiquettes — une par pièce
                for (int i = 0; i < _commande.Pieces.Count; i++)
                {
                    var panel = new StackPanel { Width = 290, Margin = new Thickness(0, 15, 0, 15) };
                    ConstruireContenuEtiquette(
                        panel,
                        new List<PieceCommande> { _commande.Pieces[i] },
                        indexPiece: i + 1,
                        totalPieces: _commande.Pieces.Count);
                    _panelsEtiquette.Add(panel);
                }
            }
        }

        private void ConstruireContenuEtiquette(
            StackPanel p,
            List<PieceCommande> pieces,
            int? indexPiece,
            int totalPieces)
        {
            string nomAtelier = _receiptInfo.NomAtelier.Length > 0 ? _receiptInfo.NomAtelier : "ATELIER";
            string tel        = _receiptInfo.Telephone;
            string nomClient  = NomClient();
            string telClient  = _commande.Client?.Telephone ?? "—";

            // ── Mention interne ───────────────────────────────────────────
            Espace(p, 8);
            Ligne(p, SEP, 9, TextAlignment.Center, Rouge);
            Ligne(p, "À AGRAFER SUR LE COLIS", 10, TextAlignment.Center, Rouge, FontWeights.Bold);
            Ligne(p, "Document interne — ne pas remettre au client", 8, TextAlignment.Center, Rouge);
            Ligne(p, SEP, 9, TextAlignment.Center, Rouge);
            Espace(p, 8);

            // ── En-tête atelier ───────────────────────────────────────────
            Ligne(p, nomAtelier, 13, TextAlignment.Center, Noir, FontWeights.Bold);
            if (!string.IsNullOrWhiteSpace(tel))
                Ligne(p, "Tel: " + tel, 9, TextAlignment.Center, Gris);
            Espace(p, 6);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ── Numéro commande (très grand) ──────────────────────────────
            Ligne(p, $"CMD #{_commande.IdCommande}",
                22, TextAlignment.Center, Noir, FontWeights.Black);
            Ligne(p, TicketsCommandeLogique.ResumePieces(_commande.Pieces),
                10, TextAlignment.Center, Gris, FontWeights.Bold);
            if (indexPiece.HasValue)
                Ligne(p, $"PIÈCE {indexPiece}/{totalPieces}", 11, TextAlignment.Center, Bleu, FontWeights.Bold);
            Ligne(p, TexteRetrait("RETRAIT"), 15, TextAlignment.Center, Noir, FontWeights.Bold);
            Espace(p, 4);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 8);

            // ── Infos client + dates ──────────────────────────────────────
            Ligne(p, "INFORMATIONS CLIENT", 10, TextAlignment.Left, Marque, FontWeights.Bold);
            Espace(p, 3);
            LigneInfo(p, "Client",    nomClient);
            LigneInfo(p, "Tél.",      telClient);
            LigneInfo(p, "Dépôt",     _commande.DateDebut.ToString("dd/MM/yyyy"));
            LigneInfo(p, "Livraison", _commande.DateFin.ToString("dd/MM/yyyy"));
            if (_commande.HeureFin.HasValue)
                LigneInfo(p, "Heure RDV", _commande.HeureFin.Value.ToString(@"hh\:mm"));
            Espace(p, 8);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);

            // ── Pièces ────────────────────────────────────────────────────
            if (pieces.Count == 0)
            {
                Espace(p, 10);
                Ligne(p, "(aucune pièce enregistrée)", 10, TextAlignment.Center, Gris);
            }
            else
            {
                for (int i = 0; i < pieces.Count; i++)
                {
                    var piece = pieces[i];
                    // N° de pièce dans le contexte de la commande complète
                    int numGlobal = indexPiece ?? (i + 1);
                    int totGlobal = indexPiece.HasValue ? totalPieces : pieces.Count;

                    Espace(p, 8);
                    Ligne(p, $"PIÈCE {numGlobal}/{totGlobal} — {piece.TypeVetement.ToUpperInvariant()}",
                        11, TextAlignment.Left, Bleu, FontWeights.Bold);
                    Ligne(p, SEP2, 9, TextAlignment.Center, Gris);
                    Espace(p, 4);

                    string couturier = piece.Couturier != null
                        ? (piece.Couturier.Prenom + " " + piece.Couturier.Nom).Trim()
                        : "— non assigné —";
                    LigneInfo(p, "Couturier", couturier);
                    LigneInfo(p, "Statut",    piece.StatutAffiche);

                    if (!string.IsNullOrWhiteSpace(piece.DescriptionPrecision))
                        LigneInfo(p, "Description", piece.DescriptionPrecision);

                    Espace(p, 5);

                    var mesures = piece.Mesures?.ToList() ?? new List<Mesure>();
                    if (mesures.Count > 0)
                    {
                        Ligne(p, "MESURES :", 10, TextAlignment.Left, Marque, FontWeights.Bold);
                        Espace(p, 2);
                        foreach (var m in mesures)
                        {
                            int pts = Math.Max(3, 26 - m.NomMesure.Length - m.Valeur.Length);
                            Ligne(p,
                                "   " + m.NomMesure + " " +
                                new string('.', pts) + " " + m.Valeur + " cm",
                                10, TextAlignment.Left, Noir);
                        }
                    }
                    else
                    {
                        Ligne(p, "   (aucune mesure enregistrée)", 9, TextAlignment.Left, Gris);
                    }

                    Espace(p, 6);
                    if (i < pieces.Count - 1)
                        Ligne(p, SEP2, 9, TextAlignment.Center, Gris);
                }
            }

            // ── Pied de fiche ─────────────────────────────────────────────
            Espace(p, 6);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 4);
            Ligne(p, "Document interne — ne pas remettre au client",
                8, TextAlignment.Center, Rouge);
            var soldeCommande = TicketsCommandeLogique.LibelleSolde(_commande.ResteAPayer);
            Ligne(p, soldeCommande.Texte, 10, TextAlignment.Center,
                soldeCommande.EstSolde ? Vert : Rouge, FontWeights.Bold);
            Ligne(p, "Imprimé le " + DateTime.Now.ToString("dd/MM/yyyy à HH:mm"),
                8, TextAlignment.Center, Gris);
            Espace(p, 14);
        }

        // ═════════════════════════════════════════════════════════════════
        // BON CLIENT — justificatif avec montants (aucune mesure, aucun couturier)
        // ═════════════════════════════════════════════════════════════════

        private void ConstruireBonClient()
        {
            var p         = _panelBonClient;
            string nomAt  = _receiptInfo.NomAtelier;
            string tel    = _receiptInfo.Telephone;
            string adresse = _receiptInfo.Adresse;
            string pied   = _receiptInfo.PiedRecu;

            decimal total   = _commande.MontantTotalAvecMateriaux;
            decimal encaisse = _commande.MontantEncaisse;
            decimal solde   = _commande.ResteAPayer;

            Espace(p, 8);

            // ── Logo (en premier, toujours en haut) ───────────────────────
            try
            {
                var logo = new System.Windows.Controls.Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/Resources/logo_retouche_choco.png")),
                    Width               = 100,
                    Height              = 100,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Stretch             = System.Windows.Media.Stretch.Uniform,
                    Margin              = new Thickness(0, 0, 0, 6)
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                    logo, System.Windows.Media.BitmapScalingMode.HighQuality);
                p.Children.Add(logo);
            }
            catch { /* logo absent → on continue */ }

            // ── En-tête atelier ───────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(nomAt))
                Ligne(p, nomAt, 14, TextAlignment.Center, Noir, FontWeights.Bold);
            if (!string.IsNullOrWhiteSpace(tel))
                Ligne(p, tel, 9, TextAlignment.Center, Gris);
            if (!string.IsNullOrWhiteSpace(adresse))
                Ligne(p, adresse, 9, TextAlignment.Center, Gris);
            Espace(p, 6);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ── Titre ─────────────────────────────────────────────────────
            Ligne(p, "BON DE COMMANDE",   12, TextAlignment.Center, Noir, FontWeights.Bold);
            Espace(p, 4);
            Ligne(p, $"CMD #{_commande.IdCommande}",
                20, TextAlignment.Center, Bleu, FontWeights.Bold);
            Ligne(p, TicketsCommandeLogique.ResumePieces(_commande.Pieces),
                10, TextAlignment.Center, Gris);
            Espace(p, 6);
            Ligne(p, SEP2, 8, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ── Infos client + dates ──────────────────────────────────────
            LignePad(p, "Client",    NomClient());
            if (!string.IsNullOrWhiteSpace(_commande.Client?.Telephone))
                LignePad(p, "Tél",   _commande.Client.Telephone);
            LignePad(p, "Dépôt",     _commande.DateDebut.ToString("dd/MM/yyyy"));
            LignePad(p, "Livraison", _commande.DateFin.ToString("dd/MM/yyyy"));
            if (_commande.HeureFin.HasValue)
                LignePad(p, "Heure RDV",
                    _commande.HeureFin.Value.ToString(@"hh\:mm"));
            Espace(p, 5);
            Ligne(p, TexteRetrait("RETRAIT PRÉVU"), 14, TextAlignment.Center, Noir, FontWeights.Bold);
            Espace(p, 6);

            // ── Pièces (sans prix ni mesures) ─────────────────────────────
            if (_commande.Pieces.Count > 0)
            {
                Ligne(p, "ARTICLES", 10, TextAlignment.Left, Gris, FontWeights.Bold);
                Espace(p, 3);
                foreach (var piece in _commande.Pieces)
                {
                    string desc = string.IsNullOrWhiteSpace(piece.DescriptionPrecision)
                        ? piece.TypeVetement
                        : piece.TypeVetement + " — " + piece.DescriptionPrecision;
                    Ligne(p, "  • " + desc, 10, TextAlignment.Left, Noir);
                }
                Ligne(p, $"Total : {_commande.Pieces.Count} pièces", 10, TextAlignment.Left, Noir, FontWeights.Bold);
                Espace(p, 6);
            }
            else
            {
                Ligne(p, "Total : 0 pièce", 10, TextAlignment.Left, Noir, FontWeights.Bold);
                Espace(p, 6);
            }

            // ── Section financière ────────────────────────────────────────
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 4);

            LigneMontant(p, "Total",        Fcfa(total),    Noir);
            if (encaisse > 0m)
                LigneMontant(p, "Acompte versé", Fcfa(encaisse), Vert);

            Espace(p, 4);

            // Solde (orange si reste à payer, vert si soldé)
            LigneMontant(p, "RESTE À PAYER", Fcfa(Math.Max(0m, solde)),
                solde <= 0m ? Vert : Orange, FontWeights.Bold, taille: 12);

            Espace(p, 6);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);

            if (!string.IsNullOrWhiteSpace(pied))
            {
                Ligne(p, pied, 9, TextAlignment.Center, Gris);
                Espace(p, 4);
            }

            // ── Mentions finales ─────────────────────────────────────────
            Ligne(p, SEP, 8, TextAlignment.Center, Gris);
            Ligne(p, "Signature du client : ______________", 10, TextAlignment.Left, Noir);
            Espace(p, 6);
            Ligne(p, "Présentez ce bon au retrait.", 10, TextAlignment.Center, Noir, FontWeights.Bold);
            Ligne(p, SEP, 8, TextAlignment.Center, Gris);
            Espace(p, 10);
        }

        // ═════════════════════════════════════════════════════════════════
        // IMPRESSION
        // ═════════════════════════════════════════════════════════════════

        private void BtnImprimer_Click(object sender, RoutedEventArgs e)
        {
            var visibilite = TicketsCommandeLogique.VisibiliteSelonRole(_roleOperateur);
            bool roleAutoriseBon = visibilite.OngletBonClientVisible;
            if (!roleAutoriseBon && _mode != ModeImpression.EtiquetteSeule)
            {
                _mode = ModeImpression.EtiquetteSeule;
                _btnImprimer.Content = $"🖨️  Imprimer : {TicketsCommandeLogique.LibelleMode(_mode)}";
            }
            bool imprimerEtiquettes = _mode == ModeImpression.EtiquetteSeule
                                   || _mode == ModeImpression.EtiquetteEtBonClient;
            bool imprimerBon        = _mode == ModeImpression.BonClientSeul
                                   || _mode == ModeImpression.EtiquetteEtBonClient;
            if (!roleAutoriseBon)
            {
                imprimerBon = false;
                imprimerEtiquettes = true;
            }

            try
            {
                // ── 1. Construire la liste des pages pour l'aperçu ────────
                var pages = new List<(StackPanel Panel, string Description)>();

                if (imprimerEtiquettes)
                {
                    for (int i = 0; i < _panelsEtiquette.Count; i++)
                    {
                        string typeTicket = _panelsEtiquette.Count == 1
                            ? "Étiquette colis"
                            : $"Étiquette {i + 1}/{_panelsEtiquette.Count}";
                        pages.Add((_panelsEtiquette[i],
                            $"Commande CMD #{_commande.IdCommande} — {typeTicket}"));
                    }
                }

                if (imprimerBon)
                {
                    pages.Add((_panelBonClient,
                        $"Commande CMD #{_commande.IdCommande} — Bon client"));
                }

                if (pages.Count == 0) return;

                // ── 2. Afficher l'aperçu avant impression ─────────────────
                string libelleMode = TicketsCommandeLogique.LibelleMode(_mode);
                var apercu = new ApercuImprimerieWindow(pages,
                    $"Aperçu — Commande CMD #{_commande.IdCommande} ({libelleMode})");
                apercu.Owner = this;
                apercu.ShowDialog();

                if (!apercu.LancerImpression) return;

                // ── 3. Dialogue d'impression Windows ──────────────────────
                var pd = new PrintDialog();
                if (pd.ShowDialog() != true) return;

                // ── Étiquette(s) ──────────────────────────────────────────
                if (imprimerEtiquettes)
                {
                    for (int i = 0; i < _panelsEtiquette.Count; i++)
                    {
                        string typeTicket = _panelsEtiquette.Count == 1
                            ? "Étiquette colis"
                            : $"Étiquette {i + 1}/{_panelsEtiquette.Count}";
                        ImprimerPanel(pd, _panelsEtiquette[i],
                            $"Commande CMD #{_commande.IdCommande} — {typeTicket}");
                        AuditerImpressionSansAttendre(typeTicket);
                    }
                }

                // ── Bon client ────────────────────────────────────────────
                if (imprimerBon)
                {
                    ImprimerPanel(pd, _panelBonClient,
                        $"Commande CMD #{_commande.IdCommande} — Bon client");
                    AuditerImpressionSansAttendre("Bon client");
                }

                // ── Marquer première impression ───────────────────────────
                _commandeService.MarquerRecuImprime(_commande.IdCommande);

                BasculerApercu();
                MessageBox.Show(
                    $"Impression réussie ({libelleMode}).",
                    "Impression", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur d'impression :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void ImprimerPanel(PrintDialog pd, StackPanel panel, string description)
        {
            // Imprimer une copie visuelle afin de ne pas modifier la mise en page de l'aperçu.
            var copie = CopierPanel(panel);
            copie.Measure(new Size(290, double.PositiveInfinity));
            copie.Arrange(new Rect(new Point(0, 0), copie.DesiredSize));
            pd.PrintVisual(copie, description);
        }

        private void AuditerImpressionSansAttendre(string typeTicket)
        {
            // La tâche observe et journalise ses erreurs, sans retarder l'impression.
            _ = AuditerImpressionAsync(typeTicket);
        }

        private async Task AuditerImpressionAsync(string typeTicket)
        {
            try
            {
                await _auditService.EnregistrerActionAsync(
                    _idOperateur, _nomOperateur, _roleOperateur,
                    "COMMANDE_IMPRESSION", "Commande", _commande.IdCommande,
                    null,
                    new
                    {
                        TypeTicket = typeTicket,
                        Mode = TicketsCommandeLogique.LibelleMode(_mode),
                        UneEtiquetteParPiece = _cbUneParPiece.IsChecked == true,
                        Date = DateTime.Now
                    });
            }
            catch (Exception ex)
            {
                try { _logService?.LogError($"Échec de l'audit du ticket '{typeTicket}' pour la commande {_commande.IdCommande}.", ex, _nomOperateur); }
                catch { /* Une panne du journal ne doit pas interrompre l'impression. */ }
            }
        }

        private static StackPanel CopierPanel(StackPanel source)
        {
            var copie = new StackPanel { Width = source.Width, Margin = source.Margin };
            foreach (UIElement enfant in source.Children)
            {
                switch (enfant)
                {
                    case TextBlock texte:
                        copie.Children.Add(new TextBlock
                        {
                            Text = texte.Text, FontFamily = texte.FontFamily, FontSize = texte.FontSize,
                            FontWeight = texte.FontWeight, TextAlignment = texte.TextAlignment,
                            Foreground = texte.Foreground, TextWrapping = texte.TextWrapping,
                            Margin = texte.Margin, Padding = texte.Padding
                        });
                        break;
                    case Border bordure:
                        copie.Children.Add(new Border
                        {
                            Height = bordure.Height, Width = bordure.Width, Margin = bordure.Margin,
                            Background = bordure.Background, BorderBrush = bordure.BorderBrush,
                            BorderThickness = bordure.BorderThickness
                        });
                        break;
                    case System.Windows.Controls.Image image:
                        copie.Children.Add(new System.Windows.Controls.Image
                        {
                            Source = image.Source, Width = image.Width, Height = image.Height,
                            Stretch = image.Stretch, Margin = image.Margin,
                            HorizontalAlignment = image.HorizontalAlignment
                        });
                        break;
                    default:
                        throw new InvalidOperationException($"Élément d'impression non pris en charge : {enfant.GetType().Name}.");
                }
            }
            return copie;
        }

        // ═════════════════════════════════════════════════════════════════
        // HELPERS VISUELS (même API que FenetreRecuRetour / FicheAtelierWindow)
        // ═════════════════════════════════════════════════════════════════

        private string NomClient() =>
            _commande.Client != null
                ? (_commande.Client.Prenom + " " + _commande.Client.Nom).Trim()
                : "—";

        private static string Fcfa(decimal montant)
            => TicketsCommandeLogique.FormaterMontant(montant) + " FCFA";

        private string TexteRetrait(string prefixe)
        {
            string date = _commande.DateFin.ToString("dd/MM/yyyy");
            return _commande.HeureFin.HasValue
                ? $"{prefixe} : {date} à {_commande.HeureFin.Value:hh\\:mm}"
                : $"{prefixe} : {date}";
        }

        private void Ligne(StackPanel p, string texte,
            double taille = 10,
            TextAlignment align = TextAlignment.Left,
            Brush? couleur = null,
            FontWeight? poids = null)
        {
            p.Children.Add(new TextBlock
            {
                Text          = texte,
                FontFamily    = new FontFamily("Consolas"),
                FontSize      = taille,
                FontWeight    = poids ?? FontWeights.Normal,
                TextAlignment = align,
                Foreground    = couleur ?? Noir,
                TextWrapping  = TextWrapping.Wrap
            });
        }

        private void Espace(StackPanel p, double hauteur)
            => p.Children.Add(new Border { Height = hauteur });

        /// <summary>Ligne "Label ......... : Valeur" pour l'étiquette.</summary>
        private void LigneInfo(StackPanel p, string label, string valeur)
        {
            const int LARGEUR = 12;
            string lbl = label.Length >= LARGEUR
                ? label
                : label + new string(' ', LARGEUR - label.Length);
            Ligne(p, lbl + " : " + valeur, 10, TextAlignment.Left, Noir);
        }

        /// <summary>Ligne "  Label..........: Valeur" avec pointillés pour le Bon client.</summary>
        private void LignePad(StackPanel p, string label, string valeur, Brush? couleur = null)
        {
            int pts = Math.Max(2, 14 - label.Length);
            string ligne = "  " + label + new string('.', pts) + ": " + valeur;
            Ligne(p, ligne, 10, TextAlignment.Left, couleur ?? Noir);
        }

        /// <summary>Ligne montant avec label aligné à gauche et valeur à droite.</summary>
        private void LigneMontant(StackPanel p, string label, string valeur,
            Brush couleur, FontWeight? poids = null, double taille = 10)
        {
            // Pad total = 32 chars (largeur ticket Consolas 10pt ≈ 32 chars)
            const int TOTAL = 32;
            int espaces = Math.Max(1, TOTAL - label.Length - valeur.Length);
            string ligne = "  " + label + new string(' ', espaces) + valeur;
            Ligne(p, ligne, taille, TextAlignment.Left, couleur, poids);
        }

        private static RadioButton CréerRadio(string texte, bool isChecked)
            => new()
            {
                Content           = texte,
                IsChecked         = isChecked,
                Foreground        = Brushes.White,
                FontSize          = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 14, 0)
            };

        private static Button CréerOnglet(string texte, bool actif)
            => new()
            {
                Content         = texte,
                Height          = 36,
                Padding         = new Thickness(20, 0, 20, 0),
                FontSize        = 12,
                FontWeight      = FontWeights.SemiBold,
                Foreground      = Brushes.White,
                Background      = new SolidColorBrush(actif
                    ? Color.FromRgb(0xD9, 0x77, 0x06)
                    : Color.FromRgb(0x1E, 0x29, 0x3B)),
                BorderThickness = new Thickness(0),
                Cursor          = System.Windows.Input.Cursors.Hand
            };

        private static Button CréerBouton(string texte, string couleurHex)
        {
            var c = (Color)ColorConverter.ConvertFromString(couleurHex);
            return new Button
            {
                Content         = texte,
                Width           = 150,
                Height          = 38,
                FontSize        = 12,
                FontWeight      = FontWeights.Bold,
                Foreground      = Brushes.White,
                Background      = new SolidColorBrush(c),
                BorderThickness = new Thickness(0),
                Cursor          = System.Windows.Input.Cursors.Hand,
                Margin          = new Thickness(8, 0, 8, 0)
            };
        }
    }
}
