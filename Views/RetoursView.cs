using System.Windows;
using System.Windows.Controls;
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
        private readonly ApplicationDbContext _context;
        private readonly Employe _utilisateur;
        private List<Commande> _commandes = new();
        private List<Retour> _tousLesRetours = new();
        private bool _initialise = false;  // guard anti-event prématuré

        public RetoursView()
        {
            InitializeComponent();

            _retourService = App.Services.GetRequiredService<IRetourService>();
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
                        bool eligible = nbPieces >= 5 && nbRetours == 0;

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

            // ── Fenêtre ──────────────────────────────────────────────────
            var fenetre = new Window
            {
                Title = modeEdition
                    ? $"Retour #{retourExistant!.IdRetour} — Détails & Modification"
                    : "🔄  Nouveau retour — Reprise gratuite",
                Width = 620,
                MaxHeight = 800,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                SizeToContent = SizeToContent.Height,
                Owner = Window.GetWindow(this)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 800
            };

            var root = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };

            // ── Titre ─────────────────────────────────────────────────────
            root.Children.Add(new TextBlock
            {
                Text = modeEdition ? $"Retour #{retourExistant!.IdRetour}" : "Nouveau retour (reprise gratuite)",
                FontSize = 17, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            root.Children.Add(new Border
            {
                Height = 3, Width = 40, CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 20)
            });

            // ══════════════════════════════════════════════════════════════
            // SECTION 1 — PIÈCE CONCERNÉE
            // ══════════════════════════════════════════════════════════════
            AjouterSectionTitre(root, "1.  Pièce concernée");

            AjouterLabel(root, "Client *");
            var cmbCommande = new ComboBox
            {
                Height = 36, FontSize = 13,
                Margin = new Thickness(0, 0, 0, 10),
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
                Height = 36, FontSize = 13,
                Margin = new Thickness(0, 0, 0, 10),
                IsEnabled = !modeEdition
            };
            cmbPiece.DisplayMemberPath = "DisplayText";
            cmbPiece.SelectedValuePath = "IdPieceCommande";
            root.Children.Add(cmbPiece);

            // Couturier initial
            AjouterLabel(root, "Couturier initial (responsable)");
            var txtCouturierInitial = new TextBox
            {
                IsReadOnly = true, Height = 36, FontSize = 13,
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                Text = "— (sélectionnez une pièce)",
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(10, 0, 10, 0)
            };
            root.Children.Add(txtCouturierInitial);

            var txtMontantOrigine = new TextBlock
            {
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(txtMontantOrigine);

            // ── Photo de la pièce (affichage automatique) ─────────────────
            AjouterLabel(root, "📸  Photo de la pièce d'origine");
            var borderPhotoPiece = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF5, 0xF3)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE0, 0xDC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Height = 160,
                Margin = new Thickness(0, 0, 0, 16),
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
                Height = 80, FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 14),
                Padding = new Thickness(10, 8, 10, 8),
                Text = modeEdition ? retourExistant!.DescriptionProbleme : ""
            };
            root.Children.Add(txtDescription);

            // ── Photo du défaut — WEBCAM + IMPORT ─────────────────────────
            AjouterLabel(root, "📷  Photo du défaut (optionnel — webcam recommandée)");

            string cheminPhotoDefaut = modeEdition ? retourExistant!.CheminPhotoDefaut ?? "" : "";

            // Prévisualisation photo défaut
            var borderPhotoDefaut = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Height = 160,
                Margin = new Thickness(0, 0, 0, 8),
                ClipToBounds = true,
                Visibility = Visibility.Collapsed   // caché jusqu'à ce qu'une photo soit prise
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
                Margin = new Thickness(0, 0, 0, 16)
            };

            // Bouton Webcam (prioritaire)
            var btnWebcam = new Button
            {
                Content = "📷  Webcam",
                Height = 36, Padding = new Thickness(16, 0, 16, 0),
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0)
            };

            // Bouton Importer
            var btnImporter = new Button
            {
                Content = "📁  Importer",
                Height = 36, Padding = new Thickness(14, 0, 14, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0)
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
                Height = 36, FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
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

            // Ligne RDV : Date + H début + H fin
            var gridRdv = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            TextBlock LblRdv(string t) => new()
            {
                Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                Margin = new Thickness(0, 0, 0, 4)
            };

            TextBox TxtHeure(string valeur) => new()
            {
                Height = 36, FontSize = 13,
                Padding = new Thickness(10, 0, 10, 0),
                Text = valeur
            };

            var spDate = new StackPanel();
            spDate.Children.Add(LblRdv("Date de rendez-vous"));
            var dpRdv = new DatePicker
            {
                Height = 36, FontSize = 13,
                SelectedDate = modeEdition ? retourExistant!.DateRdvReprise : null
            };
            spDate.Children.Add(dpRdv);
            Grid.SetColumn(spDate, 0);

            var spHdeb = new StackPanel();
            spHdeb.Children.Add(LblRdv("Heure début"));
            var txtHdeb = TxtHeure(modeEdition && retourExistant!.HeureDebutReprise.HasValue
                ? retourExistant.HeureDebutReprise.Value.ToString(@"hh\:mm") : "");
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
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xF7, 0xD0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 20),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "💰  Total à payer : ",
                            FontSize = 13, FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "0 FCFA  (Reprise sous garantie — Gratuite)",
                            FontSize = 13, FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            });

            // ── Message erreur ────────────────────────────────────────────
            var lblErreur = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Height = 18, Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(lblErreur);

            // ── Boutons ───────────────────────────────────────────────────
            var panelBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnAnnuler = new Button
            {
                Content = "❌  Fermer",
                Width = 110, Height = 38, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0)
            };
            btnAnnuler.Click += (s, ev) => fenetre.Close();

            var btnEnregistrer = new Button
            {
                Content = modeEdition ? "💾  Enregistrer modifications" : "💾  Enregistrer le retour",
                Height = 38, Padding = new Thickness(16, 0, 16, 0),
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
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
            root.Children.Add(panelBtns);

            scroll.Content = root;
            fenetre.Content = scroll;
            fenetre.ShowDialog();
        }

        // ==================================================================
        // Helpers UI
        // ==================================================================
        private static void AjouterSectionTitre(StackPanel parent, string titre)
        {
            parent.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE0, 0xDC)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 6),
                Margin = new Thickness(0, 6, 0, 12),
                Child = new TextBlock
                {
                    Text = titre,
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x73, 0x55))
                }
            });
        }

        private static void AjouterLabel(StackPanel parent, string texte)
        {
            parent.Children.Add(new TextBlock
            {
                Text = texte,
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        private string? DemanderMotif(string titre)
        {
            string? resultat = null;
            var dlg = new Window
            {
                Title = titre, Width = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                SizeToContent = SizeToContent.Height,
                Owner = Window.GetWindow(this)
            };
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            sp.Children.Add(new TextBlock
            {
                Text = "Motif :", FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var txt = new TextBox
            {
                Height = 70, FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 12)
            };
            sp.Children.Add(txt);
            var err = new TextBlock
            {
                Foreground = Brushes.Red,
                Height = 16, Margin = new Thickness(0, 0, 0, 10)
            };
            sp.Children.Add(err);
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var btnOk = new Button
            {
                Content = "Confirmer", Width = 110, Height = 36,
                FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var btnCancel = new Button { Content = "Annuler", Width = 90, Height = 36 };
            btnOk.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txt.Text)) { err.Text = "Motif obligatoire."; return; }
                resultat = txt.Text.Trim();
                dlg.DialogResult = true;
                dlg.Close();
            };
            btnCancel.Click += (s, e) => { dlg.DialogResult = false; dlg.Close(); };
            row.Children.Add(btnOk);
            row.Children.Add(btnCancel);
            sp.Children.Add(row);
            dlg.Content = sp;
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
