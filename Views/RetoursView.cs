using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class RetoursView : Page
    {
        private readonly IRetourService _retourService;
        private readonly IParametresService _p;
        private readonly IWhatsAppService _whatsApp;
        private readonly ApplicationDbContext _context;
        private readonly Employe _utilisateur;
        private List<Commande> _commandes = new();
        private List<Retour> _tousLesRetours = new();
        private bool _initialise = false;  // guard anti-event prématuré

        public RetoursView()
        {
            InitializeComponent();

            _retourService = App.Services.GetRequiredService<IRetourService>();
            _p = App.Services.GetRequiredService<IParametresService>();
            _whatsApp = App.Services.GetRequiredService<IWhatsAppService>();
            var factory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = factory.CreateDbContext();
            Unloaded += (s, e) => _context.Dispose();

            var authService = App.Services.GetRequiredService<IAuthService>();
            _utilisateur = authService.UtilisateurConnecte
                ?? throw new InvalidOperationException("Aucun utilisateur connecté.");

            try
            {
                _commandes = _context.Commandes
                    .Include(c => c.Client)
                    .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                    .OrderByDescending(c => c.DateDebut)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RetoursView: erreur commandes: " + ex.Message);
                _commandes = new List<Commande>();
            }

            ChargerRetours();
            _initialise = true;
        }

        // ==================================================================
        // Chargement
        // ==================================================================
        private void ChargerRetours()
        {
            try
            {
                _tousLesRetours = _retourService.ObtenirTous();
            }
            catch
            {
                try
                {
                    var factory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                    using var ctx = factory.CreateDbContext();
                    _tousLesRetours = ctx.Retours
                        .Include(r => r.Commande).ThenInclude(c => c!.Client)
                        .Include(r => r.PieceCommande)
                        .Include(r => r.Couturier)
                        .OrderByDescending(r => r.DateSignalement)
                        .ToList();
                }
                catch { _tousLesRetours = new List<Retour>(); }
            }
            AppliquerFiltre();
            MettreAJourBadges();
            ChargerPerformanceQualite();
        }

        private void AppliquerFiltre()
        {
            if (TxtRecherche == null || CmbFiltreStatut == null || GridRetours == null) return;

            var liste = _tousLesRetours;

            string motCle = TxtRecherche.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(motCle))
            {
                liste = liste.Where(r =>
                    r.ClientAffiche.ToLower().Contains(motCle) ||
                    (r.PieceCommande?.TypeVetement.ToLower().Contains(motCle) ?? false) ||
                    r.DescriptionProbleme.ToLower().Contains(motCle) ||
                    (r.Couturier != null &&
                     (r.Couturier.Prenom + " " + r.Couturier.Nom).ToLower().Contains(motCle))
                ).ToList();
            }

            string statut = (CmbFiltreStatut.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tous";
            if (statut != "Tous")
            {
                var statutDb = statut switch
                {
                    "Signalé"         => "Signale",
                    "Prêt ✓"          => "Pret",
                    "Rendu au client" => "Rendu",
                    _                 => statut
                };
                liste = liste.Where(r => r.Statut == statutDb && !r.EstAnnule).ToList();
            }

            GridRetours.ItemsSource = null;
            GridRetours.ItemsSource = liste;
            TxtCompteur.Text = $"{liste.Count} retour(s) affiché(s) sur {_tousLesRetours.Count} total";
            MettreAJourEtatVide(liste.Count);
        }

        private void MettreAJourBadges()
        {
            if (TxtNbSignale == null) return;
            var actifs = _tousLesRetours.Where(r => !r.EstAnnule).ToList();
            TxtNbSignale.Text   = actifs.Count(r => r.Statut == "Signale").ToString();
            TxtNbEnReprise.Text = actifs.Count(r => r.Statut == "En reprise").ToString();
            TxtNbPret.Text      = actifs.Count(r => r.Statut == "Pret").ToString();
            TxtNbRendu.Text     = actifs.Count(r => r.Statut == "Rendu").ToString();
        }

        // ── Affichage de l'état vide selon le nombre de lignes dans le DataGrid ──
        private void MettreAJourEtatVide(int nbLignesAffichees)
        {
            if (PanneauVide == null || GridRetours == null) return;
            bool aucun = nbLignesAffichees == 0;
            PanneauVide.Visibility  = aucun ? Visibility.Visible  : Visibility.Collapsed;
            GridRetours.Visibility  = aucun ? Visibility.Collapsed : Visibility.Visible;
            if (TxtEtatVide != null)
            {
                TxtEtatVide.Text = string.IsNullOrEmpty(TxtRecherche?.Text?.Trim()) &&
                                   (CmbFiltreStatut?.SelectedIndex == 0)
                    ? "Aucun retour enregistré"
                    : "Aucun résultat pour cette recherche / ce filtre";
            }
        }

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialise) return;
            AppliquerFiltre();
        }

        private void CmbFiltreStatut_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialise) return;
            AppliquerFiltre();
        }

        private void CmbPeriodeQualite_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialise) return;
            ChargerPerformanceQualite();
        }

        // ==================================================================
        // Panneau Performance Qualité
        // ==================================================================
        private void ChargerPerformanceQualite()
        {
            // Guard : appelé avant fin d'InitializeComponent si IsSelected="True" dans le XAML
            if (CmbPeriodeQualite == null || _utilisateur == null) return;

            string tag = (CmbPeriodeQualite.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mois";
            DateTime dateDebut = tag switch
            {
                "trimestre" => new DateTime(DateTime.Today.Year,
                                  ((DateTime.Today.Month - 1) / 3) * 3 + 1, 1),
                "annee"     => new DateTime(DateTime.Today.Year, 1, 1),
                "tout"      => new DateTime(2000, 1, 1),
                _           => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
            };
            DateTime dateFin = DateTime.Today;

            try
            {
                var factory = App.Services.GetRequiredService<
                    Microsoft.EntityFrameworkCore.IDbContextFactory<
                        GestionCoutureApp.Data.ApplicationDbContext>>();
                using var ctx = factory.CreateDbContext();

                // Couturiers actifs
                var couturiers = ctx.Employes
                    .Where(e => e.Statut == "Actif" &&
                                (e.Role == "Couturier" || e.Role == "Boss"))
                    .ToList();

                if (couturiers.Count == 0)
                {
                    ListePerformance.ItemsSource = null;
                    TxtAucunePerformance.Visibility = Visibility.Visible;
                    return;
                }

                // Pièces terminées/livrées sur la période
                var pieces = ctx.PiecesCommande
                    .Include(p => p.Commande)
                    .Where(p => (p.Statut == "Terminee" || p.Statut == "Livree") &&
                                p.IdCouturier.HasValue &&
                                p.Commande != null &&
                                p.Commande.DateFin.Date >= dateDebut.Date &&
                                p.Commande.DateFin.Date <= dateFin.Date)
                    .ToList();

                // Retours sur la période (couturier initial responsable)
                var retours = ctx.Retours
                    .Where(r => !r.EstAnnule &&
                                r.DateSignalement.Date >= dateDebut.Date &&
                                r.DateSignalement.Date <= dateFin.Date)
                    .ToList();

                // Seuil prime : configurable (ici on lit depuis Parametres si dispo)
                decimal primeZeroDefaut = 5000m;
                var paramPrime = ctx.Parametres.Find("PrimeZeroDefaut");
                if (paramPrime != null && decimal.TryParse(paramPrime.Valeur, out decimal v))
                    primeZeroDefaut = v;

                var performances = couturiers
                    .Select((emp, idx) =>
                    {
                        int nbPieces  = pieces.Count(p => p.IdCouturier == emp.IdEmploye);
                        int nbRetours = retours.Count(r => r.IdCouturier == emp.IdEmploye);
                        double taux   = nbPieces > 0
                            ? Math.Round(100.0 * (nbPieces - nbRetours) / nbPieces, 1)
                            : 100.0;
                        // Éligible si : minimum 5 pièces ET qualité >= 95% (max 1 retour sur 20 pièces)
                        bool eligible = nbPieces >= 5 && taux >= 95.0;

                        return new PerformanceCouturier
                        {
                            IdCouturier    = emp.IdEmploye,
                            NomCouturier   = emp.Prenom + " " + emp.Nom,
                            NbPieces       = nbPieces,
                            NbRetours      = nbRetours,
                            TauxQualite    = taux,
                            EstEligiblePrime = eligible,
                            PrimeZeroDefaut  = primeZeroDefaut
                        };
                    })
                    .Where(p => p.NbPieces > 0)
                    .OrderByDescending(p => p.TauxQualite)
                    .ThenByDescending(p => p.NbPieces)
                    .ToList();

                // Attribuer les médailles
                for (int i = 0; i < performances.Count; i++)
                    performances[i].Rang = i + 1;

                ListePerformance.ItemsSource = performances;
                TxtAucunePerformance.Visibility =
                    performances.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PerformanceQualite erreur: " + ex.Message);
                TxtAucunePerformance.Visibility = Visibility.Visible;
            }
        }

        // ==================================================================
        // Handlers tableau
        // ==================================================================
        private void BtnNouveauRetour_Click(object sender, RoutedEventArgs e)
            => OuvrirFenetreRetour(null);

        private void BtnEditerRetour_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Retour retour)
                OuvrirFenetreRetour(retour);
        }

        private void BtnAvancerStatut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Retour retour) return;

            string prochainStatut = retour.Statut switch
            {
                "Signale"    => "En reprise",
                "En reprise" => "Prêt ✓",
                "Pret"       => "Rendu au client",
                _            => ""
            };
            if (string.IsNullOrEmpty(prochainStatut)) return;

            var r = MessageBox.Show($"Passer ce retour à : « {prochainStatut} » ?",
                "Avancer le statut", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                string op = _utilisateur.Prenom + " " + _utilisateur.Nom;
                switch (retour.Statut)
                {
                    case "Signale":    _retourService.DemarrerReprise(retour.IdRetour, _utilisateur.IdEmploye, op); break;
                    case "En reprise": _retourService.Resoudre(retour.IdRetour, _utilisateur.IdEmploye, op); break;
                    case "Pret":       _retourService.MarquerRendu(retour.IdRetour, _utilisateur.IdEmploye, op); break;
                }
                ChargerRetours();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAnnulerRetour_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Retour retour) return;
            string? motif = DemanderMotif("Motif d'annulation obligatoire");
            if (string.IsNullOrWhiteSpace(motif)) return;
            try
            {
                _retourService.Annuler(retour.IdRetour, motif, _utilisateur.Prenom + " " + _utilisateur.Nom);
                ChargerRetours();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================================================================
        // Modale création / édition
        // ==================================================================
        public void OuvrirFenetreRetour(Retour? retourExistant)
        {
            bool modeEdition = retourExistant != null;

            var commandesEligibles = modeEdition
                ? _commandes
                : _commandes.Where(c =>
                    c.Pieces.Any(p => p.Statut == "Livree" || p.Statut == "Terminee")).ToList();

            if (!modeEdition && commandesEligibles.Count == 0)
            {
                MessageBox.Show(
                    "Aucune commande avec pièce livrée ou terminée disponible.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // ── Couleurs locales formulaire (charte Ambre & Ardoise) ──────
            var slateDark  = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)); // #0F172A
            var slateBody  = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)); // #334155
            var slateMuted = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)); // #64748B
            var amber      = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)); // #D97706
            var amberHover = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09)); // #B45309
            var amberLight = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)); // #FEF3C7
            var borderClr  = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)); // #E2E8F0
            var footerBg   = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)); // #F8FAFC
            var whiteBrush = Brushes.White;

            // ── Fenêtre transparente avec Border arrondie ─────────────────
            var fenetre = new Window
            {
                Title = modeEdition
                    ? $"Retour #{retourExistant!.IdRetour} — Détails & Modification"
                    : "🔄  Nouveau retour — Reprise gratuite",
                Width            = 560,          // réduit de 620 → 560
                MaxHeight        = 700,          // réduit de 860 → 700
                WindowStyle      = WindowStyle.None,
                AllowsTransparency = true,
                Background       = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode       = ResizeMode.NoResize,
                SizeToContent    = SizeToContent.Height,
                Owner            = Window.GetWindow(this)
            };

            // Fermer avec Echap
            fenetre.KeyDown += (s, ev) =>
            {
                if (ev.Key == System.Windows.Input.Key.Escape)
                    fenetre.Close();
            };

            // Enveloppe arrondie principale
            var enveloppe = new Border
            {
                CornerRadius    = new CornerRadius(14),
                Background      = Brushes.White,
                BorderBrush     = borderClr,
                BorderThickness = new Thickness(1),
                ClipToBounds    = true,
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color     = Color.FromRgb(0, 0, 0),
                    Opacity   = 0.18,
                    BlurRadius = 24,
                    ShadowDepth = 4
                }
            };

            // Grid interne : en-tête | corps scrollable | pied
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── EN-TÊTE ardoise ───────────────────────────────────────────
            var headerBorder = new Border
            {
                Background   = slateDark,
                Padding      = new Thickness(20, 14, 16, 14),
                CornerRadius = new CornerRadius(14, 14, 0, 0)
            };
            headerBorder.MouseLeftButtonDown += (s, ev) =>
            {
                if (ev.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    fenetre.DragMove();
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Pastille icône 🔄
            var headerIcon = new Border
            {
                Background    = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                CornerRadius  = new CornerRadius(8),
                Width = 36, Height = 36
            };
            headerIcon.Child = new TextBlock
            {
                Text = "🔄", FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(headerIcon, 0);

            // Titre de la modale
            var headerTitle = new TextBlock
            {
                Text = modeEdition
                    ? $"Retour #{retourExistant!.IdRetour} — Modification"
                    : "Nouveau retour — Reprise gratuite",
                FontSize   = 15, FontWeight = FontWeights.Bold,
                Foreground = whiteBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerTitle, 2);

            // Bouton ✕ fermer (sans valider)
            var btnFermerEntete = new Button
            {
                Content      = "✕",
                Width        = 30, Height = 30,
                FontSize     = 14,
                Foreground   = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                Background   = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor       = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip      = "Fermer sans enregistrer (Échap)"
            };
            btnFermerEntete.Click += (s, ev) => fenetre.Close();
            Grid.SetColumn(btnFermerEntete, 3);

            headerGrid.Children.Add(headerIcon);
            headerGrid.Children.Add(headerTitle);
            headerGrid.Children.Add(btnFermerEntete);
            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);

            // ── CORPS SCROLLABLE ──────────────────────────────────────────
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 560      // réduit de 620 → 560 pour tenir dans 700px total
            };
            Grid.SetRow(scroll, 1);

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 6) }; // marges réduites

            // ══════════════════════════════════════════════════════════════
            // SECTION 1 — PIÈCE CONCERNÉE
            // ══════════════════════════════════════════════════════════════
            AjouterSectionTitre(root, "1.  Pièce concernée");

            AjouterLabel(root, "Client *");
            var cmbCommande = new ComboBox
            {
                Height = 32, FontSize = 12,       // hauteur réduite 36→32, police 13→12
                Margin = new Thickness(0, 0, 0, 8),
                IsEnabled = !modeEdition
            };
            cmbCommande.ItemsSource = commandesEligibles.Select(c => new
            {
                c.IdCommande,
                DisplayText = $"CMD-{c.IdCommande}  —  {c.Client?.Nom} {c.Client?.Prenom}  ({c.DateDebut:dd/MM/yy})"
            }).ToList();
            cmbCommande.DisplayMemberPath = "DisplayText";
            cmbCommande.SelectedValuePath = "IdCommande";
            root.Children.Add(cmbCommande);

            AjouterLabel(root, "Pièce concernée *");
            var cmbPiece = new ComboBox
            {
                Height = 32, FontSize = 12,       // hauteur réduite
                Margin = new Thickness(0, 0, 0, 8),
                IsEnabled = !modeEdition
            };
            cmbPiece.DisplayMemberPath = "DisplayText";
            cmbPiece.SelectedValuePath = "IdPieceCommande";
            root.Children.Add(cmbPiece);

            // Couturier initial
            AjouterLabel(root, "Couturier initial (responsable)");
            var txtCouturierInitial = new TextBox
            {
                IsReadOnly = true, Height = 30, FontSize = 12,  // compacté
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                Text = "— (sélectionnez une pièce)",
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(8, 0, 8, 0)
            };
            root.Children.Add(txtCouturierInitial);

            var txtMontantOrigine = new TextBlock
            {
                FontSize = 10, FontStyle = FontStyles.Italic,   // police réduite
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(txtMontantOrigine);

            // ── Photo de la pièce (affichage automatique) — hauteur réduite ─
            AjouterLabel(root, "📸  Photo de la pièce d'origine");
            var borderPhotoPiece = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF5, 0xF3)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE0, 0xDC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Height = 100,                    // réduit de 160 → 100
                Margin = new Thickness(0, 0, 0, 10),
                ClipToBounds = true
            };
            var imgPhotoPiece = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(6)
            };
            var txtPhotoPiecePlaceholder = new TextBlock
            {
                Text = "Aucune photo — sélectionnez une pièce",
                FontSize = 12, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var gridPhotoPiece = new Grid();
            gridPhotoPiece.Children.Add(imgPhotoPiece);
            gridPhotoPiece.Children.Add(txtPhotoPiecePlaceholder);
            borderPhotoPiece.Child = gridPhotoPiece;
            root.Children.Add(borderPhotoPiece);

            int? idCouturierInitial = null;

            // Peupler les pièces + photo automatique
            void ChargerPieces()
            {
                cmbPiece.ItemsSource = null;
                idCouturierInitial = null;
                txtCouturierInitial.Text = "— (sélectionnez une pièce)";
                txtMontantOrigine.Text = "";
                imgPhotoPiece.Source = null;
                txtPhotoPiecePlaceholder.Visibility = Visibility.Visible;

                if (cmbCommande.SelectedValue == null) return;
                int idCmd = (int)cmbCommande.SelectedValue;
                var cmd = commandesEligibles.FirstOrDefault(c => c.IdCommande == idCmd);
                if (cmd == null) return;

                var pieces = modeEdition
                    ? cmd.Pieces.ToList()
                    : cmd.Pieces.Where(p => p.Statut == "Livree" || p.Statut == "Terminee").ToList();

                cmbPiece.ItemsSource = pieces.Select(p => new
                {
                    p.IdPieceCommande,
                    DisplayText = $"{p.TypeVetement}  —  {p.Couturier?.Prenom} {p.Couturier?.Nom}  ({p.MontantCouture:N0} FCFA)  [{p.StatutAffiche}]"
                }).ToList();
            }

            void AfficherPhotoPiece(PieceCommande? piece)
            {
                imgPhotoPiece.Source = null;
                txtPhotoPiecePlaceholder.Visibility = Visibility.Visible;
                if (piece == null || string.IsNullOrEmpty(piece.CheminPhoto)) return;
                if (!System.IO.File.Exists(piece.CheminPhoto)) return;
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(piece.CheminPhoto, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    imgPhotoPiece.Source = bmp;
                    txtPhotoPiecePlaceholder.Visibility = Visibility.Collapsed;
                }
                catch { /* photo corrompue ou inaccessible */ }
            }

            cmbCommande.SelectionChanged += (s, ev) => ChargerPieces();

            cmbPiece.SelectionChanged += (s, ev) =>
            {
                idCouturierInitial = null;
                txtCouturierInitial.Text = "—";
                imgPhotoPiece.Source = null;
                txtPhotoPiecePlaceholder.Visibility = Visibility.Visible;

                if (cmbPiece.SelectedValue == null || cmbCommande.SelectedValue == null) return;
                int idCmd = (int)cmbCommande.SelectedValue;
                int idPiece = (int)cmbPiece.SelectedValue;
                var cmd = commandesEligibles.FirstOrDefault(c => c.IdCommande == idCmd);
                var piece = cmd?.Pieces.FirstOrDefault(p => p.IdPieceCommande == idPiece);
                if (piece == null) return;

                idCouturierInitial = piece.IdCouturier;
                txtCouturierInitial.Text = piece.Couturier != null
                    ? $"{piece.Couturier.Prenom} {piece.Couturier.Nom}"
                    : "Non assigné";
                txtMontantOrigine.Text =
                    $"Montant d'origine : {piece.MontantCouture:N0} FCFA  —  Reprise : 0 FCFA (garantie)";

                AfficherPhotoPiece(piece);
            };

            // Pré-remplir en mode édition
            if (modeEdition && retourExistant != null)
            {
                cmbCommande.SelectedValue = retourExistant.IdCommande;
                ChargerPieces();
                cmbPiece.SelectedValue = retourExistant.IdPieceCommande;
                idCouturierInitial = retourExistant.IdCouturier;
                if (retourExistant.Couturier != null)
                    txtCouturierInitial.Text = $"{retourExistant.Couturier.Prenom} {retourExistant.Couturier.Nom}";
                txtMontantOrigine.Text =
                    $"Montant d'origine : {retourExistant.PieceCommande?.MontantCouture:N0} FCFA  —  Reprise : 0 FCFA (garantie)";
                // Afficher la photo de la pièce en édition
                if (retourExistant.PieceCommande != null)
                    AfficherPhotoPiece(retourExistant.PieceCommande);
            }

            // ══════════════════════════════════════════════════════════════
            // SECTION 2 — MOTIF + PHOTO DU DÉFAUT
            // ══════════════════════════════════════════════════════════════
            AjouterSectionTitre(root, "2.  Motif du retour");

            AjouterLabel(root, "Description du problème *");
            var txtDescription = new TextBox
            {
                Height = 60, FontSize = 12,       // réduit 80→60, police 13→12
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 6, 8, 6),
                Text = modeEdition ? retourExistant!.DescriptionProbleme : ""
            };
            root.Children.Add(txtDescription);

            // ── Photo du défaut — WEBCAM + IMPORT ─────────────────────────
            AjouterLabel(root, "📷  Photo du défaut (optionnel — webcam recommandée)");

            string cheminPhotoDefaut = modeEdition ? retourExistant!.CheminPhotoDefaut ?? "" : "";

            // Prévisualisation photo défaut — hauteur réduite
            var borderPhotoDefaut = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Height = 100,                    // réduit de 160 → 100
                Margin = new Thickness(0, 0, 0, 6),
                ClipToBounds = true,
                Visibility = Visibility.Collapsed
            };
            var imgPhotoDefaut = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4)
            };
            borderPhotoDefaut.Child = imgPhotoDefaut;
            root.Children.Add(borderPhotoDefaut);

            // Charger la photo défaut existante (mode édition)
            if (!string.IsNullOrEmpty(cheminPhotoDefaut) && System.IO.File.Exists(cheminPhotoDefaut))
            {
                try
                {
                    var bmpDef = new BitmapImage();
                    bmpDef.BeginInit();
                    bmpDef.UriSource = new Uri(cheminPhotoDefaut, UriKind.Absolute);
                    bmpDef.CacheOption = BitmapCacheOption.OnLoad;
                    bmpDef.EndInit();
                    bmpDef.Freeze();
                    imgPhotoDefaut.Source = bmpDef;
                    borderPhotoDefaut.Visibility = Visibility.Visible;
                }
                catch { }
            }

            // Boutons photo défaut
            var panelBtnsPhoto = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)   // réduit de 16 → 10
            };

            // Bouton Webcam (prioritaire)
            var btnWebcam = new Button
            {
                Content = "📷  Webcam",
                Height = 32, Padding = new Thickness(12, 0, 12, 0),  // réduit 36→32
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };

            // Bouton Importer
            var btnImporter = new Button
            {
                Content = "📁  Importer",
                Height = 32, Padding = new Thickness(12, 0, 12, 0),  // réduit
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };

            // Label nom fichier
            var lblPhotoDefaut = new TextBlock
            {
                Text = string.IsNullOrEmpty(cheminPhotoDefaut)
                    ? "Aucune photo — utilisez la webcam ou importez"
                    : "✔  " + System.IO.Path.GetFileName(cheminPhotoDefaut),
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(
                    string.IsNullOrEmpty(cheminPhotoDefaut)
                        ? Color.FromRgb(0x9C, 0xA3, 0xAF)
                        : Color.FromRgb(0x05, 0x96, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Helper : mettre à jour la prévisualisation après capture/import
            void MettreAJourPhotoDefaut(string chemin)
            {
                cheminPhotoDefaut = chemin;
                lblPhotoDefaut.Text = "✔  " + System.IO.Path.GetFileName(chemin);
                lblPhotoDefaut.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(chemin, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    imgPhotoDefaut.Source = bmp;
                    borderPhotoDefaut.Visibility = Visibility.Visible;
                }
                catch { }
            }

            btnWebcam.Click += (s, ev) =>
            {
                try
                {
                    var webcam = new WebcamCaptureWindow();
                    webcam.Owner = fenetre;
                    if (webcam.ShowDialog() == true &&
                        !string.IsNullOrEmpty(webcam.CapturedFilePath))
                    {
                        MettreAJourPhotoDefaut(webcam.CapturedFilePath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur webcam : " + ex.Message,
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            btnImporter.Click += (s, ev) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Sélectionner une photo du défaut",
                    Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp"
                };
                if (dlg.ShowDialog() != true) return;
                try
                {
                    // Copier dans le dossier photos pour cohérence
                    string dossierPhotos = GestionCoutureApp.Helpers.AppPaths.DossierPhotos;
                    string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    string suffixe = Guid.NewGuid().ToString("N")[..8];
                    string dest = System.IO.Path.Combine(dossierPhotos,
                        $"defaut_{DateTime.Now:yyyyMMdd_HHmmss}_{suffixe}{ext}");
                    System.IO.File.Copy(dlg.FileName, dest, overwrite: true);
                    // Compression JPEG 1024×768 / 70% à la source
                    GestionCoutureApp.Helpers.PhotoCompressor.Compresser(dest, dest);
                    MettreAJourPhotoDefaut(dest);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur import : " + ex.Message,
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            panelBtnsPhoto.Children.Add(btnWebcam);
            panelBtnsPhoto.Children.Add(btnImporter);
            panelBtnsPhoto.Children.Add(lblPhotoDefaut);
            root.Children.Add(panelBtnsPhoto);

            // ══════════════════════════════════════════════════════════════
            // SECTION 3 — ATTRIBUTION & RDV
            // ══════════════════════════════════════════════════════════════
            AjouterSectionTitre(root, "3.  Attribution & Rendez-vous de reprise");

            AjouterLabel(root, "Couturier pour la reprise");
            var employes = _context.Employes.Where(e => e.Statut == "Actif").ToList();
            var cmbCouturierReprise = new ComboBox
            {
                Height = 32, FontSize = 12,       // compacté
                Margin = new Thickness(0, 0, 0, 8)
            };
            cmbCouturierReprise.Items.Add(new ComboBoxItem
            {
                Content = "— Même couturier que l'initial —",
                Tag = -1
            });
            foreach (var emp in employes)
            {
                cmbCouturierReprise.Items.Add(new ComboBoxItem
                {
                    Content = $"{emp.Prenom} {emp.Nom}  [{emp.Role}]",
                    Tag = emp.IdEmploye
                });
            }
            cmbCouturierReprise.SelectedIndex = 0;

            if (modeEdition && retourExistant!.IdCouturierReprise.HasValue)
            {
                for (int i = 1; i < cmbCouturierReprise.Items.Count; i++)
                {
                    if ((int)((ComboBoxItem)cmbCouturierReprise.Items[i]).Tag
                        == retourExistant.IdCouturierReprise.Value)
                    { cmbCouturierReprise.SelectedIndex = i; break; }
                }
            }
            root.Children.Add(cmbCouturierReprise);

            // Ligne RDV : Date + H début + H fin — marges réduites
            var gridRdv = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });

            TextBlock LblRdv(string t) => new()
            {
                Text = t, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), // ardoise
                Margin = new Thickness(0, 0, 0, 3)
            };

            TextBox TxtHeure(string valeur) => new()
            {
                Height = 30, FontSize = 12,       // compacté 36→30
                Padding = new Thickness(8, 0, 8, 0),
                Text = valeur
            };

            // Calcul RDV par défaut : maintenant + 24h, contraint à 08h-22h
            DateTime dateRdvDef;
            TimeSpan heureRdvDef;
            {
                var candidat = DateTime.Now.AddHours(24);
                var h = candidat.TimeOfDay;
                if (h >= new TimeSpan(22, 0, 0))
                { dateRdvDef = candidat.Date.AddDays(1); heureRdvDef = new TimeSpan(8, 0, 0); }
                else if (h < new TimeSpan(8, 0, 0))
                { dateRdvDef = candidat.Date; heureRdvDef = new TimeSpan(8, 0, 0); }
                else
                {
                    int min = ((h.Minutes / 30) + 1) * 30;
                    heureRdvDef = min >= 60
                        ? new TimeSpan(h.Hours + 1, 0, 0)
                        : new TimeSpan(h.Hours, min, 0);
                    dateRdvDef = candidat.Date;
                }
            }

            var spDate = new StackPanel();
            spDate.Children.Add(LblRdv("Date de rendez-vous"));
            var dpRdv = new DatePicker
            {
                Height = 36, FontSize = 13,
                SelectedDate = modeEdition
                    ? retourExistant!.DateRdvReprise
                    : dateRdvDef   // +24h par défaut en création
            };
            spDate.Children.Add(dpRdv);
            Grid.SetColumn(spDate, 0);

            var spHdeb = new StackPanel();
            spHdeb.Children.Add(LblRdv("Heure début"));
            var txtHdeb = TxtHeure(modeEdition && retourExistant!.HeureDebutReprise.HasValue
                ? retourExistant.HeureDebutReprise.Value.ToString(@"hh\:mm")
                : heureRdvDef.ToString(@"hh\:mm"));  // +24h par défaut en création
            spHdeb.Children.Add(txtHdeb);
            Grid.SetColumn(spHdeb, 2);

            var spHfin = new StackPanel();
            spHfin.Children.Add(LblRdv("Heure fin"));
            var txtHfin = TxtHeure(modeEdition && retourExistant!.HeureFinReprise.HasValue
                ? retourExistant.HeureFinReprise.Value.ToString(@"hh\:mm") : "");
            spHfin.Children.Add(txtHfin);
            Grid.SetColumn(spHfin, 4);

            gridRdv.Children.Add(spDate);
            gridRdv.Children.Add(spHdeb);
            gridRdv.Children.Add(spHfin);
            root.Children.Add(gridRdv);

            // ══════════════════════════════════════════════════════════════
            // SECTION 4 — FACTURATION
            // ══════════════════════════════════════════════════════════════
            root.Children.Add(new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(12, 8, 12, 8),    // réduit de 14,10
                Margin          = new Thickness(0, 2, 0, 8),       // réduit de 0,4,0,12
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "🔄  Reprise sous garantie — ",
                            FontSize = 13, FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)), // amber-800
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "0 FCFA  (Gratuite)",
                            FontSize = 14, FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)), // amber-600
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            });

            // ── Message erreur ────────────────────────────────────────────
            var lblErreur = new TextBlock
            {
                FontSize   = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Height     = 18,
                Margin     = new Thickness(0, 0, 0, 0)
            };
            root.Children.Add(lblErreur);

            // ── Assemblage corps scrollable ───────────────────────────────
            scroll.Content = root;
            Grid.SetRow(scroll, 1);

            // ── PIED DE PAGE gris clair ───────────────────────────────────
            var footer = new Border
            {
                Background      = footerBg,
                BorderBrush     = borderClr,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding         = new Thickness(18, 10, 18, 12),
                CornerRadius    = new CornerRadius(0, 0, 14, 14)
            };

            var panelBtns = new StackPanel
            {
                Orientation          = Orientation.Horizontal,
                HorizontalAlignment  = HorizontalAlignment.Right
            };

            // Bouton Annuler
            var btnAnnuler = new Button
            {
                Content         = "✕  Annuler",
                Width           = 100, Height = 36,  // compacté 110,40 → 100,36
                FontSize        = 12,
                Foreground      = slateBody,
                Background      = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush     = borderClr,
                Cursor          = System.Windows.Input.Cursors.Hand,
                Margin          = new Thickness(0, 0, 8, 0)
            };
            btnAnnuler.Click += (s, ev) => fenetre.Close();

            // Bouton Enregistrer
            var btnEnregistrer = new Button
            {
                Content         = modeEdition ? "💾  Enregistrer les modifications" : "💾  Enregistrer la Reprise",
                Height          = 36,                // compacté 40 → 36
                Padding         = new Thickness(16, 0, 16, 0),
                FontSize        = 12, FontWeight = FontWeights.Bold,
                Foreground      = Brushes.White,
                Background      = amber,
                BorderThickness = new Thickness(0),
                Cursor          = System.Windows.Input.Cursors.Hand
            };

            btnEnregistrer.Click += (s, ev) =>
            {
                lblErreur.Text = "";

                if (!modeEdition && cmbCommande.SelectedValue == null)
                { lblErreur.Text = "Sélectionnez une commande."; return; }
                if (!modeEdition && cmbPiece.SelectedValue == null)
                { lblErreur.Text = "Sélectionnez une pièce."; return; }
                if (string.IsNullOrWhiteSpace(txtDescription.Text))
                { lblErreur.Text = "La description du problème est obligatoire."; return; }
                if (!modeEdition && idCouturierInitial == null)
                { lblErreur.Text = "La pièce n'a pas de couturier assigné."; return; }

                int? idCouturierReprise = null;
                if (cmbCouturierReprise.SelectedItem is ComboBoxItem ci && (int)ci.Tag != -1)
                    idCouturierReprise = (int)ci.Tag;

                TimeSpan? hDeb = TimeSpan.TryParse(txtHdeb.Text, out var h1) ? h1 : null;
                TimeSpan? hFin = TimeSpan.TryParse(txtHfin.Text, out var h2) ? h2 : null;

                try
                {
                    if (modeEdition)
                    {
                        retourExistant!.DescriptionProbleme = txtDescription.Text.Trim();
                        retourExistant.IdCouturierReprise   = idCouturierReprise;
                        retourExistant.DateRdvReprise       = dpRdv.SelectedDate;
                        retourExistant.HeureDebutReprise    = hDeb;
                        retourExistant.HeureFinReprise      = hFin;
                        if (!string.IsNullOrEmpty(cheminPhotoDefaut))
                            retourExistant.CheminPhotoDefaut = cheminPhotoDefaut;
                        _retourService.Modifier(retourExistant);
                        MessageBox.Show("Retour mis à jour avec succès !", "Succès",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        var retour = new Retour
                        {
                            IdCommande              = (int)cmbCommande.SelectedValue,
                            IdPieceCommande         = (int)cmbPiece.SelectedValue,
                            IdCouturier             = idCouturierInitial!.Value,
                            IdCouturierReprise      = idCouturierReprise,
                            DescriptionProbleme     = txtDescription.Text.Trim(),
                            CheminPhotoDefaut       = string.IsNullOrEmpty(cheminPhotoDefaut) ? null : cheminPhotoDefaut,
                            DateRdvReprise          = dpRdv.SelectedDate,
                            HeureDebutReprise       = hDeb,
                            HeureFinReprise         = hFin,
                            Statut                  = "Signale",
                            IdOperateurEnregistrement  = _utilisateur.IdEmploye,
                            NomOperateurEnregistrement = _utilisateur.Prenom + " " + _utilisateur.Nom
                        };
                        _retourService.Ajouter(retour);
                        MessageBox.Show("Retour enregistré avec succès !", "Succès",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    ChargerRetours();
                    fenetre.DialogResult = true;
                    fenetre.Close();
                }
                catch (Exception ex)
                {
                    lblErreur.Text = ex.Message;
                }
            };

            panelBtns.Children.Add(btnAnnuler);
            panelBtns.Children.Add(btnEnregistrer);
            footer.Child = panelBtns;
            Grid.SetRow(footer, 2);

            // ── Assemblage final ──────────────────────────────────────────
            mainGrid.Children.Add(headerBorder);
            mainGrid.Children.Add(scroll);
            mainGrid.Children.Add(footer);
            enveloppe.Child = mainGrid;
            fenetre.Content = enveloppe;
            fenetre.ShowDialog();
        }

        // ==================================================================
        // Helpers UI — charte Ambre & Ardoise
        // (utilisés uniquement dans OuvrirFenetreRetour de cette classe)
        // ==================================================================
        private static void AjouterSectionTitre(StackPanel parent, string titre)
        {
            // Ligne ambre + texte ardoise
            var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 12) };
            panel.Children.Add(new TextBlock
            {
                Text       = titre,
                FontSize   = 11, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), // slateDark
                Margin     = new Thickness(0, 0, 0, 5)
            });
            panel.Children.Add(new Border
            {
                Height              = 2,
                CornerRadius        = new CornerRadius(1),
                Background          = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)), // amber
                HorizontalAlignment = HorizontalAlignment.Left,
                Width               = 36
            });
            parent.Children.Add(panel);
        }

        private static void AjouterLabel(StackPanel parent, string texte)
        {
            parent.Children.Add(new TextBlock
            {
                Text       = texte,
                FontSize   = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), // slateDark
                Margin     = new Thickness(0, 0, 0, 4)
            });
        }

        // ==================================================================
        // WhatsApp — retouche prête (bouton vert dans le tableau)
        // ==================================================================
        private async void BtnWhatsAppRetour_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Retour retour) return;

            string tel = retour.Commande?.Client?.Telephone ?? "";
            if (string.IsNullOrWhiteSpace(tel))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone.",
                    "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Utiliser le modèle "Retouche prête" configuré dans Paramètres
                string modele    = await _p.ObtenirMsgRetouchePrete();
                string nomAtelier = await _p.ObtenirNomAtelier();

                string nomClient = retour.ClientAffiche;
                string rdvDate   = retour.DateRdvReprise.HasValue
                    ? retour.DateRdvReprise.Value.ToString("dd/MM/yyyy")
                    : "—";
                string rdvHeure  = retour.HeureDebutReprise.HasValue
                    ? retour.HeureDebutReprise.Value.ToString(@"hh\:mm")
                    : "—";

                string message = modele
                    .Replace("{Nom}",      $"*{nomClient}*")
                    .Replace("{Commande}", retour.IdCommande.ToString())
                    .Replace("{Pieces}",   retour.PieceCommande?.TypeVetement ?? "vêtement")
                    .Replace("{Atelier}",  nomAtelier)
                    .Replace("{Date}",     $"*{rdvDate}*")
                    .Replace("{Heure}",    $"*{rdvHeure}*");

                _whatsApp.OuvrirConversation(tel, message);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir WhatsApp :\n" + ex.Message,
                    "Erreur WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string? DemanderMotif(string titre)
        {
            // ── Couleurs locales (charte Ambre & Ardoise) ─────────────────
            var slateDark  = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
            var slateBody  = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
            var borderClr  = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            var footerBg   = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            var amber      = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));

            string? resultat = null;

            var dlg = new Window
            {
                Title                  = titre,
                Width                  = 440,
                WindowStyle            = WindowStyle.None,
                AllowsTransparency     = true,
                Background             = Brushes.Transparent,
                WindowStartupLocation  = WindowStartupLocation.CenterOwner,
                ResizeMode             = ResizeMode.NoResize,
                SizeToContent          = SizeToContent.Height,
                Owner                  = Window.GetWindow(this)
            };
            dlg.KeyDown += (s, ev) =>
            {
                if (ev.Key == System.Windows.Input.Key.Escape)
                    dlg.Close();
            };

            // Enveloppe arrondie
            var env = new Border
            {
                CornerRadius    = new CornerRadius(12),
                Background      = Brushes.White,
                BorderBrush     = borderClr,
                BorderThickness = new Thickness(1),
                ClipToBounds    = true,
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0, 0, 0), Opacity = 0.15,
                    BlurRadius = 20, ShadowDepth = 3
                }
            };

            var mainG = new Grid();
            mainG.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainG.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainG.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // En-tête ardoise
            var hdr = new Border
            {
                Background    = slateDark,
                Padding       = new Thickness(18, 12, 14, 12),
                CornerRadius  = new CornerRadius(12, 12, 0, 0)
            };
            hdr.MouseLeftButtonDown += (s, ev) =>
            {
                if (ev.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    dlg.DragMove();
            };
            var hdrRow = new Grid();
            hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hdrTitle = new TextBlock
            {
                Text = "🚫  " + titre,
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hdrTitle, 0);
            var btnX = new Button
            {
                Content = "✕", Width = 28, Height = 28, FontSize = 13,
                Foreground = Brushes.White, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnX.Click += (s, ev) => { dlg.DialogResult = false; dlg.Close(); };
            Grid.SetColumn(btnX, 1);
            hdrRow.Children.Add(hdrTitle);
            hdrRow.Children.Add(btnX);
            hdr.Child = hdrRow;
            Grid.SetRow(hdr, 0);

            // Corps
            var sp = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };

            sp.Children.Add(new TextBlock
            {
                Text = "Motif :", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = slateDark, Margin = new Thickness(0, 0, 0, 6)
            });
            var txt = new TextBox
            {
                Height                    = 72, FontSize = 13,
                TextWrapping              = TextWrapping.Wrap,
                AcceptsReturn             = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin                    = new Thickness(0, 0, 0, 8),
                Padding                   = new Thickness(10, 8, 10, 8),
                BorderBrush               = borderClr,
                BorderThickness           = new Thickness(1)
            };
            sp.Children.Add(txt);

            var err = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Height = 16, Margin = new Thickness(0, 0, 0, 4)
            };
            sp.Children.Add(err);
            Grid.SetRow(sp, 1);

            // Pied
            var foot = new Border
            {
                Background      = footerBg,
                BorderBrush     = borderClr,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding         = new Thickness(18, 10, 18, 12),
                CornerRadius    = new CornerRadius(0, 0, 12, 12)
            };
            var footRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var btnCancel = new Button
            {
                Content = "Annuler", Width = 90, Height = 38,
                FontSize = 13, Foreground = slateBody,
                Background = Brushes.White,
                BorderBrush = borderClr, BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0)
            };
            btnCancel.Click += (s, ev) => { dlg.DialogResult = false; dlg.Close(); };

            var btnOk = new Button
            {
                Content = "Confirmer", Width = 110, Height = 38,
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Background = amber,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnOk.Click += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(txt.Text))
                { err.Text = "Le motif est obligatoire."; return; }
                resultat = txt.Text.Trim();
                dlg.DialogResult = true;
                dlg.Close();
            };

            footRow.Children.Add(btnCancel);
            footRow.Children.Add(btnOk);
            foot.Child = footRow;
            Grid.SetRow(foot, 2);

            mainG.Children.Add(hdr);
            mainG.Children.Add(sp);
            mainG.Children.Add(foot);
            env.Child = mainG;
            dlg.Content = env;
            dlg.ShowDialog();
            return resultat;
        }
    }

    // DTO pour le panneau Performance Qualité
    public class PerformanceCouturier
    {
        public int IdCouturier     { get; set; }
        public string NomCouturier { get; set; } = string.Empty;
        public int NbPieces        { get; set; }
        public int NbRetours       { get; set; }
        public double TauxQualite  { get; set; }
        public int Rang            { get; set; }
        public bool EstEligiblePrime { get; set; }
        public decimal PrimeZeroDefaut { get; set; }

        // Propriétés calculées pour le binding XAML
        public string NbPiecesAffiche  => NbPieces.ToString();
        public string NbRetoursAffiche => NbRetours == 0 ? "✓ 0" : NbRetours.ToString();
        public string TauxQualiteAffiche => TauxQualite.ToString("0.#") + "%";
        public bool EstExcellent => TauxQualite >= 98;
        public bool EstFaible    => TauxQualite < 85;

        public string Medaille => Rang switch
        {
            1 => "🥇",
            2 => "🥈",
            3 => "🥉",
            _ => $"#{Rang}"
        };

        public string BadgePrime => EstEligiblePrime
            ? $"🎁 Éligible Prime +{PrimeZeroDefaut:N0} FCFA"
            : NbRetours == 0 && NbPieces < 5
                ? "⚠️ < 5 pièces (pas encore éligible)"
                : $"❌ {NbRetours} retour(s) — non éligible";
    }
}
