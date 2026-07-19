using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Fenetre de recu de paiement construite entierement en code C#
    /// (pas de XAML pour eviter les conflits de partial class avec WPF).
    /// </summary>
    public class FenetreRecu : Window
    {
        // ----------------------------------------------------------------
        // Constantes visuelles
        // ----------------------------------------------------------------
        private const string SEP = "================================";
        private static readonly Brush Noir = Brushes.Black;
        private static readonly Brush Gris = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        private static readonly Brush Rouge = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
        private static readonly Brush Vert = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
        private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
        private static readonly Brush Marque = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

        // ----------------------------------------------------------------
        // Champs
        // ----------------------------------------------------------------
        private readonly Commande _commande;
        private readonly Paiement _paiement;
        private readonly List<Mesure> _mesures;
        private readonly string _nomOperateur;

        // Panneau scrollable ou seront ajoutes les lignes du recu
        private readonly StackPanel _receiptPanel;

        // ----------------------------------------------------------------
        // Constructeur : construit toute la fenetre en code
        // ----------------------------------------------------------------
        public FenetreRecu(
            Commande commande,
            Paiement paiement,
            List<Mesure> mesures,
            string nomOperateur)
        {
            _commande = commande;
            _paiement = paiement;
            _mesures = mesures ?? new List<Mesure>();
            _nomOperateur = nomOperateur ?? "";

            // ---- Proprietes de la fenetre ----
            Title = "Recu N° " + paiement.RecuNumero;
            Width = 360;
            Height = 780;
            MinWidth = 340;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            // ---- Structure : Grid avec zone recu + boutons ----
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- Zone apercu du recu ----
            _receiptPanel = new StackPanel
            {
                Width = 290,
                Margin = new Thickness(0, 15, 0, 15)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _receiptPanel
            };

            var borderRecu = new Border
            {
                Background = Brushes.White,
                Margin = new Thickness(20, 20, 20, 10),
                CornerRadius = new CornerRadius(4),
                Child = scroll
            };
            Grid.SetRow(borderRecu, 0);
            grid.Children.Add(borderRecu);

            // ---- Boutons ----
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 18)
            };

            var btnImprimer = CreerBouton("Imprimer", "#2E86C1");
            btnImprimer.Click += BtnImprimer_Click;

            var btnFermer = CreerBouton("Fermer", "#95A5A6");
            btnFermer.Click += (s, e) => Close();

            btnPanel.Children.Add(btnImprimer);
            btnPanel.Children.Add(btnFermer);

            Grid.SetRow(btnPanel, 1);
            grid.Children.Add(btnPanel);

            Content = grid;

            // ---- Construit le contenu du recu ----
            ConstruireRecu();
        }

        // ----------------------------------------------------------------
        // Construction du contenu du recu
        // ----------------------------------------------------------------
        private void ConstruireRecu()
        {
            var p = _receiptPanel;

            // ===== BANNIERE ANNULATION =====
            if (_paiement.EstAnnule)
            {
                Ligne(p, SEP, 9, TextAlignment.Center, Rouge);
                Ligne(p, "*** PAIEMENT ANNULE ***", 13, TextAlignment.Center, Rouge, FontWeights.Bold);
                Ligne(p, "Motif : " + (_paiement.MotifsAnnulation ?? "-"),
                                                                 10, TextAlignment.Center, Rouge);
                Ligne(p, "Annule par : " + (_paiement.NomAnnulateur ?? "-"),
                                                                 10, TextAlignment.Center, Rouge);
                Ligne(p, "Date : " + _paiement.DateAnnulation?.ToString("dd/MM/yyyy HH:mm"),
                                                                 10, TextAlignment.Center, Rouge);
                Ligne(p, SEP, 9, TextAlignment.Center, Rouge);
                Espace(p, 8);
            }

            // ===== LOGO =====
            Espace(p, 8);
            try
            {
                var logo = new System.Windows.Controls.Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/Resources/logo_retouche_choco.png")),
                    Width = 100,
                    Height = 100,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                    logo, System.Windows.Media.BitmapScalingMode.HighQuality);
                p.Children.Add(logo);
            }
            catch { /* si le logo est absent, on continue sans planter */ }

            // ===== EN-TETE BOUTIQUE =====
            Espace(p, 8);
            Ligne(p, "RETOUCHE CHOCO", 14, TextAlignment.Center, Marque, FontWeights.Bold);
            Ligne(p, "ILASSA DESIGN", 13, TextAlignment.Center, Noir, FontWeights.Bold);
            Ligne(p, "Les specialistes en slim", 10, TextAlignment.Center, Marque);
            Espace(p, 6);
            Ligne(p, "Zogona, derriere l'alimentation la shopette",
                       9, TextAlignment.Center, Gris);
            Ligne(p, "Ouagadougou, Burkina Faso",
                       9, TextAlignment.Center, Gris);
            Ligne(p, "Tel: +226 62 11 45 11 / WhatsApp: 77 78 86 86",
                       8, TextAlignment.Center, Gris);
            Ligne(p, "Email: ilassailassa11@gmail.com",
                       8, TextAlignment.Center, Gris);
            Espace(p, 4);
            Ligne(p, "Specialiste: VESTES - Pantalons - CHEMISES",
                       8, TextAlignment.Center, Gris);
            Ligne(p, "TENUES DAMES", 8, TextAlignment.Center, Gris);
            Espace(p, 6);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ===== NUMERO RECU + DATE + OPERATEUR =====
            Ligne(p, "RECU N° " + _paiement.RecuNumero,
                       12, TextAlignment.Center, Noir, FontWeights.Bold);
            Ligne(p, _paiement.DatePaiement.ToString("dd/MM/yyyy  a  HH:mm"),
                       10, TextAlignment.Center, Gris);
            Ligne(p, "Par : " + (string.IsNullOrWhiteSpace(_nomOperateur) ? "-" : _nomOperateur),
                       10, TextAlignment.Center, Gris);
            Espace(p, 6);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ===== CLIENT / LIVRAISON / COUTURIER =====
            string nomClient = ((_commande.Client?.Prenom ?? "") + " " +
                                   (_commande.Client?.Nom ?? "")).Trim();
            string nomCouturier = _commande.Couturier != null
                ? (_commande.Couturier.Prenom + " " + _commande.Couturier.Nom).Trim()
                : "Non assigne";

            Ligne(p, Pad("CLIENT", 17) + " : " + nomClient, 10);
            Ligne(p, Pad("LIVRAISON PREVUE", 17) + " : " +
                       _commande.DateFin.ToString("dd/MM/yyyy"), 10);
            Ligne(p, Pad("COUTURIER", 17) + " : " + nomCouturier, 10);
            Espace(p, 6);

            // ===== DETAILS DU VETEMENT =====
            Ligne(p, "DETAILS DU VETEMENT", 11, TextAlignment.Left, Noir, FontWeights.Bold);
            Ligne(p, Pad("   Type", 17) + " : " + _commande.TypeVetement, 10);
            if (!string.IsNullOrWhiteSpace(_commande.DescriptionPrecision))
                Ligne(p, Pad("   Desc.", 17) + " : " + _commande.DescriptionPrecision, 10);
            Espace(p, 6);

            // ===== MESURES =====
            if (_mesures.Any())
            {
                Ligne(p, "MESURES PRISES", 11, TextAlignment.Left, Noir, FontWeights.Bold);
                foreach (var m in _mesures)
                {
                    int pts = Math.Max(3, 28 - m.NomMesure.Length - m.Valeur.Length);
                    Ligne(p, "   " + m.NomMesure + " " + new string('.', pts) +
                             " " + m.Valeur + " cm", 10);
                }
                Espace(p, 6);
            }

            // ===== SEPARATEUR FINANCIER =====
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);

            // Montants financiers en decimal
            decimal montantTotal = _paiement.MontantTotalCommande > 0
                ? _paiement.MontantTotalCommande : _commande.MontantTotal;
            decimal resteAvant     = _paiement.ResteAvantPaiement;
            decimal montantCePai   = _paiement.MontantPaye;
            decimal resteApres     = Math.Max(0m, resteAvant - montantCePai);
            decimal totalPaye      = montantTotal - resteApres;

            LigneMontant(p, "Montant commande", Fcfa(montantTotal), 10, Noir);
            LigneMontant(p, "Reste avant paiement", Fcfa(resteAvant), 10, Orange);
            LigneMontant(p, "Ce paiement", Fcfa(montantCePai), 10, Vert, true);
            LigneMontant(p, "Mode", " " + _paiement.ModePaiement, 10, Gris);
            LigneMontant(p, "Total paye", Fcfa(totalPaye), 10, Noir);
            Espace(p, 4);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 4);

            // ===== RESTE A PAYER =====
            Brush cReste = resteApres <= 0 ? Vert : Rouge;
            string tReste = resteApres <= 0 ? "  SOLDE !" : Fcfa(resteApres);
            Ligne(p, Pad("RESTE A PAYER", 17) + " : " + tReste.Trim(),
                       13, TextAlignment.Left, cReste, FontWeights.Bold);
            Espace(p, 4);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 10);

            // ===== PIED DE PAGE =====
            Ligne(p, "Merci pour votre confiance !", 11,
                       TextAlignment.Center, Noir, FontWeights.Bold);
            Espace(p, 6);

            if (resteApres > 0)
            {
                Ligne(p, "Presentez ce recu pour le retrait", 9, TextAlignment.Center, Gris);
                Ligne(p, "de votre commande.", 9, TextAlignment.Center, Gris);
                Espace(p, 4);
                Ligne(p, "Aucun vetement ne sera livre", 9, TextAlignment.Center, Rouge);
                Ligne(p, "sans le paiement integral.", 9, TextAlignment.Center, Rouge);
            }
            else
            {
                Ligne(p, "Commande integralement payee.", 9, TextAlignment.Center, Vert);
                Ligne(p, "Votre commande peut etre retiree", 9, TextAlignment.Center, Gris);
                Ligne(p, "sur presentation de ce recu.", 9, TextAlignment.Center, Gris);
            }
            Espace(p, 14);
        }

        // ----------------------------------------------------------------
        // Impression
        // ----------------------------------------------------------------
        private void BtnImprimer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pd = new System.Windows.Controls.PrintDialog();
                if (pd.ShowDialog() == true)
                {
                    _receiptPanel.Measure(new Size(290, double.PositiveInfinity));
                    _receiptPanel.Arrange(
                        new Rect(new Point(0, 0), _receiptPanel.DesiredSize));
                    pd.PrintVisual(_receiptPanel, "Recu " + _paiement.RecuNumero);
                    MessageBox.Show("Recu imprime avec succes !",
                        "Impression", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur impression :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ----------------------------------------------------------------
        // Helpers de construction du recu
        // ----------------------------------------------------------------

        private void Ligne(StackPanel p, string texte,
            double taille = 10,
            TextAlignment align = TextAlignment.Left,
            Brush? couleur = null,
            FontWeight? poids = null)
        {
            p.Children.Add(new TextBlock
            {
                Text = texte,
                FontFamily = new FontFamily("Consolas"),
                FontSize = taille,
                FontWeight = poids ?? FontWeights.Normal,
                TextAlignment = align,
                Foreground = couleur ?? Noir,
                TextWrapping = TextWrapping.Wrap
            });
        }

        private void Espace(StackPanel p, double hauteur)
        {
            p.Children.Add(new Border { Height = hauteur });
        }

        private void LigneMontant(StackPanel p, string label, string valeur,
            double taille = 10, Brush? couleur = null, bool gras = false)
        {
            int pts = Math.Max(3, 22 - label.Length);
            string l = label + " " + new string('.', pts) + " : " + valeur.TrimStart();
            Ligne(p, l, taille, TextAlignment.Left, couleur,
                  gras ? FontWeights.Bold : FontWeights.Normal);
        }

        private static string Fcfa(decimal montant)
        {
            return montant.ToString("N0", new CultureInfo("fr-FR")) + " FCFA";
        }

        private static string Pad(string s, int n)
        {
            return s.Length >= n ? s : s + new string(' ', n - s.Length);
        }

        private static Button CreerBouton(string texte, string couleurHex)
        {
            var c = (Color)ColorConverter.ConvertFromString(couleurHex);
            return new Button
            {
                Content = texte,
                Width = 120,
                Height = 38,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(c),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(6, 0, 6, 0)
            };
        }
    }
}
