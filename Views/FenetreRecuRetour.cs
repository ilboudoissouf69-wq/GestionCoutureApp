using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Fenêtre d'impression des tickets retour 80 mm (entièrement en code-behind).
    /// Deux types de ticket au choix :
    ///   - ÉTIQUETTE COLIS  : à coller sur la pièce en atelier.
    ///   - REÇU CLIENT      : à remettre au déposant.
    /// Réimpression possible, tracée dans le journal d'audit.
    /// Réutilise les mêmes helpers visuels que FenetreRecu (police Consolas, largeur 290 px).
    /// </summary>
    public class FenetreRecuRetour : Window
    {
        // ── Constantes visuelles (identiques à FenetreRecu) ──────────────
        private const string SEP  = "================================";
        private const string SEP2 = "--------------------------------";
        private static readonly Brush Noir   = Brushes.Black;
        private static readonly Brush Gris   = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        private static readonly Brush Rouge  = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
        private static readonly Brush Vert   = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
        private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
        private static readonly Brush Marque = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

        // ── Données ───────────────────────────────────────────────────────
        private readonly Retour _retour;
        private readonly IRetourService _retourService;
        private readonly IReceiptService _receiptService;
        private readonly int _idOperateur;
        private readonly string _nomOperateur;
        private readonly string _roleOperateur;

        // ── État UI ───────────────────────────────────────────────────────
        private readonly StackPanel _panelEtiquette;
        private readonly StackPanel _panelRecu;
        private StackPanel _panelActif;

        // ═════════════════════════════════════════════════════════════════
        // CONSTRUCTEUR
        // ═════════════════════════════════════════════════════════════════
        public FenetreRecuRetour(
            Retour retour,
            IRetourService retourService,
            IReceiptService receiptService,
            int idOperateur,
            string nomOperateur,
            string roleOperateur)
        {
            _retour          = retour;
            _retourService   = retourService;
            _receiptService  = receiptService;
            _idOperateur     = idOperateur;
            _nomOperateur    = nomOperateur;
            _roleOperateur   = roleOperateur;

            Title                       = $"Tickets Retour — {retour.NumeroRetour}";
            Width                       = 780;
            Height                      = 720;
            MinWidth                    = 700;
            WindowStartupLocation       = WindowStartupLocation.CenterOwner;
            ResizeMode                  = ResizeMode.CanResize;
            Background                  = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            UseLayoutRounding           = true;
            SnapsToDevicePixels         = true;

            // ── Panneaux de contenu ───────────────────────────────────────
            _panelEtiquette = new StackPanel { Width = 290, Margin = new Thickness(0, 15, 0, 15) };
            _panelRecu      = new StackPanel { Width = 290, Margin = new Thickness(0, 15, 0, 15) };
            _panelActif     = _panelEtiquette;

            // ── Structure principale ──────────────────────────────────────
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // onglets
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // aperçu
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // boutons

            // ── Onglets ───────────────────────────────────────────────────
            var tabPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                Background          = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin              = new Thickness(0)
            };

            var btnTabEtiquette = CréerOnglet("📦  Étiquette colis", true);
            var btnTabRecu      = CréerOnglet("🧾  Reçu client",     false);

            // Scroll partagé (une seule zone d'aperçu)
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var borderApercu = new Border
            {
                Background   = Brushes.White,
                Margin       = new Thickness(20, 10, 20, 6),
                CornerRadius = new CornerRadius(4),
                Child        = scrollViewer
            };
            Grid.SetRow(borderApercu, 1);
            mainGrid.Children.Add(borderApercu);

            void BasculerOnglet(bool versEtiquette)
            {
                scrollViewer.Content = versEtiquette ? _panelEtiquette : _panelRecu;
                _panelActif          = versEtiquette ? _panelEtiquette : _panelRecu;
                // Style onglets
                btnTabEtiquette.Background = new SolidColorBrush(versEtiquette
                    ? Color.FromRgb(0xD9, 0x77, 0x06)
                    : Color.FromRgb(0x1E, 0x29, 0x3B));
                btnTabRecu.Background = new SolidColorBrush(!versEtiquette
                    ? Color.FromRgb(0xD9, 0x77, 0x06)
                    : Color.FromRgb(0x1E, 0x29, 0x3B));
            }

            btnTabEtiquette.Click += (_, _) => BasculerOnglet(true);
            btnTabRecu.Click      += (_, _) => BasculerOnglet(false);

            tabPanel.Children.Add(btnTabEtiquette);
            tabPanel.Children.Add(btnTabRecu);
            Grid.SetRow(tabPanel, 0);
            mainGrid.Children.Add(tabPanel);

            // ── Boutons du bas ────────────────────────────────────────────
            var btnPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 4, 0, 18)
            };

            var btnImprimer = CréerBouton("🖨️  Imprimer", "#2E86C1");
            var btnFermer   = CréerBouton("Fermer",       "#95A5A6");

            btnImprimer.Click += BtnImprimer_Click;
            btnFermer.Click   += (_, _) => Close();

            btnPanel.Children.Add(btnImprimer);
            btnPanel.Children.Add(btnFermer);
            Grid.SetRow(btnPanel, 2);
            mainGrid.Children.Add(btnPanel);

            Content = mainGrid;

            // ── Construction des deux tickets ─────────────────────────────
            var settings = _receiptService.GetReceiptInfoAsync().Result;
            string nomAtelier  = settings?.NomAtelier  ?? "RETOUCHE CHOCO";
            string telAtelier  = settings?.Telephone   ?? "+226 62 11 45 11";

            ConstruireEtiquetteColis(nomAtelier, telAtelier);
            ConstruireRecuClient(nomAtelier, telAtelier);

            // Afficher l'étiquette par défaut
            scrollViewer.Content = _panelEtiquette;
        }

        // ═════════════════════════════════════════════════════════════════
        // ÉTIQUETTE COLIS (à coller sur la pièce)
        // ═════════════════════════════════════════════════════════════════
        private void ConstruireEtiquetteColis(string nomAtelier, string telAtelier)
        {
            var p = _panelEtiquette;
            var r = _retour;

            string nomClient    = r.Commande?.Client != null
                ? r.Commande.Client.Prenom + " " + r.Commande.Client.Nom : "—";
            string telephone    = r.Commande?.Client?.Telephone ?? "—";
            string typeVetement = r.PieceCommande?.TypeVetement ?? "—";
            string couturier    = r.CouturierReprise != null
                ? r.CouturierReprise.Prenom + " " + r.CouturierReprise.Nom
                : r.Couturier != null
                    ? r.Couturier.Prenom + " " + r.Couturier.Nom
                    : "—";
            string rdv = r.DateRdvReprise.HasValue
                ? r.DateRdvReprise.Value.ToString("dd/MM/yyyy") +
                  (r.HeureDebutReprise.HasValue
                      ? " à " + r.HeureDebutReprise.Value.ToString(@"hh\:mm")
                      : "")
                : "—";

            Espace(p, 6);
            Ligne(p, SEP, 8, TextAlignment.Center, Gris);
            Ligne(p, "ÉTIQUETTE COLIS — RETOUR", 12, TextAlignment.Center, Marque, FontWeights.Bold);
            Ligne(p, SEP, 8, TextAlignment.Center, Gris);
            Espace(p, 8);

            // ── Numéro retour en TRÈS GRAND ───────────────────────────────
            Ligne(p, r.NumeroRetour, 22, TextAlignment.Center, Rouge, FontWeights.Bold);
            Espace(p, 4);
            Ligne(p, SEP2, 8, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ── Infos client ──────────────────────────────────────────────
            Ligne(p, "CLIENT",      10, TextAlignment.Left, Noir, FontWeights.Bold);
            LignePad(p, "Nom",      nomClient);
            LignePad(p, "Tél",      telephone);
            Espace(p, 4);

            // ── Pièce ─────────────────────────────────────────────────────
            Ligne(p, "PIÈCE",       10, TextAlignment.Left, Noir, FontWeights.Bold);
            LignePad(p, "Type",     typeVetement);
            Espace(p, 4);

            // ── Problème ──────────────────────────────────────────────────
            Ligne(p, "DÉFAUT SIGNALÉ", 10, TextAlignment.Left, Noir, FontWeights.Bold);
            Ligne(p, "  " + r.DescriptionProbleme, 9, TextAlignment.Left, Noir);
            Espace(p, 4);

            // ── Couturier responsable de la reprise ───────────────────────
            Ligne(p, "COUTURIER REPRISE", 10, TextAlignment.Left, Noir, FontWeights.Bold);
            LignePad(p, "Nom", couturier);
            Espace(p, 4);

            // ── RDV ───────────────────────────────────────────────────────
            Ligne(p, "RDV REPRISE",  10, TextAlignment.Left, Noir, FontWeights.Bold);
            LignePad(p, "Date/Heure", rdv);
            Espace(p, 4);

            // ── Date de signalement ───────────────────────────────────────
            Ligne(p, "DATE SIGNALEMENT", 10, TextAlignment.Left, Noir, FontWeights.Bold);
            LignePad(p, "Le", r.DateSignalement.ToString("dd/MM/yyyy à HH:mm"));
            Espace(p, 8);

            Ligne(p, SEP, 8, TextAlignment.Center, Gris);
            Ligne(p, nomAtelier + "  |  " + telAtelier, 8, TextAlignment.Center, Gris);
            Ligne(p, SEP, 8, TextAlignment.Center, Gris);
            Espace(p, 8);
        }

        // ═════════════════════════════════════════════════════════════════
        // REÇU CLIENT (à remettre au déposant)
        // ═════════════════════════════════════════════════════════════════
        private void ConstruireRecuClient(string nomAtelier, string telAtelier)
        {
            var p = _panelRecu;
            var r = _retour;

            string nomClient    = r.Commande?.Client != null
                ? r.Commande.Client.Prenom + " " + r.Commande.Client.Nom : "—";
            string typeVetement = r.PieceCommande?.TypeVetement ?? "—";
            string rdv = r.DateRdvReprise.HasValue
                ? r.DateRdvReprise.Value.ToString("dd/MM/yyyy") +
                  (r.HeureDebutReprise.HasValue
                      ? " à " + r.HeureDebutReprise.Value.ToString(@"hh\:mm")
                      : "")
                : "Date à confirmer";

            Espace(p, 8);

            // ── Logo (même logique que FenetreRecu) ───────────────────────
            try
            {
                var logo = new System.Windows.Controls.Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/Resources/logo_retouche_choco.png")),
                    Width  = 80, Height = 80,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Margin  = new Thickness(0, 0, 0, 4)
                };
                p.Children.Add(logo);
            }
            catch { /* logo absent → on continue */ }

            Ligne(p, nomAtelier,        13, TextAlignment.Center, Marque, FontWeights.Bold);
            Ligne(p, telAtelier,         9, TextAlignment.Center, Gris);
            Espace(p, 6);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ── Numéro retour en GRAND ────────────────────────────────────
            Ligne(p, "REÇU DE RETOUR",   12, TextAlignment.Center, Noir, FontWeights.Bold);
            Espace(p, 4);
            Ligne(p, r.NumeroRetour,     20, TextAlignment.Center, Rouge, FontWeights.Bold);
            Espace(p, 6);
            Ligne(p, SEP2, 8, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ── Infos ─────────────────────────────────────────────────────
            LignePad(p, "Client",    nomClient);
            LignePad(p, "Pièce",     typeVetement);
            Espace(p, 4);

            // ── Problème ──────────────────────────────────────────────────
            Ligne(p, "Défaut :",     10, TextAlignment.Left, Gris, FontWeights.Bold);
            Ligne(p, "  " + r.DescriptionProbleme, 9, TextAlignment.Left, Noir);
            Espace(p, 6);

            // ── RDV de reprise ────────────────────────────────────────────
            LignePad(p, "RDV reprise", rdv, Orange);
            Espace(p, 6);

            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ── Message central ───────────────────────────────────────────
            Ligne(p,
                "Présentez ce numéro pour",
                10, TextAlignment.Center, Noir, FontWeights.Bold);
            Ligne(p,
                "récupérer votre pièce.",
                10, TextAlignment.Center, Noir, FontWeights.Bold);
            Espace(p, 6);

            // ── Reprise gratuite ──────────────────────────────────────────
            Ligne(p, "★  Reprise sous garantie — GRATUITE  ★",
                9, TextAlignment.Center, Vert, FontWeights.Bold);
            Espace(p, 8);

            Ligne(p, SEP, 8, TextAlignment.Center, Gris);
            Ligne(p, "Merci pour votre confiance !", 9, TextAlignment.Center, Noir, FontWeights.Bold);
            Ligne(p, SEP, 8, TextAlignment.Center, Gris);
            Espace(p, 10);
        }

        // ═════════════════════════════════════════════════════════════════
        // IMPRESSION
        // ═════════════════════════════════════════════════════════════════
        private void BtnImprimer_Click(object sender, RoutedEventArgs e)
        {
            bool estEtiquette = ReferenceEquals(_panelActif, _panelEtiquette);
            string typeTicket = estEtiquette ? "Étiquette colis" : "Reçu client";

            try
            {
                var pd = new PrintDialog();
                if (pd.ShowDialog() == true)
                {
                    _panelActif.Measure(new Size(290, double.PositiveInfinity));
                    _panelActif.Arrange(new Rect(new Point(0, 0), _panelActif.DesiredSize));
                    pd.PrintVisual(_panelActif, $"Retour {_retour.NumeroRetour} — {typeTicket}");

                    // Audit réimpression
                    _ = _retourService.EnregistrerImpressionAsync(
                        _retour.IdRetour, typeTicket,
                        _idOperateur, _nomOperateur, _roleOperateur);

                    MessageBox.Show(
                        $"{typeTicket} imprimé avec succès.",
                        "Impression", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur d'impression :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // HELPERS VISUELS (dupliqués de FenetreRecu pour autonomie)
        // ═════════════════════════════════════════════════════════════════

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

        private void LignePad(StackPanel p, string label, string valeur, Brush? couleur = null)
        {
            int pts = Math.Max(2, 14 - label.Length);
            string ligne = "  " + label + new string('.', pts) + ": " + valeur;
            Ligne(p, ligne, 10, TextAlignment.Left, couleur ?? Noir);
        }

        private static Button CréerOnglet(string texte, bool actif)
            => new()
            {
                Content          = texte,
                Height           = 36,
                Padding          = new Thickness(20, 0, 20, 0),
                FontSize         = 12,
                FontWeight       = FontWeights.SemiBold,
                Foreground       = Brushes.White,
                Background       = new SolidColorBrush(actif
                    ? Color.FromRgb(0xD9, 0x77, 0x06)
                    : Color.FromRgb(0x1E, 0x29, 0x3B)),
                BorderThickness  = new Thickness(0),
                Cursor           = System.Windows.Input.Cursors.Hand
            };

        private static Button CréerBouton(string texte, string couleurHex)
        {
            var c = (Color)ColorConverter.ConvertFromString(couleurHex);
            return new Button
            {
                Content         = texte,
                Width           = 150,
                Height          = 38,
                FontSize        = 13,
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
