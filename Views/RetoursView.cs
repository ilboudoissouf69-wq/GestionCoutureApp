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

        public RetoursView()
        {
            InitializeComponent();

            _retourService = App.Services.GetRequiredService<IRetourService>();
            var factory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = factory.CreateDbContext();
            Unloaded += (s, e) => _context.Dispose();

            var authService = App.Services.GetRequiredService<IAuthService>();
            // Sécurité : UtilisateurConnecte peut être null si on arrive ici sans être connecté
            _utilisateur = authService.UtilisateurConnecte
                ?? throw new InvalidOperationException("Aucun utilisateur connecté.");

            try
            {
                // Charger les commandes avec pièces + client pour la modale
                _commandes = _context.Commandes
                    .Include(c => c.Client)
                    .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                    .OrderByDescending(c => c.DateDebut)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RetoursView: erreur chargement commandes: " + ex.Message);
                _commandes = new List<Commande>();
            }

            ChargerRetours();
        }

        // ==================================================================
        // Chargement & rafraîchissement
        // ==================================================================
        private void ChargerRetours()
        {
            try
            {
                _tousLesRetours = _retourService.ObtenirTous();
            }
            catch (Exception ex)
            {
                // Si les colonnes ne sont pas encore en base (migration pas encore appliquée),
                // on charge sans les includes problématiques
                System.Diagnostics.Debug.WriteLine("ChargerRetours erreur: " + ex.Message);
                try
                {
                    _tousLesRetours = ObtenirRetoursSansIncludeProblematique();
                }
                catch
                {
                    _tousLesRetours = new List<Retour>();
                }
            }
            AppliquerFiltre();
            MettreAJourBadges();
        }

        // Fallback : charge les retours sans Include sur CouturierReprise
        // (utilisé si la colonne IdCouturierReprise n'existe pas encore en base)
        private List<Retour> ObtenirRetoursSansIncludeProblematique()
        {
            var factory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            using var ctx = factory.CreateDbContext();
            return ctx.Retours
                .Include(r => r.Commande).ThenInclude(c => c!.Client)
                .Include(r => r.PieceCommande)
                .Include(r => r.Couturier)
                .OrderByDescending(r => r.DateSignalement)
                .ToList();
        }

        private void AppliquerFiltre()
        {
            // Guard : appelé avant la fin d'InitializeComponent sur certains events XAML
            if (TxtRecherche == null || CmbFiltreStatut == null || GridRetours == null) return;

            var liste = _tousLesRetours;

            // Filtre texte
            string motCle = TxtRecherche.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(motCle))
            {
                liste = liste.Where(r =>
                    r.ClientAffiche.ToLower().Contains(motCle) ||
                    (r.PieceCommande?.TypeVetement.ToLower().Contains(motCle) ?? false) ||
                    r.DescriptionProbleme.ToLower().Contains(motCle) ||
                    (r.Couturier != null && (r.Couturier.Prenom + " " + r.Couturier.Nom).ToLower().Contains(motCle))
                ).ToList();
            }

            // Filtre statut
            string statut = (CmbFiltreStatut.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tous";
            if (statut != "Tous")
            {
                var statutDb = statut switch
                {
                    "Signalé"        => "Signale",
                    "Prêt ✓"         => "Pret",
                    "Rendu au client"=> "Rendu",
                    _                => statut
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

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e) => AppliquerFiltre();
        private void CmbFiltreStatut_SelectionChanged(object sender, SelectionChangedEventArgs e) => AppliquerFiltre();

        // ==================================================================
        // Handlers boutons du tableau
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
            if (sender is not Button btn) return;
            if (btn.DataContext is not Retour retour) return;

            string prochainStatut = retour.Statut switch
            {
                "Signale"    => "En reprise",
                "En reprise" => "Prêt ✓",
                "Pret"       => "Rendu au client",
                _            => ""
            };

            if (string.IsNullOrEmpty(prochainStatut)) return;

            var r = MessageBox.Show(
                $"Faire passer ce retour à : « {prochainStatut} » ?",
                "Avancer le statut",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (r != MessageBoxResult.Yes) return;

            try
            {
                string operateur = _utilisateur.Prenom + " " + _utilisateur.Nom;
                switch (retour.Statut)
                {
                    case "Signale":
                        _retourService.DemarrerReprise(retour.IdRetour, _utilisateur.IdEmploye, operateur);
                        break;
                    case "En reprise":
                        _retourService.Resoudre(retour.IdRetour, _utilisateur.IdEmploye, operateur);
                        break;
                    case "Pret":
                        _retourService.MarquerRendu(retour.IdRetour, _utilisateur.IdEmploye, operateur);
                        break;
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
            if (sender is not Button btn) return;
            if (btn.DataContext is not Retour retour) return;

            string? motif = DemanderMotif("Motif d'annulation obligatoire");
            if (string.IsNullOrWhiteSpace(motif)) return;

            try
            {
                _retourService.Annuler(retour.IdRetour, motif,
                    _utilisateur.Prenom + " " + _utilisateur.Nom);
                ChargerRetours();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================================================================
        // Fenêtre modale — Création / Édition
        // ==================================================================
        public void OuvrirFenetreRetour(Retour? retourExistant)
        {
            bool modeEdition = retourExistant != null;

            // En création : filtrer les commandes éligibles (pièce Livrée ou Terminée)
            var commandesEligibles = modeEdition
                ? _commandes
                : _commandes.Where(c => c.Pieces.Any(p => p.Statut == "Livree" || p.Statut == "Terminee")).ToList();

            if (!modeEdition && commandesEligibles.Count == 0)
            {
                MessageBox.Show(
                    "Aucune commande avec pièce livrée ou terminée n'est disponible pour un retour.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fenetre = new Window
            {
                Title = modeEdition ? $"Retour #{retourExistant!.IdRetour} — Détails & Modification" : "🔄  Enregistrer un retour (reprise gratuite)",
                Width = 580,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                SizeToContent = SizeToContent.Height,
                Owner = Window.GetWindow(this)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 700
            };

            var root = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };

            // ── En-tête ──────────────────────────────────────────────────
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
            // SECTION 1 — PIÈCE INITIALE
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

            AjouterLabel(root, "Commande / Pièce concernée *");
            var cmbPiece = new ComboBox
            {
                Height = 36, FontSize = 13,
                Margin = new Thickness(0, 0, 0, 10),
                IsEnabled = !modeEdition
            };
            cmbPiece.DisplayMemberPath = "DisplayText";
            cmbPiece.SelectedValuePath = "IdPieceCommande";
            root.Children.Add(cmbPiece);

            // Couturier initial (lecture seule)
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

            // Montant payé (lecture seule)
            var txtMontantOrigine = new TextBlock
            {
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(txtMontantOrigine);

            int? idCouturierInitial = null;

            // Peupler les pièces quand on choisit la commande
            void ChargerPieces()
            {
                cmbPiece.ItemsSource = null;
                idCouturierInitial = null;
                txtCouturierInitial.Text = "— (sélectionnez une pièce)";
                txtMontantOrigine.Text = "";
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

            cmbCommande.SelectionChanged += (s, ev) => ChargerPieces();

            cmbPiece.SelectionChanged += (s, ev) =>
            {
                idCouturierInitial = null;
                txtCouturierInitial.Text = "—";
                if (cmbPiece.SelectedValue == null || cmbCommande.SelectedValue == null) return;
                int idCmd = (int)cmbCommande.SelectedValue;
                int idPiece = (int)cmbPiece.SelectedValue;
                var cmd = commandesEligibles.FirstOrDefault(c => c.IdCommande == idCmd);
                var piece = cmd?.Pieces.FirstOrDefault(p => p.IdPieceCommande == idPiece);
                if (piece != null)
                {
                    idCouturierInitial = piece.IdCouturier;
                    txtCouturierInitial.Text = piece.Couturier != null
                        ? $"{piece.Couturier.Prenom} {piece.Couturier.Nom}"
                        : "Non assigné";
                    txtMontantOrigine.Text = $"Montant d'origine : {piece.MontantCouture:N0} FCFA  —  Reprise : 0 FCFA (garantie)";
                }
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
            }

            // ══════════════════════════════════════════════════════════════
            // SECTION 2 — MOTIF
            // ══════════════════════════════════════════════════════════════
            AjouterSectionTitre(root, "2.  Motif du retour");

            AjouterLabel(root, "Description du problème *");
            var txtDescription = new TextBox
            {
                Height = 80, FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10, 8, 10, 8),
                Text = modeEdition ? retourExistant!.DescriptionProbleme : ""
            };
            root.Children.Add(txtDescription);

            // Photo du défaut
            AjouterLabel(root, "Photo du défaut (optionnel)");
            string cheminPhoto = modeEdition ? retourExistant!.CheminPhotoDefaut ?? "" : "";

            var panelPhoto = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            var btnPhoto = new Button
            {
                Content = "📷  Importer une photo",
                Height = 32, Padding = new Thickness(12, 0, 12, 0),
                FontSize = 12,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var lblPhoto = new TextBlock
            {
                Text = string.IsNullOrEmpty(cheminPhoto) ? "Aucune photo" : System.IO.Path.GetFileName(cheminPhoto),
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                VerticalAlignment = VerticalAlignment.Center
            };

            btnPhoto.Click += (s, ev) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Sélectionner une photo du défaut",
                    Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp"
                };
                if (dlg.ShowDialog() != true) return;
                cheminPhoto = dlg.FileName;
                lblPhoto.Text = System.IO.Path.GetFileName(cheminPhoto);
            };

            panelPhoto.Children.Add(btnPhoto);
            panelPhoto.Children.Add(lblPhoto);
            root.Children.Add(panelPhoto);

            // ══════════════════════════════════════════════════════════════
            // SECTION 3 — ATTRIBUTION & RDV
            // ══════════════════════════════════════════════════════════════
            AjouterSectionTitre(root, "3.  Attribution &amp; Rendez-vous de reprise");

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
                    if ((int)((ComboBoxItem)cmbCouturierReprise.Items[i]).Tag == retourExistant.IdCouturierReprise.Value)
                    { cmbCouturierReprise.SelectedIndex = i; break; }
                }
            }
            root.Children.Add(cmbCouturierReprise);

            // Date + heures RDV
            var gridRdv = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gridRdv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            var spDate = new StackPanel();
            spDate.Children.Add(new TextBlock { Text = "Date de rendez-vous", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)), Margin = new Thickness(0, 0, 0, 4) });
            var dpRdv = new DatePicker { Height = 36, FontSize = 13, SelectedDate = modeEdition ? retourExistant!.DateRdvReprise : null };
            spDate.Children.Add(dpRdv);
            Grid.SetColumn(spDate, 0);

            var spHdeb = new StackPanel();
            spHdeb.Children.Add(new TextBlock { Text = "Heure début", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)), Margin = new Thickness(0, 0, 0, 4) });
            var txtHdeb = new TextBox
            {
                Height = 36, FontSize = 13,
                Padding = new Thickness(10, 0, 10, 0),
                Text = modeEdition && retourExistant!.HeureDebutReprise.HasValue
                    ? retourExistant.HeureDebutReprise.Value.ToString(@"hh\:mm") : ""
            };
            spHdeb.Children.Add(txtHdeb);
            Grid.SetColumn(spHdeb, 2);

            var spHfin = new StackPanel();
            spHfin.Children.Add(new TextBlock { Text = "Heure fin", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)), Margin = new Thickness(0, 0, 0, 4) });
            var txtHfin = new TextBox
            {
                Height = 36, FontSize = 13,
                Padding = new Thickness(10, 0, 10, 0),
                Text = modeEdition && retourExistant!.HeureFinReprise.HasValue
                    ? retourExistant.HeureFinReprise.Value.ToString(@"hh\:mm") : ""
            };
            spHfin.Children.Add(txtHfin);
            Grid.SetColumn(spHfin, 4);

            gridRdv.Children.Add(spDate);
            gridRdv.Children.Add(spHdeb);
            gridRdv.Children.Add(spHfin);
            root.Children.Add(gridRdv);

            // ══════════════════════════════════════════════════════════════
            // SECTION 4 — FACTURATION (info)
            // ══════════════════════════════════════════════════════════════
            var blcFacturation = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xF7, 0xD0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 20)
            };
            var rowFactu = new StackPanel { Orientation = Orientation.Horizontal };
            rowFactu.Children.Add(new TextBlock { Text = "💰  Total à payer : ", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)), VerticalAlignment = VerticalAlignment.Center });
            rowFactu.Children.Add(new TextBlock { Text = "0 FCFA  (Reprise sous garantie — Gratuite)", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)), VerticalAlignment = VerticalAlignment.Center });
            blcFacturation.Child = rowFactu;
            root.Children.Add(blcFacturation);

            // ── Message d'erreur ─────────────────────────────────────────
            var lblErreur = new TextBlock
            {
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
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
                Content = "❌  Annuler",
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
                Content = modeEdition ? "💾  Enregistrer les modifications" : "💾  Enregistrer le retour",
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

                // Validations
                if (!modeEdition && cmbCommande.SelectedValue == null)
                { lblErreur.Text = "Sélectionnez une commande."; return; }

                if (!modeEdition && cmbPiece.SelectedValue == null)
                { lblErreur.Text = "Sélectionnez une pièce."; return; }

                if (string.IsNullOrWhiteSpace(txtDescription.Text))
                { lblErreur.Text = "La description du problème est obligatoire."; return; }

                if (!modeEdition && idCouturierInitial == null)
                { lblErreur.Text = "La pièce sélectionnée n'a pas de couturier assigné."; return; }

                // Lire le couturier reprise
                int? idCouturierReprise = null;
                if (cmbCouturierReprise.SelectedItem is ComboBoxItem item && (int)item.Tag != -1)
                    idCouturierReprise = (int)item.Tag;

                // Lire les heures
                TimeSpan? hDeb = TimeSpan.TryParse(txtHdeb.Text, out var h1) ? h1 : null;
                TimeSpan? hFin = TimeSpan.TryParse(txtHfin.Text, out var h2) ? h2 : null;

                try
                {
                    if (modeEdition)
                    {
                        retourExistant!.DescriptionProbleme = txtDescription.Text.Trim();
                        retourExistant.IdCouturierReprise = idCouturierReprise;
                        retourExistant.DateRdvReprise = dpRdv.SelectedDate;
                        retourExistant.HeureDebutReprise = hDeb;
                        retourExistant.HeureFinReprise = hFin;
                        if (!string.IsNullOrEmpty(cheminPhoto))
                            retourExistant.CheminPhotoDefaut = cheminPhoto;
                        _retourService.Modifier(retourExistant);
                        MessageBox.Show("Retour mis à jour avec succès !", "Succès",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        var retour = new Retour
                        {
                            IdCommande = (int)cmbCommande.SelectedValue,
                            IdPieceCommande = (int)cmbPiece.SelectedValue,
                            IdCouturier = idCouturierInitial!.Value,
                            IdCouturierReprise = idCouturierReprise,
                            DescriptionProbleme = txtDescription.Text.Trim(),
                            CheminPhotoDefaut = string.IsNullOrEmpty(cheminPhoto) ? null : cheminPhoto,
                            DateRdvReprise = dpRdv.SelectedDate,
                            HeureDebutReprise = hDeb,
                            HeureFinReprise = hFin,
                            Statut = "Signale",
                            IdOperateurEnregistrement = _utilisateur.IdEmploye,
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
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF5, 0xF3)),
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
            sp.Children.Add(new TextBlock { Text = "Motif :", FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
            var txt = new TextBox
            {
                Height = 70, FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 12)
            };
            sp.Children.Add(txt);
            var err = new TextBlock { Foreground = Brushes.Red, Height = 16, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(err);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button
            {
                Content = "Confirmer", Width = 110, Height = 36,
                FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var btnCancel = new Button { Content = "Annuler", Width = 90, Height = 36 };
            btnOk.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txt.Text)) { err.Text = "Motif obligatoire."; return; }
                resultat = txt.Text.Trim();
                dlg.DialogResult = true; dlg.Close();
            };
            btnCancel.Click += (s, e) => { dlg.DialogResult = false; dlg.Close(); };
            row.Children.Add(btnOk); row.Children.Add(btnCancel);
            sp.Children.Add(row);
            dlg.Content = sp;
            dlg.ShowDialog();
            return resultat;
        }
    }
}
