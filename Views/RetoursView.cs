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
        // ── Services ──────────────────────────────────────────────────────
        private readonly IRetourService        _retourService;
        private readonly IParametresService    _p;
        private readonly IWhatsAppService      _whatsApp;
        private readonly IMesureClientService  _mesureService;
        private readonly IReceiptService       _receiptService;
        private readonly IAuditService         _auditService;
        private readonly ApplicationDbContext  _context;
        private readonly Employe               _utilisateur;

        // ── Données ───────────────────────────────────────────────────────
        private List<Commande> _commandes    = new();
        private List<Retour>   _tousLesRetours = new();
        private bool           _initialise   = false;

        // ── Pagination ────────────────────────────────────────────────────
        private const int PAGE_SIZE = 20;
        private int  _page      = 1;
        private int  _total     = 0;

        // ── Filtres date ──────────────────────────────────────────────────
        private DateTime? _filtreDate1;
        private DateTime? _filtreDate2;

        // ═════════════════════════════════════════════════════════════════
        // CONSTRUCTEUR
        // ═════════════════════════════════════════════════════════════════
        public RetoursView()
        {
            InitializeComponent();

            _retourService = App.Services.GetRequiredService<IRetourService>();
            _p             = App.Services.GetRequiredService<IParametresService>();
            _whatsApp      = App.Services.GetRequiredService<IWhatsAppService>();
            _mesureService = App.Services.GetRequiredService<IMesureClientService>();
            _receiptService = App.Services.GetRequiredService<IReceiptService>();
            _auditService  = App.Services.GetRequiredService<IAuditService>();

            var factory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = factory.CreateDbContext();
            Unloaded += (_, _) => _context.Dispose();

            var auth = App.Services.GetRequiredService<IAuthService>();
            _utilisateur = auth.UtilisateurConnecte
                ?? throw new InvalidOperationException("Aucun utilisateur connecté.");

            try
            {
                _commandes = _context.Commandes
                    .Include(c => c.Client)
                    .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                    .Include(c => c.Paiements)
                    .Where(c => !c.EstSupprimee)
                    .OrderByDescending(c => c.DateDebut)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RetoursView commandes: " + ex.Message);
            }

            ChargerRetours();
            _initialise = true;
        }

        // ═════════════════════════════════════════════════════════════════
        // CHARGEMENT / PAGINATION
        // ═════════════════════════════════════════════════════════════════
        private void ChargerRetours() => _ = ChargerRetoursAsync();

        private async Task ChargerRetoursAsync()
        {
            if (!_initialise && TxtRecherche == null) return;
            try
            {
                string? motCle = TxtRecherche?.Text?.Trim();
                if (string.IsNullOrEmpty(motCle)) motCle = null;

                string? statut = (CmbFiltreStatut?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (statut == "Tous") statut = null;

                var result = await _retourService.ObtenirPageAsync(
                    _page, PAGE_SIZE, motCle, statut, _filtreDate1, _filtreDate2);

                _tousLesRetours = result.Items;
                _total          = result.TotalCount;

                GridRetours.ItemsSource = null;
                GridRetours.ItemsSource = _tousLesRetours;

                int start = _total == 0 ? 0 : (_page - 1) * PAGE_SIZE + 1;
                int end   = Math.Min(_page * PAGE_SIZE, _total);
                string info = _total == 0
                    ? "Aucun retour"
                    : $"{start}–{end} / {_total} retour{(_total > 1 ? "s" : "")}";
                if (TxtCompteur        != null) TxtCompteur.Text        = info;
                if (TxtPaginationRetours != null) TxtPaginationRetours.Text = info;

                if (BtnRetourPagePrec != null) BtnRetourPagePrec.IsEnabled = _page > 1;
                if (BtnRetourPageSuiv != null) BtnRetourPageSuiv.IsEnabled = end < _total;

                MettreAJourEtatVide(_tousLesRetours.Count);
                MettreAJourBadges();
            }
            catch (Exception ex)
            {
                try
                {
                    _tousLesRetours = _retourService.ObtenirTous();
                    GridRetours.ItemsSource = _tousLesRetours;
                }
                catch { _tousLesRetours = new(); }
                System.Diagnostics.Debug.WriteLine("RetoursView chargement: " + ex.Message);
            }
            ChargerPerformanceQualite();
        }

        private void AppliquerFiltre() { _page = 1; _ = ChargerRetoursAsync(); }

        private void MettreAJourBadges()
        {
            if (TxtNbSignale == null) return;
            var actifs = _tousLesRetours.Where(r => !r.EstAnnule).ToList();
            TxtNbSignale.Text   = actifs.Count(r => r.Statut == "Signale").ToString();
            TxtNbEnReprise.Text = actifs.Count(r => r.Statut == "En reprise").ToString();
            // "Terminé" couvre l'ancienne valeur "Pret" et la nouvelle "Termine"
            TxtNbPret.Text      = actifs.Count(r => r.EstTermine).ToString();
            TxtNbRendu.Text     = actifs.Count(r => r.Statut == "Rendu").ToString();
        }

        private void MettreAJourEtatVide(int n)
        {
            if (PanneauVide == null || GridRetours == null) return;
            bool vide = n == 0;
            PanneauVide.Visibility = vide ? Visibility.Visible : Visibility.Collapsed;
            GridRetours.Visibility = vide ? Visibility.Collapsed : Visibility.Visible;
            if (TxtEtatVide != null)
                TxtEtatVide.Text = string.IsNullOrEmpty(TxtRecherche?.Text?.Trim()) &&
                                   CmbFiltreStatut?.SelectedIndex == 0
                    ? "Aucun retour enregistré"
                    : "Aucun résultat pour ce filtre";
        }

        // ── Pagination ────────────────────────────────────────────────────
        private async void BtnRetourPagePrec_Click(object s, RoutedEventArgs e)
        { if (_page > 1) { _page--; await ChargerRetoursAsync(); } }

        private async void BtnRetourPageSuiv_Click(object s, RoutedEventArgs e)
        { _page++; await ChargerRetoursAsync(); }

        // ── Filtres ───────────────────────────────────────────────────────
        private async void BtnFiltreAujourdhui_Click(object s, RoutedEventArgs e)
        {
            bool actif = _filtreDate1 == DateTime.Today && _filtreDate2 == DateTime.Today;
            _filtreDate1 = actif ? null : DateTime.Today;
            _filtreDate2 = actif ? null : DateTime.Today;
            if (DpFiltreDebut != null) DpFiltreDebut.SelectedDate = _filtreDate1;
            if (DpFiltreFin   != null) DpFiltreFin.SelectedDate   = _filtreDate2;
            if (TxtBtnAujourdhui    != null) TxtBtnAujourdhui.Text    = actif ? "Aujourd'hui" : "✓ Aujourd'hui";
            if (BtnFiltreAujourdhui != null) BtnFiltreAujourdhui.Opacity = actif ? 1.0 : 0.75;
            if (BtnEffacerDate      != null) BtnEffacerDate.Visibility   = actif ? Visibility.Collapsed : Visibility.Visible;
            _page = 1; await ChargerRetoursAsync();
        }

        private async void FiltreDate_Changed(object s, SelectionChangedEventArgs e)
        {
            if (!_initialise) return;
            _filtreDate1 = DpFiltreDebut?.SelectedDate;
            _filtreDate2 = DpFiltreFin?.SelectedDate;
            if (TxtBtnAujourdhui    != null) TxtBtnAujourdhui.Text    = "Aujourd'hui";
            if (BtnFiltreAujourdhui != null) BtnFiltreAujourdhui.Opacity = 1.0;
            if (BtnEffacerDate      != null)
                BtnEffacerDate.Visibility = (_filtreDate1.HasValue || _filtreDate2.HasValue)
                    ? Visibility.Visible : Visibility.Collapsed;
            _page = 1; await ChargerRetoursAsync();
        }

        private async void BtnEffacerDate_Click(object s, RoutedEventArgs e)
        {
            _filtreDate1 = null; _filtreDate2 = null;
            if (DpFiltreDebut != null) DpFiltreDebut.SelectedDate = null;
            if (DpFiltreFin   != null) DpFiltreFin.SelectedDate   = null;
            if (TxtBtnAujourdhui    != null) TxtBtnAujourdhui.Text    = "Aujourd'hui";
            if (BtnFiltreAujourdhui != null) BtnFiltreAujourdhui.Opacity = 1.0;
            if (BtnEffacerDate      != null) BtnEffacerDate.Visibility   = Visibility.Collapsed;
            _page = 1; await ChargerRetoursAsync();
        }

        private void TxtRecherche_TextChanged(object s, TextChangedEventArgs e)
        { if (_initialise) AppliquerFiltre(); }

        private void CmbFiltreStatut_SelectionChanged(object s, SelectionChangedEventArgs e)
        { if (_initialise) AppliquerFiltre(); }

        private void CmbPeriodeQualite_SelectionChanged(object s, SelectionChangedEventArgs e)
        { if (_initialise) ChargerPerformanceQualite(); }

        // ═════════════════════════════════════════════════════════════════
        // PANNEAU PERFORMANCE QUALITÉ
        // ═════════════════════════════════════════════════════════════════
        private void ChargerPerformanceQualite()
        {
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

            try
            {
                var factory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                using var ctx = factory.CreateDbContext();

                var couturiers = ctx.Employes
                    .Where(e => e.Statut == "Actif" && (e.Role == "Couturier" || e.Role == "Boss"))
                    .ToList();

                if (couturiers.Count == 0) { ListePerformance.ItemsSource = null; TxtAucunePerformance.Visibility = Visibility.Visible; return; }

                // Pièces livrées sur la période
                var pieces = ctx.PiecesCommande
                    .Include(p => p.Commande)
                    .Where(p => (p.Statut == "Terminee" || p.Statut == "Livree") &&
                                p.IdCouturier.HasValue &&
                                p.Commande != null && p.Commande.DateFin.Date >= dateDebut.Date &&
                                p.Commande.DateFin.Date <= DateTime.Today)
                    .ToList();

                // Retours terminés (DateResolution non null) = comptent dans les indicateurs
                var retours = ctx.Retours
                    .Where(r => !r.EstAnnule && r.DateResolution.HasValue &&
                                r.DateResolution.Value.Date >= dateDebut.Date &&
                                r.DateResolution.Value.Date <= DateTime.Today)
                    .ToList();

                decimal primeZeroDefaut = 5000m;
                var paramPrime = ctx.Parametres.Find("PrimeZeroDefaut");
                if (paramPrime != null && decimal.TryParse(paramPrime.Valeur, out decimal pv))
                    primeZeroDefaut = pv;

                var perfs = couturiers
                    .Select((emp, _) =>
                    {
                        int nbPieces  = pieces.Count(p => p.IdCouturier == emp.IdEmploye);
                        int nbImputes = retours.Count(r => r.IdCouturier == emp.IdEmploye);
                        int nbReprises = retours.Count(r => r.IdCouturierRepriseEffectif == emp.IdEmploye);
                        double taux   = nbPieces > 0 ? Math.Round(100.0 * (nbPieces - nbImputes) / nbPieces, 1) : 100.0;
                        bool eligible = nbPieces >= 5 && taux >= 95.0;
                        return new PerformanceCouturier
                        {
                            IdCouturier      = emp.IdEmploye,
                            NomCouturier     = emp.Prenom + " " + emp.Nom,
                            NbPieces         = nbPieces,
                            NbRetours        = nbImputes,
                            NbReprises       = nbReprises,
                            TauxQualite      = taux,
                            EstEligiblePrime = eligible,
                            PrimeZeroDefaut  = primeZeroDefaut
                        };
                    })
                    .Where(p => p.NbPieces > 0)
                    .OrderByDescending(p => p.TauxQualite).ThenByDescending(p => p.NbPieces)
                    .ToList();

                for (int i = 0; i < perfs.Count; i++) perfs[i].Rang = i + 1;
                ListePerformance.ItemsSource = perfs;
                TxtAucunePerformance.Visibility = perfs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PerformanceQualite: " + ex.Message);
                TxtAucunePerformance.Visibility = Visibility.Visible;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ACTIONS TABLEAU
        // ═════════════════════════════════════════════════════════════════
        private void BtnNouveauRetour_Click(object s, RoutedEventArgs e) => OuvrirFenetreRetour(null);

        private void BtnEditerRetour_Click(object s, RoutedEventArgs e)
        {
            if (s is Button btn && btn.DataContext is Retour r) OuvrirFenetreRetour(r);
        }

        private void BtnAvancerStatut_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn || btn.DataContext is not Retour retour) return;

            string prochainStatut = retour.Statut switch
            {
                "Signale"    => "En reprise",
                "En reprise" => "Terminé",
                "Termine"    => "Rendu au client",
                "Pret"       => "Rendu au client",   // compat.
                _            => ""
            };
            if (string.IsNullOrEmpty(prochainStatut)) return;

            if (MessageBox.Show($"Passer ce retour à : « {prochainStatut} » ?",
                    "Avancer le statut", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;

            try
            {
                string op = _utilisateur.Prenom + " " + _utilisateur.Nom;
                switch (retour.Statut)
                {
                    case "Signale":               _retourService.DemarrerReprise(retour.IdRetour, _utilisateur.IdEmploye, op); break;
                    case "En reprise":            _retourService.Resoudre(retour.IdRetour, _utilisateur.IdEmploye, op);       break;
                    case "Pret": case "Termine":  _retourService.MarquerRendu(retour.IdRetour, _utilisateur.IdEmploye, op);   break;
                }
                ChargerRetours();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnReculerStatut_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn || btn.DataContext is not Retour retour) return;

            if (_utilisateur.Role != "Boss")
            {
                MessageBox.Show("Seul le Boss peut effectuer un retour arrière de statut.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? motif = DemanderMotif("Motif du retour arrière (obligatoire)");
            if (string.IsNullOrWhiteSpace(motif)) return;

            try
            {
                _retourService.ReculerStatut(
                    retour.IdRetour, motif,
                    _utilisateur.IdEmploye, _utilisateur.Prenom + " " + _utilisateur.Nom,
                    _utilisateur.Role);
                ChargerRetours();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAnnulerRetour_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn || btn.DataContext is not Retour retour) return;

            if (_utilisateur.Role != "Boss")
            {
                MessageBox.Show("Seul le Boss peut annuler un retour.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? motif = DemanderMotif("Motif d'annulation obligatoire");
            if (string.IsNullOrWhiteSpace(motif)) return;

            try
            {
                _retourService.Annuler(retour.IdRetour, motif, _utilisateur.Prenom + " " + _utilisateur.Nom);

                _ = _auditService.EnregistrerActionAsync(
                    _utilisateur.IdEmploye, _utilisateur.Prenom + " " + _utilisateur.Nom, _utilisateur.Role,
                    "RETOUR_ANNULE", "Retour", retour.IdRetour,
                    new { retour.Statut, retour.NumeroRetour },
                    new { Annule = true, Motif = motif },
                    motif: motif);

                ChargerRetours();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Impression tickets ────────────────────────────────────────────
        private void BtnImprimerRetour_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn || btn.DataContext is not Retour retour) return;

            // Recharger avec toutes les navigations nécessaires
            var complet = _retourService.ObtenirParId(retour.IdRetour);
            if (complet == null) { MessageBox.Show("Retour introuvable."); return; }

            var fenetre = new FenetreRecuRetour(
                complet, _retourService, _receiptService,
                _utilisateur.IdEmploye,
                _utilisateur.Prenom + " " + _utilisateur.Nom,
                _utilisateur.Role)
            {
                Owner = Window.GetWindow(this)
            };
            fenetre.ShowDialog();
        }

        // ── WhatsApp ──────────────────────────────────────────────────────
        private async void BtnWhatsAppRetour_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn || btn.DataContext is not Retour retour) return;

            string tel = retour.Commande?.Client?.Telephone ?? "";
            if (string.IsNullOrWhiteSpace(tel))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone.",
                    "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }

            try
            {
                string nomClient = retour.Commande?.Client != null
                    ? retour.Commande.Client.Prenom + " " + retour.Commande.Client.Nom : "";
                string typeVet   = retour.PieceCommande?.TypeVetement ?? "";
                string nomAtelier = (await _receiptService.GetReceiptInfoAsync())?.NomAtelier ?? "l'atelier";

                string rdv = retour.DateRdvReprise.HasValue
                    ? retour.DateRdvReprise.Value.ToString("dd/MM/yyyy") +
                      (retour.HeureDebutReprise.HasValue ? " à " + retour.HeureDebutReprise.Value.ToString(@"hh\:mm") : "")
                    : "un rendez-vous à convenir";

                string message =
                    $"Bonjour {nomClient} 👋\n" +
                    $"Votre {typeVet} a été repris avec succès.\n" +
                    $"Numéro de retour : *{retour.NumeroRetour}*\n" +
                    $"Vous pouvez venir le récupérer : *{rdv}*\n" +
                    $"Merci de votre confiance — {nomAtelier} ✂️";

                _whatsApp.OuvrirConversation(tel, message);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur WhatsApp :\n" + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // MODALE CRÉATION / ÉDITION
        // ═════════════════════════════════════════════════════════════════
        public void OuvrirFenetreRetour(Retour? retourExistant)
        {
            bool modeEdition = retourExistant != null;

            var commandesEligibles = modeEdition
                ? _commandes
                : _commandes.Where(c => c.Pieces.Any(p => p.Statut == "Livree" || p.Statut == "Terminee")).ToList();

            if (!modeEdition && commandesEligibles.Count == 0)
            {
                MessageBox.Show("Aucune commande avec pièce livrée disponible.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information); return;
            }

            // ── Couleurs charte Ambre & Ardoise ───────────────────────────
            var slateDark  = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
            var slateBody  = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
            var slateMuted = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            var amber      = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
            var borderClr  = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            var footerBg   = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

            var fenetre = new Window
            {
                Title = modeEdition ? $"Retour {retourExistant!.NumeroRetour}" : "🔄  Nouveau retour",
                Width = 560, MaxHeight = 760,
                WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height,
                Owner = Window.GetWindow(this)
            };
            fenetre.KeyDown += (_, ev) => { if (ev.Key == Key.Escape) fenetre.Close(); };

            var enveloppe = new Border
            {
                CornerRadius = new CornerRadius(14), Background = Brushes.White,
                BorderBrush = borderClr, BorderThickness = new Thickness(1), ClipToBounds = true,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, Opacity = 0.18, BlurRadius = 24, ShadowDepth = 4 }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── En-tête ───────────────────────────────────────────────────
            var header = new Border
            {
                Background = slateDark, Padding = new Thickness(20, 14, 16, 14),
                CornerRadius = new CornerRadius(14, 14, 0, 0)
            };
            header.MouseLeftButtonDown += (_, ev) => { if (ev.ButtonState == MouseButtonState.Pressed) fenetre.DragMove(); };

            var hGrid = new Grid();
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hIcon = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(8), Width = 36, Height = 36
            };
            hIcon.Child = new TextBlock { Text = "🔄", FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hIcon, 0);

            var hTitle = new TextBlock
            {
                Text = modeEdition ? $"Retour {retourExistant!.NumeroRetour}" : "Nouveau retour — Reprise gratuite",
                FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hTitle, 2);

            var btnClose = new Button
            {
                Content = "✕", Width = 30, Height = 30, FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center
            };
            btnClose.Click += (_, _) => fenetre.Close();
            Grid.SetColumn(btnClose, 3);

            hGrid.Children.Add(hIcon); hGrid.Children.Add(hTitle); hGrid.Children.Add(btnClose);
            header.Child = hGrid;
            Grid.SetRow(header, 0);

            // ── Corps ─────────────────────────────────────────────────────
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 600
            };
            Grid.SetRow(scroll, 1);

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 6) };

            // ══════════════════════════════════════════════════════════════
            // SECTION 1 — PIÈCE
            // ══════════════════════════════════════════════════════════════
            AjouterSectionTitre(root, "1.  Pièce concernée");

            AjouterLabel(root, "Client *");
            var cmbCommande = new ComboBox { Height = 32, FontSize = 12, Margin = new Thickness(0, 0, 0, 8), IsEnabled = !modeEdition };
            cmbCommande.ItemsSource    = commandesEligibles.Select(c => new { c.IdCommande, DisplayText = $"CMD-{c.IdCommande}  —  {c.Client?.Nom} {c.Client?.Prenom}  ({c.DateDebut:dd/MM/yy})" }).ToList();
            cmbCommande.DisplayMemberPath = "DisplayText"; cmbCommande.SelectedValuePath = "IdCommande";
            root.Children.Add(cmbCommande);

            AjouterLabel(root, "Pièce concernée *");
            var cmbPiece = new ComboBox { Height = 32, FontSize = 12, Margin = new Thickness(0, 0, 0, 8), IsEnabled = !modeEdition };
            cmbPiece.DisplayMemberPath = "DisplayText"; cmbPiece.SelectedValuePath = "IdPieceCommande";
            root.Children.Add(cmbPiece);

            AjouterLabel(root, "Couturier initial (responsable)");
            var txtCouturierInitial = new TextBox
            {
                IsReadOnly = true, Height = 30, FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                Foreground = slateMuted, Text = "— (sélectionnez une pièce)",
                Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(8, 0, 8, 0)
            };
            root.Children.Add(txtCouturierInitial);

            var txtMontantOrigine = new TextBlock
            {
                FontSize = 10, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(txtMontantOrigine);

            // Photo de la pièce d'origine
            var borderPhotoPiece = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF5, 0xF3)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE0, 0xDC)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Height = 100, Margin = new Thickness(0, 0, 0, 10), ClipToBounds = true
            };
            var imgPhotoPiece = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(6) };
            var txtPhotoPlaceholder = new TextBlock
            {
                Text = "Aucune photo — sélectionnez une pièce", FontSize = 12, FontStyle = FontStyles.Italic,
                Foreground = slateMuted, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var gridPhoto = new Grid();
            gridPhoto.Children.Add(imgPhotoPiece); gridPhoto.Children.Add(txtPhotoPlaceholder);
            borderPhotoPiece.Child = gridPhoto;
            root.Children.Add(borderPhotoPiece);

            int? idCouturierInitial = null;

            // ── Section Mesures client (affichées après sélection de la pièce) ──
            AjouterSectionTitre(root, "📐  Mesures à jour du client");
            var panneauMesures = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF9, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xBA, 0xE6, 0xFD)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10)
            };
            var spMesures = new StackPanel();
            var txtMesuresInfo = new TextBlock
            {
                Text = "Sélectionnez une pièce pour afficher les mesures du client.",
                FontSize = 10, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))
            };
            spMesures.Children.Add(txtMesuresInfo);
            panneauMesures.Child = spMesures;
            root.Children.Add(panneauMesures);

            // Dictionnaire des TextBox de mesures (NomMesure → TextBox) pour lecture/écriture
            var txtsMesures = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
            int? idClientCourant = null;
            string? typeVetementCourant = null;

            // Peupler les pièces + mesures automatiquement
            void ChargerPieces()
            {
                cmbPiece.ItemsSource = null; idCouturierInitial = null;
                txtCouturierInitial.Text = "— (sélectionnez une pièce)";
                txtMontantOrigine.Text = ""; imgPhotoPiece.Source = null;
                txtPhotoPlaceholder.Visibility = Visibility.Visible;
                spMesures.Children.Clear(); txtsMesures.Clear();
                txtMesuresInfo.Text = "Sélectionnez une pièce pour afficher les mesures du client.";
                spMesures.Children.Add(txtMesuresInfo);
                idClientCourant = null; typeVetementCourant = null;

                if (cmbCommande.SelectedValue == null) return;
                int idCmd = (int)cmbCommande.SelectedValue;
                var cmd = commandesEligibles.FirstOrDefault(c => c.IdCommande == idCmd);
                if (cmd == null) return;

                var piecesDispos = modeEdition
                    ? cmd.Pieces.ToList()
                    : cmd.Pieces.Where(p => p.Statut == "Livree" || p.Statut == "Terminee").ToList();

                cmbPiece.ItemsSource = piecesDispos.Select(p => new
                {
                    p.IdPieceCommande,
                    DisplayText = $"{p.TypeVetement}  —  {p.Couturier?.Prenom} {p.Couturier?.Nom}  ({p.MontantCouture:N0} FCFA)  [{p.StatutAffiche}]"
                }).ToList();
            }

            void AfficherMesuresClient(int idClient, string typeVetement)
            {
                idClientCourant = idClient; typeVetementCourant = typeVetement;
                spMesures.Children.Clear(); txtsMesures.Clear();

                var mesures = _mesureService.ObtenirMesuresPourType(idClient, typeVetement);

                if (mesures.Count == 0)
                {
                    spMesures.Children.Add(new TextBlock
                    {
                        Text = $"Aucune mesure enregistrée pour ce client ({typeVetement}).",
                        FontSize = 10, FontStyle = FontStyles.Italic,
                        Foreground = slateMuted
                    });
                    return;
                }

                var dateMaj = mesures.Max(m => m.DateMiseAJour);
                spMesures.Children.Add(new TextBlock
                {
                    Text = $"📐 Mesures {typeVetement} — dernière MAJ : {dateMaj:dd/MM/yyyy}",
                    FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0E, 0x49, 0x6B)),
                    Margin = new Thickness(0, 0, 0, 6)
                });

                // Grille 2 colonnes pour les mesures
                var gridM = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                gridM.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                gridM.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                gridM.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                int row = 0;
                foreach (var m in mesures)
                {
                    gridM.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    int col = row % 2 == 0 ? 0 : 2;
                    if (col == 0 && row > 0) { /* nouvelle ligne */ }

                    var spM = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                    spM.Children.Add(new TextBlock { Text = m.NomMesure, FontSize = 9, Foreground = slateMuted });
                    var txtM = new TextBox
                    {
                        Text = m.Valeur, Height = 26, FontSize = 11,
                        Padding = new Thickness(6, 0, 6, 0),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0xBA, 0xE6, 0xFD)),
                        BorderThickness = new Thickness(1), Background = Brushes.White
                    };
                    spM.Children.Add(txtM);
                    txtsMesures[m.NomMesure] = txtM;
                    Grid.SetColumn(spM, col);
                    Grid.SetRow(spM, row / 2);
                    gridM.Children.Add(spM);
                    row++;
                }
                spMesures.Children.Add(gridM);

                // Note explicative
                spMesures.Children.Add(new TextBlock
                {
                    Text = "ℹ️ Modifiez les mesures si nécessaire — elles seront sauvegardées dans la fiche client.",
                    FontSize = 9, FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0E, 0x49, 0x6B)),
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
                });
            }

            cmbCommande.SelectionChanged += (_, _) => ChargerPieces();

            cmbPiece.SelectionChanged += (_, ev) =>
            {
                idCouturierInitial = null;
                imgPhotoPiece.Source = null; txtPhotoPlaceholder.Visibility = Visibility.Visible;
                spMesures.Children.Clear(); txtsMesures.Clear();
                txtMesuresInfo.Text = "Sélectionnez une pièce pour afficher les mesures du client.";
                spMesures.Children.Add(txtMesuresInfo);

                if (cmbPiece.SelectedValue == null || cmbCommande.SelectedValue == null) return;
                int idCmd   = (int)cmbCommande.SelectedValue;
                int idPiece = (int)cmbPiece.SelectedValue;
                var cmd     = commandesEligibles.FirstOrDefault(c => c.IdCommande == idCmd);
                var piece   = cmd?.Pieces.FirstOrDefault(p => p.IdPieceCommande == idPiece);
                if (piece == null) return;

                idCouturierInitial = piece.IdCouturier;
                txtCouturierInitial.Text = piece.Couturier != null
                    ? $"{piece.Couturier.Prenom} {piece.Couturier.Nom}" : "Non assigné";
                txtMontantOrigine.Text =
                    $"Montant d'origine : {piece.MontantCouture:N0} FCFA  —  Reprise : 0 FCFA (garantie)";

                // Photo pièce
                if (!string.IsNullOrEmpty(piece.CheminPhoto) && System.IO.File.Exists(piece.CheminPhoto))
                {
                    try
                    {
                        var bmp = new BitmapImage(); bmp.BeginInit();
                        bmp.UriSource = new Uri(piece.CheminPhoto, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
                        imgPhotoPiece.Source = bmp; txtPhotoPlaceholder.Visibility = Visibility.Collapsed;
                    }
                    catch { }
                }

                // Mesures client
                if (cmd?.IdClient != null)
                    AfficherMesuresClient(cmd.IdClient, piece.TypeVetement);
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
                txtMontantOrigine.Text = $"Montant d'origine : {retourExistant.PieceCommande?.MontantCouture:N0} FCFA  —  Reprise : 0 FCFA (garantie)";
                if (retourExistant.PieceCommande?.CheminPhoto != null &&
                    System.IO.File.Exists(retourExistant.PieceCommande.CheminPhoto))
                {
                    try
                    {
                        var bmp = new BitmapImage(); bmp.BeginInit();
                        bmp.UriSource = new Uri(retourExistant.PieceCommande.CheminPhoto, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
                        imgPhotoPiece.Source = bmp; txtPhotoPlaceholder.Visibility = Visibility.Collapsed;
                    }
                    catch { }
                }
                // Mesures en mode édition
                if (retourExistant.Commande?.IdClient != null && retourExistant.PieceCommande?.TypeVetement != null)
                    AfficherMesuresClient(retourExistant.Commande.IdClient, retourExistant.PieceCommande.TypeVetement);
            }

            // ══════════════════════════════════════════════════════════════
            // SECTION 2 — MOTIF + PHOTO DU DÉFAUT
            // ══════════════════════════════════════════════════════════════
            AjouterSectionTitre(root, "2.  Motif du retour");

            AjouterLabel(root, "Description du problème *");
            var txtDescription = new TextBox
            {
                Height = 60, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6),
                Text = modeEdition ? retourExistant!.DescriptionProbleme : ""
            };
            root.Children.Add(txtDescription);

            // Photo défaut
            string cheminPhotoDefaut = modeEdition ? retourExistant!.CheminPhotoDefaut ?? "" : "";
            var borderPhotoDefaut = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Height = 100, Margin = new Thickness(0, 0, 0, 6), ClipToBounds = true,
                Visibility = Visibility.Collapsed
            };
            var imgPhotoDefaut = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(4) };
            borderPhotoDefaut.Child = imgPhotoDefaut;
            root.Children.Add(borderPhotoDefaut);

            if (!string.IsNullOrEmpty(cheminPhotoDefaut) && System.IO.File.Exists(cheminPhotoDefaut))
            {
                try
                {
                    var bmpDef = new BitmapImage(); bmpDef.BeginInit();
                    bmpDef.UriSource = new Uri(cheminPhotoDefaut, UriKind.Absolute);
                    bmpDef.CacheOption = BitmapCacheOption.OnLoad; bmpDef.EndInit(); bmpDef.Freeze();
                    imgPhotoDefaut.Source = bmpDef; borderPhotoDefaut.Visibility = Visibility.Visible;
                }
                catch { }
            }

            var panelBtnsPhoto = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var btnWebcam   = CréerBouton("📷  Webcam",   "#D97706", 32);
            var btnImporter = CréerBouton("📁  Importer", "#6B7280", 32);
            var lblFoto = new TextBlock
            {
                Text = string.IsNullOrEmpty(cheminPhotoDefaut) ? "Aucune photo" : "✔  " + System.IO.Path.GetFileName(cheminPhotoDefaut),
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(string.IsNullOrEmpty(cheminPhotoDefaut)
                    ? Color.FromRgb(0x9C, 0xA3, 0xAF) : Color.FromRgb(0x05, 0x96, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };

            void SetPhoto(string chemin)
            {
                cheminPhotoDefaut = chemin;
                lblFoto.Text = "✔  " + System.IO.Path.GetFileName(chemin);
                lblFoto.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
                try
                {
                    var bmp = new BitmapImage(); bmp.BeginInit();
                    bmp.UriSource = new Uri(chemin, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
                    imgPhotoDefaut.Source = bmp; borderPhotoDefaut.Visibility = Visibility.Visible;
                }
                catch { }
            }

            btnWebcam.Click += (_, _) =>
            {
                try
                {
                    var wc = new WebcamCaptureWindow(); wc.Owner = fenetre;
                    if (wc.ShowDialog() == true && !string.IsNullOrEmpty(wc.CapturedFilePath))
                        SetPhoto(wc.CapturedFilePath);
                }
                catch (Exception ex) { MessageBox.Show("Erreur webcam : " + ex.Message); }
            };
            btnImporter.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Photo du défaut", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" };
                if (dlg.ShowDialog() != true) return;
                try
                {
                    string dossier = Helpers.AppPaths.DossierPhotos;
                    string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    string dest = System.IO.Path.Combine(dossier, $"defaut_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N8}{ext}");
                    System.IO.File.Copy(dlg.FileName, dest, true);
                    Helpers.PhotoCompressor.Compresser(dest, dest);
                    SetPhoto(dest);
                }
                catch (Exception ex) { MessageBox.Show("Erreur import : " + ex.Message); }
            };

            panelBtnsPhoto.Children.Add(btnWebcam); panelBtnsPhoto.Children.Add(btnImporter);
            panelBtnsPhoto.Children.Add(lblFoto);
            root.Children.Add(panelBtnsPhoto);

            // ══════════════════════════════════════════════════════════════
            // SECTION 3 — ATTRIBUTION & RDV
            // ══════════════════════════════════════════════════════════════
            AjouterSectionTitre(root, "3.  Attribution & Rendez-vous");

            AjouterLabel(root, "Couturier pour la reprise");
            var employes = _context.Employes.Where(e => e.Statut == "Actif").ToList();
            var cmbCouturierReprise = new ComboBox { Height = 32, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
            cmbCouturierReprise.Items.Add(new ComboBoxItem { Content = "— Même couturier que l'initial —", Tag = -1 });
            foreach (var emp in employes)
                cmbCouturierReprise.Items.Add(new ComboBoxItem { Content = $"{emp.Prenom} {emp.Nom}  [{emp.Role}]", Tag = emp.IdEmploye });
            cmbCouturierReprise.SelectedIndex = 0;

            if (modeEdition && retourExistant!.IdCouturierReprise.HasValue)
            {
                for (int i = 1; i < cmbCouturierReprise.Items.Count; i++)
                    if ((int)((ComboBoxItem)cmbCouturierReprise.Items[i]).Tag == retourExistant.IdCouturierReprise.Value)
                    { cmbCouturierReprise.SelectedIndex = i; break; }
            }
            root.Children.Add(cmbCouturierReprise);

            // RDV
            DateTime dateRdvDef; TimeSpan heureRdvDef;
            {
                var c = DateTime.Now.AddHours(24); var h = c.TimeOfDay;
                if (h >= new TimeSpan(22, 0, 0)) { dateRdvDef = c.Date.AddDays(1); heureRdvDef = new TimeSpan(8, 0, 0); }
                else if (h < new TimeSpan(8, 0, 0)) { dateRdvDef = c.Date; heureRdvDef = new TimeSpan(8, 0, 0); }
                else
                {
                    int min = ((h.Minutes / 30) + 1) * 30;
                    heureRdvDef = min >= 60 ? new TimeSpan(h.Hours + 1, 0, 0) : new TimeSpan(h.Hours, min, 0);
                    dateRdvDef = c.Date;
                }
            }

            var gridRdv = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });

            TextBlock LblRdv(string t) => new() { Text = t, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = slateDark, Margin = new Thickness(0, 0, 0, 3) };
            TextBox TxtH(string v)     => new() { Height = 30, FontSize = 12, Padding = new Thickness(8, 0, 8, 0), Text = v };

            var spDate = new StackPanel(); spDate.Children.Add(LblRdv("Date RDV"));
            var dpRdv = new DatePicker { Height = 36, FontSize = 13, SelectedDate = modeEdition ? retourExistant!.DateRdvReprise : dateRdvDef };
            spDate.Children.Add(dpRdv); Grid.SetColumn(spDate, 0);

            var spHdeb = new StackPanel(); spHdeb.Children.Add(LblRdv("Heure début"));
            var txtHdeb = TxtH(modeEdition && retourExistant!.HeureDebutReprise.HasValue ? retourExistant.HeureDebutReprise.Value.ToString(@"hh\:mm") : heureRdvDef.ToString(@"hh\:mm"));
            spHdeb.Children.Add(txtHdeb); Grid.SetColumn(spHdeb, 2);

            var spHfin = new StackPanel(); spHfin.Children.Add(LblRdv("Heure fin"));
            var txtHfin = TxtH(modeEdition && retourExistant!.HeureFinReprise.HasValue ? retourExistant.HeureFinReprise.Value.ToString(@"hh\:mm") : "");
            spHfin.Children.Add(txtHfin); Grid.SetColumn(spHfin, 4);

            gridRdv.Children.Add(spDate); gridRdv.Children.Add(spHdeb); gridRdv.Children.Add(spHfin);
            root.Children.Add(gridRdv);

            // ══════════════════════════════════════════════════════════════
            // SECTION 4 — FACTURATION (0 FCFA)
            // ══════════════════════════════════════════════════════════════
            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 2, 0, 8),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children =
                {
                    new TextBlock { Text = "🔄  Reprise sous garantie — ", FontSize = 13, FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "0 FCFA  (Gratuite)", FontSize = 14, FontWeight = FontWeights.Bold,
                        Foreground = amber, VerticalAlignment = VerticalAlignment.Center }
                }}
            });

            var lblErreur = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), Height = 18 };
            root.Children.Add(lblErreur);

            scroll.Content = root; Grid.SetRow(scroll, 1);

            // ── Pied ──────────────────────────────────────────────────────
            var footer = new Border
            {
                Background = footerBg, BorderBrush = borderClr,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(18, 10, 18, 12), CornerRadius = new CornerRadius(0, 0, 14, 14)
            };
            var panelBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnAnnuler = new Button
            {
                Content = "✕  Annuler", Width = 100, Height = 36, FontSize = 12,
                Foreground = slateBody, Background = Brushes.White,
                BorderThickness = new Thickness(1), BorderBrush = borderClr,
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0)
            };
            btnAnnuler.Click += (_, _) => fenetre.Close();

            var btnEnregistrer = new Button
            {
                Content = modeEdition ? "💾  Enregistrer les modifications" : "💾  Enregistrer la Reprise",
                Height = 36, Padding = new Thickness(16, 0, 16, 0), FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Background = amber, BorderThickness = new Thickness(0), Cursor = Cursors.Hand
            };

            btnEnregistrer.Click += (_, _) =>
            {
                lblErreur.Text = "";

                if (!modeEdition && cmbCommande.SelectedValue == null) { lblErreur.Text = "Sélectionnez une commande."; return; }
                if (!modeEdition && cmbPiece.SelectedValue == null)    { lblErreur.Text = "Sélectionnez une pièce."; return; }
                if (string.IsNullOrWhiteSpace(txtDescription.Text))    { lblErreur.Text = "Description obligatoire."; return; }
                if (!modeEdition && idCouturierInitial == null)        { lblErreur.Text = "La pièce n'a pas de couturier assigné."; return; }

                int? idCoutReprise = null;
                if (cmbCouturierReprise.SelectedItem is ComboBoxItem ci && (int)ci.Tag != -1)
                    idCoutReprise = (int)ci.Tag;

                TimeSpan? hDeb = TimeSpan.TryParse(txtHdeb.Text, out var h1) ? h1 : null;
                TimeSpan? hFin = TimeSpan.TryParse(txtHfin.Text, out var h2) ? h2 : null;

                try
                {
                    // Sauvegarder les mesures modifiées dans la fiche client (avec audit)
                    if (idClientCourant.HasValue && typeVetementCourant != null && txtsMesures.Count > 0)
                    {
                        var mesuresModif = txtsMesures
                            .Select(kvp => (kvp.Key, kvp.Value.Text.Trim()))
                            .Where(m => !string.IsNullOrEmpty(m.Item2));
                        _mesureService.UpsertMesures(
                            idClientCourant.Value, typeVetementCourant, mesuresModif,
                            _utilisateur.IdEmploye, _utilisateur.Prenom + " " + _utilisateur.Nom);
                    }

                    if (modeEdition)
                    {
                        retourExistant!.DescriptionProbleme = txtDescription.Text.Trim();
                        retourExistant.IdCouturierReprise   = idCoutReprise;
                        retourExistant.DateRdvReprise       = dpRdv.SelectedDate;
                        retourExistant.HeureDebutReprise    = hDeb;
                        retourExistant.HeureFinReprise      = hFin;
                        if (!string.IsNullOrEmpty(cheminPhotoDefaut))
                            retourExistant.CheminPhotoDefaut = cheminPhotoDefaut;
                        _retourService.Modifier(retourExistant);
                        MessageBox.Show("Retour mis à jour.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        var retour = new Retour
                        {
                            IdCommande              = (int)cmbCommande.SelectedValue,
                            IdPieceCommande         = (int)cmbPiece.SelectedValue,
                            IdCouturier             = idCouturierInitial!.Value,
                            IdCouturierReprise      = idCoutReprise,
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
                        MessageBox.Show($"Retour {retour.NumeroRetour} enregistré.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    ChargerRetours();
                    fenetre.DialogResult = true;
                    fenetre.Close();
                }
                catch (Exception ex) { lblErreur.Text = ex.Message; }
            };

            panelBtns.Children.Add(btnAnnuler); panelBtns.Children.Add(btnEnregistrer);
            footer.Child = panelBtns; Grid.SetRow(footer, 2);

            mainGrid.Children.Add(header); mainGrid.Children.Add(scroll); mainGrid.Children.Add(footer);
            enveloppe.Child = mainGrid; fenetre.Content = enveloppe;
            fenetre.ShowDialog();
        }

        // ═════════════════════════════════════════════════════════════════
        // HELPERS UI
        // ═════════════════════════════════════════════════════════════════
        private static void AjouterSectionTitre(StackPanel parent, string titre)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 12) };
            panel.Children.Add(new TextBlock { Text = titre, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)), HorizontalAlignment = HorizontalAlignment.Left, Width = 36 });
            parent.Children.Add(panel);
        }

        private static void AjouterLabel(StackPanel parent, string texte)
        {
            parent.Children.Add(new TextBlock { Text = texte, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), Margin = new Thickness(0, 0, 0, 4) });
        }

        private static Button CréerBouton(string texte, string hex, int h = 36)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new Button
            {
                Content = texte, Height = h, Padding = new Thickness(12, 0, 12, 0), FontSize = 11,
                FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                Background = new SolidColorBrush(c), BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0)
            };
        }

        private string? DemanderMotif(string titre)
        {
            string? resultat = null;
            var slateDark  = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
            var amber      = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
            var borderClr  = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            var footerBg   = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            var slateBody  = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));

            var dlg = new Window
            {
                Width = 440, Height = 300, WindowStyle = WindowStyle.None,
                AllowsTransparency = true, Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, Owner = Window.GetWindow(this)
            };
            dlg.KeyDown += (_, ev) => { if (ev.Key == Key.Escape) { dlg.DialogResult = false; dlg.Close(); } };

            var env = new Border
            {
                CornerRadius = new CornerRadius(12), Background = Brushes.White,
                BorderBrush = borderClr, BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = 0.15, BlurRadius = 18, ShadowDepth = 3 }
            };
            var mainG = new Grid();
            mainG.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainG.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainG.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var hdr = new Border { Background = slateDark, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(12, 12, 0, 0) };
            hdr.Child = new TextBlock { Text = titre, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            Grid.SetRow(hdr, 0);

            var sp = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
            sp.Children.Add(new TextBlock { Text = "Motif :", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = slateDark, Margin = new Thickness(0, 0, 0, 6) });
            var txt = new TextBox { Height = 72, FontSize = 13, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10, 8, 10, 8), BorderBrush = borderClr, BorderThickness = new Thickness(1) };
            sp.Children.Add(txt);
            var err = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), Height = 16 };
            sp.Children.Add(err);
            Grid.SetRow(sp, 1);

            var foot = new Border { Background = footerBg, BorderBrush = borderClr, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 10, 18, 12), CornerRadius = new CornerRadius(0, 0, 12, 12) };
            var footRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnC = new Button { Content = "Annuler", Width = 90, Height = 38, FontSize = 13, Foreground = slateBody, Background = Brushes.White, BorderBrush = borderClr, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0) };
            btnC.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };
            var btnOk = new Button { Content = "Confirmer", Width = 110, Height = 38, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Background = amber, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            btnOk.Click += (_, _) => { if (string.IsNullOrWhiteSpace(txt.Text)) { err.Text = "Le motif est obligatoire."; return; } resultat = txt.Text.Trim(); dlg.DialogResult = true; dlg.Close(); };
            footRow.Children.Add(btnC); footRow.Children.Add(btnOk);
            foot.Child = footRow; Grid.SetRow(foot, 2);

            mainG.Children.Add(hdr); mainG.Children.Add(sp); mainG.Children.Add(foot);
            env.Child = mainG; dlg.Content = env;
            dlg.ShowDialog();
            return resultat;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // DTO PERFORMANCE QUALITÉ
    // ═════════════════════════════════════════════════════════════════
    public class PerformanceCouturier
    {
        public int IdCouturier       { get; set; }
        public string NomCouturier   { get; set; } = string.Empty;
        public int NbPieces          { get; set; }
        public int NbRetours         { get; set; }
        public int NbReprises        { get; set; }
        public double TauxQualite    { get; set; }
        public int Rang              { get; set; }
        public bool EstEligiblePrime { get; set; }
        public decimal PrimeZeroDefaut { get; set; }

        public string NbPiecesAffiche   => NbPieces.ToString();
        public string NbRetoursAffiche  => NbRetours == 0 ? "✓ 0" : NbRetours.ToString();
        public string NbReprisesAffiche => NbReprises == 0 ? "—" : NbReprises.ToString();
        public string TauxQualiteAffiche => TauxQualite.ToString("0.#") + "%";
        public double TauxRetour => NbPieces > 0 ? Math.Round(100.0 * NbRetours / NbPieces, 1) : 0.0;
        public string TauxRetourAffiche => TauxRetour.ToString("0.#") + "%";

        public bool EstExcellent => TauxQualite >= 98;
        public bool EstFaible    => TauxQualite < 85;

        public string Medaille => Rang switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => $"#{Rang}" };

        public string BadgePrime => EstEligiblePrime
            ? $"🎁 Éligible Prime +{PrimeZeroDefaut:N0} FCFA"
            : NbRetours == 0 && NbPieces < 5 ? "⚠️ < 5 pièces" : $"❌ {NbRetours} retour(s)";
    }
}
