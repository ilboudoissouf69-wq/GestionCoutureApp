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
    /// Fiche atelier interne — ticket agrafé sur le colis du client.
    /// Contient les mesures complètes de chaque pièce et le couturier assigné.
    /// <para>
    /// RÈGLE ABSOLUE : aucune donnée financière (montant, prix, reste à payer)
    /// ne doit figurer sur ce document. Il est strictement réservé à l'atelier.
    /// </para>
    /// Construite entièrement en C#, même pattern que <see cref="FenetreRecu"/>.
    /// </summary>
    public class FicheAtelierWindow : Window
    {
        // ----------------------------------------------------------------
        // Constantes visuelles — identiques à FenetreRecu pour cohérence
        // ----------------------------------------------------------------
        private const string SEP  = "================================";
        private const string SEP2 = "--------------------------------";

        private static readonly Brush Noir   = Brushes.Black;
        private static readonly Brush Gris   = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        private static readonly Brush Rouge  = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
        private static readonly Brush Marron = new SolidColorBrush(Color.FromRgb(0x8B, 0x73, 0x55));
        private static readonly Brush Bleu   = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0x89));

        // ----------------------------------------------------------------
        // Champs
        // ----------------------------------------------------------------
        private readonly Commande _commande;
        private readonly IReceiptService _receiptService;
        private readonly StackPanel _fichePanel;

        // ----------------------------------------------------------------
        // Constructeur statique de commodité — résout IReceiptService depuis DI
        // ----------------------------------------------------------------
        public static FicheAtelierWindow Creer(Commande commande)
        {
            var receiptService = App.Services.GetRequiredService<IReceiptService>();
            return new FicheAtelierWindow(commande, receiptService);
        }

        // ----------------------------------------------------------------
        // Constructeur principal
        // ----------------------------------------------------------------
        public FicheAtelierWindow(Commande commande, IReceiptService receiptService)
        {
            _commande       = commande ?? throw new ArgumentNullException(nameof(commande));
            _receiptService = receiptService ?? throw new ArgumentNullException(nameof(receiptService));

            // ── Propriétés fenêtre ──
            Title                   = $"Fiche Atelier — Commande #{commande.IdCommande}";
            Width                   = 380;
            Height                  = 820;
            MinWidth                = 340;
            WindowStartupLocation   = WindowStartupLocation.CenterOwner;
            ResizeMode              = ResizeMode.CanResize;
            Background              = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            UseLayoutRounding       = true;
            SnapsToDevicePixels     = true;

            // ── Structure : Grid avec zone fiche + boutons ──
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Zone aperçu de la fiche (fond blanc, largeur ticket 290px) ──
            _fichePanel = new StackPanel
            {
                Width  = 290,
                Margin = new Thickness(0, 15, 0, 15)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content                       = _fichePanel
            };

            var borderFiche = new Border
            {
                Background   = Brushes.White,
                Margin       = new Thickness(20, 20, 20, 10),
                CornerRadius = new CornerRadius(4),
                Child        = scroll
            };
            Grid.SetRow(borderFiche, 0);
            grid.Children.Add(borderFiche);

            // ── Boutons Imprimer / Fermer ──
            var btnPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 4, 0, 18)
            };

            var btnImprimer = CreerBouton("🖨  Imprimer", "#2E86C1");
            btnImprimer.Click += BtnImprimer_Click;

            var btnFermer = CreerBouton("Fermer", "#95A5A6");
            btnFermer.Click += (s, e) => Close();

            btnPanel.Children.Add(btnImprimer);
            btnPanel.Children.Add(btnFermer);

            Grid.SetRow(btnPanel, 1);
            grid.Children.Add(btnPanel);

            Content = grid;

            // ── Construire le contenu ──
            ConstruireFiche();
        }

        // ----------------------------------------------------------------
        // Construction du contenu de la fiche
        // ----------------------------------------------------------------
        private void ConstruireFiche()
        {
            var p = _fichePanel;

            // Charger les infos atelier (dynamiques, comme FenetreRecu)
            var settings     = _receiptService.GetReceiptInfoAsync().Result;
            string nomAtelier = settings?.NomAtelier ?? "RETOUCHE CHOCO";
            string telAtelier = settings?.Telephone  ?? "+226 62 11 45 11";

            // ─────────────────────────────────────────────────────
            // MENTION INTERNE (en rouge, visible immédiatement)
            // ─────────────────────────────────────────────────────
            Espace(p, 8);
            Ligne(p, SEP, 9, TextAlignment.Center, Rouge);
            Ligne(p, "  À AGRAFER SUR LE COLIS  ", 11,
                  TextAlignment.Center, Rouge, FontWeights.Bold);
            Ligne(p, "Document interne — ne pas remettre au client",
                  8, TextAlignment.Center, Rouge);
            Ligne(p, SEP, 9, TextAlignment.Center, Rouge);
            Espace(p, 8);

            // ─────────────────────────────────────────────────────
            // EN-TÊTE ATELIER
            // ─────────────────────────────────────────────────────
            Ligne(p, nomAtelier, 13, TextAlignment.Center, Noir, FontWeights.Bold);
            Ligne(p, "Tel: " + telAtelier, 9, TextAlignment.Center, Gris);
            Espace(p, 6);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);

            // ─────────────────────────────────────────────────────
            // NUMÉRO DE COMMANDE (affiché en gros — clé pour agrafage)
            // ─────────────────────────────────────────────────────
            Ligne(p, $"CMD #{_commande.IdCommande}",
                  22, TextAlignment.Center, Noir, FontWeights.Black);
            Espace(p, 4);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 8);

            // ─────────────────────────────────────────────────────
            // INFOS CLIENT + DATES
            // ─────────────────────────────────────────────────────
            string nomClient = ((_commande.Client?.Prenom ?? "") + " " +
                                (_commande.Client?.Nom    ?? "")).Trim();
            string telClient = _commande.Client?.Telephone ?? "—";

            Ligne(p, "INFORMATIONS CLIENT", 10,
                  TextAlignment.Left, Marron, FontWeights.Bold);
            Espace(p, 3);
            LigneInfo(p, "Client",   nomClient);
            LigneInfo(p, "Tél.",     telClient);
            LigneInfo(p, "Dépôt",    _commande.DateDebut.ToString("dd/MM/yyyy"));
            LigneInfo(p, "Livraison", _commande.DateFin.ToString("dd/MM/yyyy"));
            if (_commande.HeureFin.HasValue)
                LigneInfo(p, "Heure RDV",
                    _commande.HeureFin.Value.ToString(@"hh\:mm"));
            Espace(p, 8);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);

            // ─────────────────────────────────────────────────────
            // PIÈCES DE LA COMMANDE (une section par pièce)
            // ─────────────────────────────────────────────────────
            var pieces = _commande.Pieces;
            if (pieces.Count == 0)
            {
                Espace(p, 10);
                Ligne(p, "(aucune pièce enregistrée)", 10,
                      TextAlignment.Center, Gris);
            }
            else
            {
                for (int i = 0; i < pieces.Count; i++)
                {
                    var piece = pieces[i];
                    Espace(p, 8);

                    // Titre de la pièce
                    Ligne(p, $"PIÈCE {i + 1}/{pieces.Count} — {piece.TypeVetement.ToUpperInvariant()}",
                          11, TextAlignment.Left, Bleu, FontWeights.Bold);
                    Ligne(p, SEP2, 9, TextAlignment.Center, Gris);
                    Espace(p, 4);

                    // Couturier assigné
                    string couturier = piece.Couturier != null
                        ? (piece.Couturier.Prenom + " " + piece.Couturier.Nom).Trim()
                        : "— non assigné —";
                    LigneInfo(p, "Couturier", couturier);

                    // Statut
                    LigneInfo(p, "Statut", piece.StatutAffiche);

                    // Description (si renseignée)
                    if (!string.IsNullOrWhiteSpace(piece.DescriptionPrecision))
                        LigneInfo(p, "Description", piece.DescriptionPrecision);

                    Espace(p, 5);

                    // MESURES — liste complète
                    var mesures = piece.Mesures?.ToList() ?? new List<Mesure>();
                    if (mesures.Count > 0)
                    {
                        Ligne(p, "MESURES :", 10, TextAlignment.Left, Marron, FontWeights.Bold);
                        Espace(p, 2);
                        foreach (var m in mesures)
                        {
                            // Alignement pointillé : NomMesure .......... XX.X cm
                            int pts = Math.Max(3, 26 - m.NomMesure.Length - m.Valeur.Length);
                            Ligne(p,
                                "   " + m.NomMesure + " " +
                                new string('.', pts) + " " +
                                m.Valeur + " cm",
                                10, TextAlignment.Left, Noir);
                        }
                    }
                    else
                    {
                        Ligne(p, "   (aucune mesure enregistrée)", 9,
                              TextAlignment.Left, Gris);
                    }

                    Espace(p, 6);
                    if (i < pieces.Count - 1)
                        Ligne(p, SEP2, 9, TextAlignment.Center, Gris);
                }
            }

            // ─────────────────────────────────────────────────────
            // PIED DE FICHE — rappel mention interne
            // ─────────────────────────────────────────────────────
            Espace(p, 6);
            Ligne(p, SEP, 9, TextAlignment.Center, Gris);
            Espace(p, 6);
            Ligne(p, "Document interne — ne pas remettre au client",
                  8, TextAlignment.Center, Rouge);
            Ligne(p, "Imprimé le " + DateTime.Now.ToString("dd/MM/yyyy à HH:mm"),
                  8, TextAlignment.Center, Gris);
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
                    _fichePanel.Measure(new Size(290, double.PositiveInfinity));
                    _fichePanel.Arrange(
                        new Rect(new Point(0, 0), _fichePanel.DesiredSize));
                    pd.PrintVisual(_fichePanel, $"Fiche atelier CMD#{_commande.IdCommande}");
                    MessageBox.Show("Fiche atelier imprimée avec succès !",
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
        // Helpers de construction — même API que FenetreRecu
        // ----------------------------------------------------------------

        private void Ligne(StackPanel p, string texte,
            double taille = 10,
            TextAlignment align = TextAlignment.Left,
            Brush? couleur = null,
            FontWeight? poids = null)
        {
            p.Children.Add(new TextBlock
            {
                Text         = texte,
                FontFamily   = new FontFamily("Consolas"),
                FontSize     = taille,
                FontWeight   = poids ?? FontWeights.Normal,
                TextAlignment = align,
                Foreground   = couleur ?? Noir,
                TextWrapping = TextWrapping.Wrap
            });
        }

        /// <summary>Ligne "Label ......... : Valeur" alignée par pointillés.</summary>
        private void LigneInfo(StackPanel p, string label, string valeur)
        {
            const int LARGEUR_LABEL = 12;
            string lbl = label.Length >= LARGEUR_LABEL
                ? label
                : label + new string(' ', LARGEUR_LABEL - label.Length);
            Ligne(p, lbl + " : " + valeur, 10, TextAlignment.Left, Noir);
        }

        private void Espace(StackPanel p, double hauteur)
        {
            p.Children.Add(new Border { Height = hauteur });
        }

        private static Button CreerBouton(string texte, string couleurHex)
        {
            var c = (Color)ColorConverter.ConvertFromString(couleurHex);
            return new Button
            {
                Content         = texte,
                Width           = 130,
                Height          = 38,
                FontSize        = 13,
                FontWeight      = FontWeights.Bold,
                Foreground      = Brushes.White,
                Background      = new SolidColorBrush(c),
                BorderThickness = new Thickness(0),
                Cursor          = System.Windows.Input.Cursors.Hand,
                Margin          = new Thickness(6, 0, 6, 0)
            };
        }
    }
}
