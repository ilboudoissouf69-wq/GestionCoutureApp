using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Fenêtre d'aperçu avant impression réutilisable.
    /// Affiche le(s) panel(s) à imprimer dans un visualiseur zoomable,
    /// puis propose de lancer l'impression ou d'annuler.
    /// </summary>
    public class ApercuImprimerieWindow : Window
    {
        // ── Données ──────────────────────────────────────────────────────
        private readonly List<(StackPanel Panel, string Description)> _pages;
        private int _pageActuelle = 0;

        // ── Contrôles ────────────────────────────────────────────────────
        private readonly ScrollViewer _scrollViewer;
        private readonly Border _conteneurPage;
        private readonly TextBlock _lblPageInfo;
        private readonly Button _btnPrecedent;
        private readonly Button _btnSuivant;
        private readonly Slider _sliderZoom;
        private readonly TextBlock _lblZoom;
        private double _zoom = 1.0;

        // ── Résultat ─────────────────────────────────────────────────────
        /// <summary>
        /// True si l'utilisateur a cliqué sur « Imprimer »,
        /// false s'il a fermé ou cliqué « Annuler ».
        /// </summary>
        public bool LancerImpression { get; private set; } = false;

        // ─────────────────────────────────────────────────────────────────
        // Constructeur
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Crée la fenêtre d'aperçu.
        /// </summary>
        /// <param name="pages">
        /// Chaque élément est le panel à prévisualiser accompagné de sa description (titre du job d'impression).
        /// </param>
        /// <param name="titre">Titre affiché dans la barre de la fenêtre.</param>
        public ApercuImprimerieWindow(
            IEnumerable<(StackPanel Panel, string Description)> pages,
            string titre = "Aperçu avant impression")
        {
            _pages = pages?.ToList()
                ?? throw new ArgumentNullException(nameof(pages));

            if (_pages.Count == 0)
                throw new ArgumentException("Au moins une page est requise.", nameof(pages));

            // ── Propriétés fenêtre ────────────────────────────────────────
            Title                 = titre;
            Width                 = 700;
            Height                = 860;
            MinWidth              = 480;
            MinHeight             = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode            = ResizeMode.CanResize;
            Background            = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));

            // ── Grille principale ─────────────────────────────────────────
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // barre supérieure
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // zone aperçu
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // barre bas

            // ── BARRE SUPÉRIEURE ─────────────────────────────────────────
            var barreHaut = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                Padding    = new Thickness(16, 8, 16, 8)
            };

            var panelHaut = new StackPanel { Orientation = Orientation.Horizontal };

            // Titre aperçu
            var lblTitre = new TextBlock
            {
                Text              = "🖨️  " + titre,
                Foreground        = Brushes.White,
                FontSize          = 15,
                FontWeight        = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 24, 0)
            };
            panelHaut.Children.Add(lblTitre);

            // Zoom −
            var btnZoomMoins = CréerBoutonOutil("−");
            btnZoomMoins.Click += (_, _) => ChangerZoom(-0.1);
            panelHaut.Children.Add(btnZoomMoins);

            // Slider zoom
            _sliderZoom = new Slider
            {
                Minimum           = 0.5,
                Maximum           = 2.5,
                Value             = 1.0,
                Width             = 120,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(4, 0, 4, 0),
                ToolTip           = "Zoom"
            };
            _sliderZoom.ValueChanged += (_, _) =>
            {
                _zoom = _sliderZoom.Value;
                _lblZoom.Text = $"{(int)(_zoom * 100)} %";
                AppliquerZoom();
            };
            panelHaut.Children.Add(_sliderZoom);

            // Zoom +
            var btnZoomPlus = CréerBoutonOutil("+");
            btnZoomPlus.Click += (_, _) => ChangerZoom(+0.1);
            panelHaut.Children.Add(btnZoomPlus);

            // Libellé zoom
            _lblZoom = new TextBlock
            {
                Text              = "100 %",
                Foreground        = Brushes.LightGray,
                FontSize          = 12,
                Width             = 44,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 20, 0),
                TextAlignment     = TextAlignment.Left
            };
            panelHaut.Children.Add(_lblZoom);

            // Navigation pages (visible seulement si plusieurs pages)
            if (_pages.Count > 1)
            {
                _btnPrecedent = CréerBoutonOutil("◀");
                _btnPrecedent.Click += (_, _) => NaviguerPage(-1);
                panelHaut.Children.Add(_btnPrecedent);

                _lblPageInfo = new TextBlock
                {
                    Foreground        = Brushes.LightGray,
                    FontSize          = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(8, 0, 8, 0),
                    MinWidth          = 60,
                    TextAlignment     = TextAlignment.Center
                };
                panelHaut.Children.Add(_lblPageInfo);

                _btnSuivant = CréerBoutonOutil("▶");
                _btnSuivant.Click += (_, _) => NaviguerPage(+1);
                panelHaut.Children.Add(_btnSuivant);
            }
            else
            {
                // Champs jamais null (évite les warnings nullable) mais non affichés
                _btnPrecedent = new Button();
                _btnSuivant   = new Button();
                _lblPageInfo  = new TextBlock();
            }

            barreHaut.Child = panelHaut;
            Grid.SetRow(barreHaut, 0);
            mainGrid.Children.Add(barreHaut);

            // ── ZONE APERÇU ───────────────────────────────────────────────
            _conteneurPage = new Border
            {
                Background      = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Top,
                Margin          = new Thickness(0),
                // LayoutTransform géré dans AppliquerZoom
            };

            _scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                Background                    = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                Padding                       = new Thickness(40, 30, 40, 30),
                Content                       = _conteneurPage
            };

            Grid.SetRow(_scrollViewer, 1);
            mainGrid.Children.Add(_scrollViewer);

            // ── BARRE BAS ─────────────────────────────────────────────────
            var barreBas = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                Padding         = new Thickness(16, 10, 16, 10)
            };

            var panelBas = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var btnImprimer = CréerBoutonAction("🖨️  Imprimer", "#2E86C1");
            btnImprimer.Width  = 160;
            btnImprimer.Click += (_, _) =>
            {
                LancerImpression = true;
                Close();
            };

            var btnAnnuler = CréerBoutonAction("Annuler", "#636E72");
            btnAnnuler.Width  = 120;
            btnAnnuler.Click += (_, _) =>
            {
                LancerImpression = false;
                Close();
            };

            panelBas.Children.Add(btnImprimer);
            panelBas.Children.Add(new Border { Width = 12 });
            panelBas.Children.Add(btnAnnuler);
            barreBas.Child = panelBas;
            Grid.SetRow(barreBas, 2);
            mainGrid.Children.Add(barreBas);

            Content = mainGrid;

            // ── Afficher la première page après chargement ─────────────────
            Loaded += (_, _) => AfficherPage(0);

            // Initialiser les contrôles de navigation (évite CS8602 sur première lecture)
            _lblPageInfo.Text       = _pages.Count > 1 ? $"Page 1 / {_pages.Count}" : "";
            _btnPrecedent.IsEnabled = false;
            _btnSuivant.IsEnabled   = _pages.Count > 1;
        }

        // ─────────────────────────────────────────────────────────────────
        // API publique — variante simple pour une seule page
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Crée une fenêtre d'aperçu pour un seul panel.
        /// </summary>
        public static ApercuImprimerieWindow PourUnPanel(
            StackPanel panel, string description, string titre)
            => new ApercuImprimerieWindow(
                new[] { (panel, description) },
                titre);

        // ─────────────────────────────────────────────────────────────────
        // Navigation entre pages
        // ─────────────────────────────────────────────────────────────────

        private void NaviguerPage(int delta)
        {
            int nouvelle = _pageActuelle + delta;
            if (nouvelle < 0 || nouvelle >= _pages.Count) return;
            AfficherPage(nouvelle);
        }

        private void AfficherPage(int index)
        {
            _pageActuelle = index;

            var (panel, _) = _pages[index];

            // Cloner visuellement le panel pour l'aperçu sans le détacher de sa fenêtre parente
            _conteneurPage.Child = ClonerPourApercu(panel);
            AppliquerZoom();

            if (_pages.Count > 1)
            {
                _lblPageInfo.Text         = $"Page {index + 1} / {_pages.Count}";
                _btnPrecedent.IsEnabled   = index > 0;
                _btnSuivant.IsEnabled     = index < _pages.Count - 1;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Zoom
        // ─────────────────────────────────────────────────────────────────

        private void ChangerZoom(double delta)
        {
            double nouvelleValeur = Math.Clamp(_zoom + delta, 0.5, 2.5);
            _sliderZoom.Value = nouvelleValeur; // déclenche ValueChanged → AppliquerZoom
        }

        private void AppliquerZoom()
        {
            _conteneurPage.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        }

        // ─────────────────────────────────────────────────────────────────
        // Clone du panel pour l'affichage (non-destructif)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Crée une copie visuelle fidèle d'un StackPanel pour l'affichage dans l'aperçu.
        /// Supporte TextBlock, Border, et Image — les mêmes éléments que CopierPanel
        /// de FenetreTicketsCommande.
        /// </summary>
        private static StackPanel ClonerPourApercu(StackPanel source)
        {
            var clone = new StackPanel
            {
                Width   = source.Width,
                Margin  = source.Margin,
                MinWidth = 290
            };

            foreach (UIElement enfant in source.Children)
            {
                switch (enfant)
                {
                    case TextBlock texte:
                        clone.Children.Add(new TextBlock
                        {
                            Text          = texte.Text,
                            FontFamily    = texte.FontFamily,
                            FontSize      = texte.FontSize,
                            FontWeight    = texte.FontWeight,
                            TextAlignment = texte.TextAlignment,
                            Foreground    = texte.Foreground,
                            TextWrapping  = texte.TextWrapping,
                            Margin        = texte.Margin,
                            Padding       = texte.Padding
                        });
                        break;

                    case Border bordure:
                        var cloneBordure = new Border
                        {
                            Height          = bordure.Height,
                            Width           = bordure.Width,
                            Margin          = bordure.Margin,
                            Background      = bordure.Background,
                            BorderBrush     = bordure.BorderBrush,
                            BorderThickness = bordure.BorderThickness,
                            CornerRadius    = bordure.CornerRadius,
                            Padding         = bordure.Padding
                        };
                        // Si la bordure a un enfant TextBlock (ex: lignes montant encadrées)
                        if (bordure.Child is TextBlock txtEnfant)
                        {
                            cloneBordure.Child = new TextBlock
                            {
                                Text          = txtEnfant.Text,
                                FontFamily    = txtEnfant.FontFamily,
                                FontSize      = txtEnfant.FontSize,
                                FontWeight    = txtEnfant.FontWeight,
                                TextAlignment = txtEnfant.TextAlignment,
                                Foreground    = txtEnfant.Foreground,
                                TextWrapping  = txtEnfant.TextWrapping,
                                Margin        = txtEnfant.Margin,
                                Padding       = txtEnfant.Padding
                            };
                        }
                        clone.Children.Add(cloneBordure);
                        break;

                    case System.Windows.Controls.Image image:
                        clone.Children.Add(new System.Windows.Controls.Image
                        {
                            Source              = image.Source,
                            Width               = image.Width,
                            Height              = image.Height,
                            Stretch             = image.Stretch,
                            Margin              = image.Margin,
                            HorizontalAlignment = image.HorizontalAlignment
                        });
                        break;

                    default:
                        // Élément inconnu : on place un espace vide pour conserver la mise en page
                        clone.Children.Add(new Border { Height = 4 });
                        break;
                }
            }
            return clone;
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers visuels
        // ─────────────────────────────────────────────────────────────────

        private static Button CréerBoutonOutil(string contenu)
            => new Button
            {
                Content         = contenu,
                Width           = 28,
                Height          = 28,
                FontSize        = 14,
                FontWeight      = FontWeights.Bold,
                Foreground      = Brushes.White,
                Background      = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5C)),
                BorderThickness = new Thickness(0),
                Margin          = new Thickness(2, 0, 2, 0),
                Padding         = new Thickness(0),
                Cursor          = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

        private static Button CréerBoutonAction(string contenu, string couleurHex)
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(couleurHex);
            return new Button
            {
                Content         = contenu,
                Height          = 40,
                FontSize        = 13,
                FontWeight      = FontWeights.Bold,
                Foreground      = Brushes.White,
                Background      = new SolidColorBrush(c),
                BorderThickness = new Thickness(0),
                Padding         = new Thickness(16, 0, 16, 0),
                Cursor          = System.Windows.Input.Cursors.Hand
            };
        }
    }
}
