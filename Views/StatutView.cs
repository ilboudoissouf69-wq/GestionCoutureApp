using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Kanban Suivi Atelier — une carte par commande, scroll global unique.
    /// Le Frame WPF a ScrollViewer désactivé (MainWindow.xaml) pour que
    /// la scrollbar de cette Page soit la seule visible.
    /// Boss/Secrétaire : lecture + modification. Couturier : lecture seule.
    ///
    /// Les changements de statut sont confirmés dans la vue avant leur envoi
    /// au service ; les rétrogradations Boss demandent un motif.
    /// </summary>
    public partial class StatutView : Page
    {
        private readonly ICommandeService _commandeService;
        private readonly IWhatsAppService _whatsApp;
        private readonly IAuthService     _authService;

        private const int PAGE_SIZE   = 20;
        private int    _currentPage   = 1;
        private string? _filtreStatut = null;
        private string  _recherche    = string.Empty;
        private readonly string _role;

        private int?  _filtreCouturierId   = null;
        private bool  _suppressCmbCouturier = false;

        private Func<string?, Task>? _actionEnAttente = null;

        private List<Commande> _itemsAfaire   = new();
        private List<Commande> _itemsEnCours  = new();
        private List<Commande> _itemsTerminee = new();
        private List<Commande> _itemsLivree   = new();

        private int _versionChargement = 0;
        private bool _pret = false;
        private readonly DispatcherTimer _rechercheTimer;

        // ──────────────────────────────────────────────────────────────
        public StatutView()
        {
            InitializeComponent();

            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _whatsApp        = App.Services.GetRequiredService<IWhatsAppService>();
            _authService     = App.Services.GetRequiredService<IAuthService>();
            _role            = _authService.UtilisateurConnecte?.Role ?? "";

            _rechercheTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _rechercheTimer.Tick += async (s, e) =>
            {
                _rechercheTimer.Stop();
                _currentPage = 1;
                await ChargerCommandes();
            };

            Loaded += async (s, e) =>
            {
                if (_role == "Couturier")
                    PanelFiltreCouturier.Visibility = Visibility.Collapsed;

                RemplirFiltreCouturier();
                await ChargerCommandes();

                _commandeService.CommandeChanged -= OnCommandeChanged;
                _commandeService.CommandeChanged += OnCommandeChanged;
                _pret = true;
            };

            Unloaded += (s, e) =>
            {
                _pret = false;
                _rechercheTimer.Stop();
                _commandeService.CommandeChanged -= OnCommandeChanged;
            };
        }

        // ── Filtre couturier ──────────────────────────────────────────
        private void RemplirFiltreCouturier()
        {
            _suppressCmbCouturier = true;
            try
            {
                var sentinelle = new Employe { IdEmploye = 0, Prenom = "Tous les couturiers", Nom = "", Role = "", Statut = "" };
                List<Employe> couturiers = new();
                try
                {
                    var ctxFactory = App.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Data.ApplicationDbContext>>();
                    using var ctx = ctxFactory.CreateDbContext();
                    couturiers = ctx.Employes
                        .Where(e => e.Statut == "Actif" && (e.Role == "Couturier" || e.Role == "Boss"))
                        .OrderBy(e => e.Prenom)
                        .Select(e => new Employe
                        {
                            IdEmploye = e.IdEmploye,
                            Nom       = e.Nom,
                            Prenom    = e.Role == "Boss" ? e.Prenom + " (Boss)" : e.Prenom,
                            Role      = e.Role,
                            Statut    = e.Statut
                        }).ToList();
                }
                catch (Exception ex)
                {
                    // Erreur non bloquante : la liste restera vide mais l'écran fonctionnera
                    System.Diagnostics.Debug.WriteLine($"[StatutView] Erreur chargement couturiers : {ex.Message}");
                }

                var liste = new List<Employe> { sentinelle };
                liste.AddRange(couturiers);
                CmbFiltreCouturier.ItemsSource   = liste;
                CmbFiltreCouturier.SelectedIndex = 0;
                _filtreCouturierId = null;
            }
            finally { _suppressCmbCouturier = false; }
        }

        // ── CommandeChanged ───────────────────────────────────────────
        private async void OnCommandeChanged(object? sender, CommandeChangedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () => await ChargerCommandes());

            if (e.TypeChangement == "PieceTerminee")
            {
                var cmd = _commandeService.ObtenirParId(e.IdCommande);
                if (cmd?.Client != null && !string.IsNullOrWhiteSpace(cmd.Client.Telephone))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var rep = MessageBox.Show(
                            $"Une pièce est terminée !\n\nClient : {cmd.Client.Prenom} {cmd.Client.Nom}\n" +
                            "Envoyer un message WhatsApp ?",
                            "Pièce terminée", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (rep == MessageBoxResult.Yes)
                            _ = _whatsApp.NotifierCommandePreteAsync(cmd);
                    });
                }
            }
        }

        private (int id, string nom) OperateurConnecte()
        {
            var op = _authService.UtilisateurConnecte;
            return op != null ? (op.IdEmploye, $"{op.Prenom} {op.Nom}".Trim()) : (0, string.Empty);
        }

        // ── Chargement Kanban ─────────────────────────────────────────
        private async Task ChargerCommandes()
        {
            int maVersion = System.Threading.Interlocked.Increment(ref _versionChargement);
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                GrilleKanban.IsEnabled      = false;

                string? rech = string.IsNullOrWhiteSpace(_recherche) ? null : _recherche;
                List<Commande> afaire = new(), enCours = new(), terminee = new(), livree = new();
                int tAfaire = 0, tEnCours = 0, tTerminee = 0, tLivree = 0;

                if (_filtreStatut == null)
                {
                    var t1 = _commandeService.ObtenirPageCommandesStatutAsync("A faire",  _currentPage, PAGE_SIZE, rech);
                    var t2 = _commandeService.ObtenirPageCommandesStatutAsync("En cours", _currentPage, PAGE_SIZE, rech);
                    var t3 = _commandeService.ObtenirPageCommandesStatutAsync("Terminee", _currentPage, PAGE_SIZE, rech);
                    var t4 = _commandeService.ObtenirPageCommandesStatutAsync("Livree",   _currentPage, PAGE_SIZE, rech);
                    await Task.WhenAll(t1, t2, t3, t4);
                    afaire   = t1.Result.Items; tAfaire   = t1.Result.TotalCount;
                    enCours  = t2.Result.Items; tEnCours  = t2.Result.TotalCount;
                    terminee = t3.Result.Items; tTerminee = t3.Result.TotalCount;
                    livree   = t4.Result.Items; tLivree   = t4.Result.TotalCount;
                }
                else if (_filtreStatut == "Retard")
                {
                    var r = await _commandeService.ObtenirPageCommandesStatutAsync("Retard", _currentPage, PAGE_SIZE, rech);
                    afaire  = r.Items.Where(c => c.StatutGlobal == "A faire").ToList();
                    enCours = r.Items.Where(c => c.StatutGlobal == "En cours").ToList();
                    tAfaire = afaire.Count; tEnCours = enCours.Count;
                }
                else
                {
                    var r = await _commandeService.ObtenirPageCommandesStatutAsync(_filtreStatut, _currentPage, PAGE_SIZE, rech);
                    switch (_filtreStatut)
                    {
                        case "A faire":  afaire   = r.Items; tAfaire   = r.TotalCount; break;
                        case "En cours": enCours  = r.Items; tEnCours  = r.TotalCount; break;
                        case "Terminee": terminee = r.Items; tTerminee = r.TotalCount; break;
                        case "Livree":   livree   = r.Items; tLivree   = r.TotalCount; break;
                    }
                }

                if (maVersion != _versionChargement) return;

                _itemsAfaire   = FiltreCouturier(afaire);
                _itemsEnCours  = FiltreCouturier(enCours);
                _itemsTerminee = FiltreCouturier(terminee);
                _itemsLivree   = FiltreCouturier(livree);

                ListeAfaire.ItemsSource   = _itemsAfaire;
                ListeEnCours.ItemsSource  = _itemsEnCours;
                ListeTerminee.ItemsSource = _itemsTerminee;
                ListeLivree.ItemsSource   = _itemsLivree;

                bool fc = _filtreCouturierId.HasValue && _filtreCouturierId != 0;
                TxtCompteurAfaire.Text   = fc ? _itemsAfaire.Count.ToString()   : tAfaire.ToString();
                TxtCompteurEnCours.Text  = fc ? _itemsEnCours.Count.ToString()  : tEnCours.ToString();
                TxtCompteurTerminee.Text = fc ? _itemsTerminee.Count.ToString() : tTerminee.ToString();
                TxtCompteurLivree.Text   = fc ? _itemsLivree.Count.ToString()   : tLivree.ToString();

                TxtVideAfaire.Visibility   = _itemsAfaire.Count   == 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtVideEnCours.Visibility  = _itemsEnCours.Count  == 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtVideTerminee.Visibility = _itemsTerminee.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtVideLivree.Visibility   = _itemsLivree.Count   == 0 ? Visibility.Visible : Visibility.Collapsed;

                int total = tAfaire + tEnCours + tTerminee + tLivree;
                BtnPagePrecedente.IsEnabled = _currentPage > 1;
                BtnPageSuivante.IsEnabled   = total > _currentPage * PAGE_SIZE;
                TxtPaginationInfo.Text = total == 0 ? "Aucune commande"
                    : fc ? $"Page {_currentPage} — {_itemsAfaire.Count + _itemsEnCours.Count + _itemsTerminee.Count + _itemsLivree.Count} (filtre couturier)"
                         : $"Page {_currentPage} — {total} commande(s)";
            }
            catch (Exception ex)
            {
                if (maVersion != _versionChargement) return;
                MessageBox.Show("Erreur chargement : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (maVersion == _versionChargement)
                {
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                    GrilleKanban.IsEnabled      = true;
                }
            }
        }

        private List<Commande> FiltreCouturier(List<Commande> items)
        {
            if (!_filtreCouturierId.HasValue || _filtreCouturierId == 0) return items;
            return items.Where(c => c.Pieces.Any(p => p.IdCouturier == _filtreCouturierId)).ToList();
        }

        // ── Handlers UI ───────────────────────────────────────────────
        private async void CmbFiltreCouturier_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCmbCouturier || !_pret) return;
            _filtreCouturierId = CmbFiltreCouturier.SelectedItem is Employe emp && emp.IdEmploye > 0
                ? emp.IdEmploye : null;
            _currentPage = 1;
            await ChargerCommandes();
        }

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_pret) return;
            _recherche = TxtRecherche.Text.Trim();
            _rechercheTimer.Stop();
            _rechercheTimer.Start();
        }

        private async void BtnPagePrecedente_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1) { _currentPage--; await ChargerCommandes(); }
        }

        private async void BtnPageSuivante_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await ChargerCommandes();
        }

        // ── Avancer toutes les pièces ─────────────────────────────────
        private async void BtnAvancerStatut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Commande cmd) return;
            string? suivant = cmd.StatutGlobal switch
            {
                "A faire"                => "En cours",
                "En cours"               => "Terminee",
                "Terminee"               => "Livree",
                "Terminee partiellement" => "Livree",
                _                        => null
            };
            if (suivant == null) return;
            if (!ConfirmerTransitionsStatut(cmd, suivant, out string? motif)) return;
            await ForcerStatutCommande(cmd.IdCommande, suivant, motif);
        }

        // ── Fiche atelier → Tickets commande ─────────────────────────
        private async void BtnFicheAtelier_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Commande cmd) return;
            await FenetreTicketsCommande.OuvrirAsync(cmd.IdCommande, Window.GetWindow(this));
        }

        // ── Menu ⋯ — changer statut commande ─────────────────────────
        private void BtnPasserA_Click(object sender, RoutedEventArgs e)
        {
            if (_role == "Couturier")
            {
                MessageBox.Show("Réservé au Boss et à la Secrétaire.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (sender is not Button btn || btn.Tag is not Commande cmd) return;

            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = $"Commande #{cmd.IdCommande} — {cmd.TypeVetementAffiche}", IsEnabled = false, FontWeight = FontWeights.Bold });
            menu.Items.Add(new Separator());

            void Add(string lbl, string s)
            {
                var it = new MenuItem { Header = lbl };
                it.Click += async (x, y) =>
                {
                    if (!ConfirmerTransitionsStatut(cmd, s, out string? motif)) return;
                    await ForcerStatutCommande(cmd.IdCommande, s, motif);
                };
                menu.Items.Add(it);
            }
            Add("⬜  À faire",  "A faire");
            Add("🟡  En cours",  "En cours");
            Add("🟢  Terminée",  "Terminee");
            Add("🔵  Livrée",    "Livree");

            btn.ContextMenu = menu;
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }

        private bool ConfirmerTransitionsStatut(Commande cmd, string nouveauStatut, out string? motif)
        {
            motif = null;
            var changements = cmd.Pieces
                .Where(p => p.Statut != nouveauStatut)
                .ToList();
            if (changements.Count == 0) return true;

            static int Rang(string statut) => statut switch
            {
                "A faire" => 0, "En cours" => 1, "Terminee" => 2, "Livree" => 3, _ => -1
            };
            static string Afficher(string statut) => statut switch
            {
                "A faire" => "À faire", "Terminee" => "Terminée", "Livree" => "Livrée", _ => statut
            };

            bool retrogradation = changements.Any(p => Rang(nouveauStatut) >= 0 && Rang(p.Statut) > Rang(nouveauStatut));
            if (retrogradation && changements.Any(p => p.Statut == "Livree" && Rang(nouveauStatut) < Rang(p.Statut)))
            {
                MessageBox.Show("Une pièce déjà livrée ne peut pas être rétrogradée ici. Utilisez le module Retours.",
                    "Rétrogradation impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (retrogradation && _role != "Boss")
            {
                MessageBox.Show("Seul le Boss peut rétrograder un statut. Contactez le Boss pour cette opération.",
                    "Accès réservé au Boss", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            string statutsAvant = string.Join(", ", changements.Select(p => Afficher(p.Statut)).Distinct());
            string message = $"Commande #{cmd.IdCommande} — {changements.Count} pièce(s) concernée(s)\n\n" +
                $"Statut(s) actuel(s) : {statutsAvant}\nNouveau statut : {Afficher(nouveauStatut)}\n\n";
            if (retrogradation)
                message += "Cette rétrogradation sera inscrite dans le journal d'audit et nécessite un motif.\n\n";
            else
                message += "Cette transition est définitive pour la Secrétaire. Seul le Boss peut revenir à un statut précédent, avec un motif.\n\n";
            if (nouveauStatut == "Terminee")
                message += "Le passage à « Terminée » déclenche le calcul des commissions à la clôture de période.\n\n";
            message += "Confirmer ce changement de statut ?";

            if (MessageBox.Show(message, "Confirmation du changement de statut",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;

            if (retrogradation)
            {
                motif = DemanderMotif("Motif de la rétrogradation (obligatoire)");
                if (string.IsNullOrWhiteSpace(motif)) return false;
            }
            return true;
        }

        private async Task ForcerStatutCommande(int idCommande, string statut, string? motifTransition = null)
        {
            if (_role == "Couturier")
            {
                MessageBox.Show("Réservé au Boss et à la Secrétaire.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var (idOp, nomOp) = OperateurConnecte();
            bool ok = await AvecControleLivraison(async motif =>
            {
                await Task.Run(() => _commandeService.ForcerStatutToutesPieces(
                    idCommande, statut, idOp, nomOp, motifTransition ?? motif));
            });
            if (ok) await ChargerCommandes();
        }

        // ── AvecControleLivraison — règle uniforme avec CommandesView ────
        private async Task<bool> AvecControleLivraison(Func<string?, Task> action)
        {
            try { await action(null); return true; }
            catch (LivraisonNonSoldeeException ex) when (ex.PeutForcer)
            {
                // Boss uniquement — paiements non soldés
                var msg = $"⚠️ Livraison bloquée — solde restant : {ex.ResteAPayer:N0} FCFA\n\n" +
                          "Le client n'a pas encore réglé la totalité de cette commande.\n\n" +
                          "En tant que Boss, vous pouvez forcer la livraison.\n" +
                          "Cela sera tracé dans le journal d'audit.\n\n" +
                          "Voulez-vous forcer quand même la livraison ?";
                var rep = MessageBox.Show(msg, "Livraison — Paiement incomplet",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (rep != MessageBoxResult.Yes) return false;

                string? motif = DemanderMotif("Motif de la livraison non soldée (obligatoire)");
                if (string.IsNullOrWhiteSpace(motif)) return false;

                if (!DemanderMotDePasse()) return false;

                try
                {
                    await action(motif);
                    return true;
                }
                catch (LivraisonNonSoldeeException ex2) when (!ex2.PeutForcer)
                {
                    MessageBox.Show(ex2.Message, "Livraison impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            catch (LivraisonNonSoldeeException ex) when (!ex.PeutForcer)
            {
                MessageBox.Show(
                    $"⛔ Livraison impossible — solde restant : {ex.ResteAPayer:N0} FCFA\n\n" +
                    "Le client n'a pas réglé la totalité de cette commande.\n" +
                    "Encaissez d'abord le solde dans le module Paiements,\n" +
                    "ou demandez au Boss d'autoriser la livraison.",
                    "Livraison — Paiement incomplet",
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Opération impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(ex.Message, "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private string? DemanderMotif(string titre)
        {
            string? resultat = null;
            var dialog = new Window
            {
                Title = titre, Width = 440, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, Background = System.Windows.Media.Brushes.White
            };
            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            panel.Children.Add(new TextBlock { Text = "Motif :", FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
            var txtMotif = new TextBox
            {
                Height = 70, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(txtMotif);
            var message = new TextBlock
            {
                FontSize = 12, Height = 18, Margin = new Thickness(0, 0, 0, 12),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26))
            };
            panel.Children.Add(message);
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button
            {
                Content = "Confirmer", Width = 110, Height = 36, FontSize = 13,
                FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x05, 0x96, 0x69)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var btnAnnuler = new Button
            {
                Content = "Annuler", Width = 100, Height = 36, FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Cursor = Cursors.Hand
            };
            btnOk.Click += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(txtMotif.Text)) { message.Text = "Motif obligatoire."; return; }
                resultat = txtMotif.Text.Trim();
                dialog.DialogResult = true; dialog.Close();
            };
            btnAnnuler.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            btnPanel.Children.Add(btnOk); btnPanel.Children.Add(btnAnnuler);
            panel.Children.Add(btnPanel);
            dialog.Content = panel;
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
            return resultat;
        }

        private async void BtnConfirmerLivraisonNonSoldee_Click(object sender, RoutedEventArgs e)
        {
            // Ce bouton n'est plus utilisé — la logique est dans AvecControleLivraison
            // via messagebox + motif + mot de passe. On le garde pour compatibilité XAML.
            FermerModal();
        }

        private void BtnAnnulerLivraisonNonSoldee_Click(object sender, RoutedEventArgs e) => FermerModal();

        private void FermerModal()
        {
            ModalLivraisonNonSoldee.Visibility = Visibility.Collapsed;
            TxtMotifLivraison.Text = string.Empty;
            _actionEnAttente       = null;
        }

        /// <summary>
        /// Demande le mot de passe du Boss pour confirmer une action sensible.
        /// Retourne true si le mot de passe est correct.
        /// </summary>
        private bool DemanderMotDePasse()
        {
            var dialog = new Window
            {
                Title = "Vérification d'identité Boss", Width = 400,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, Background = System.Windows.Media.Brushes.White
            };
            var mainPanel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            mainPanel.Children.Add(new TextBlock
            {
                Text = "Confirmation requise — saisissez votre mot de passe :",
                FontSize = 13, Margin = new Thickness(0, 0, 0, 14),
                TextWrapping = TextWrapping.Wrap
            });
            var passwordBox = new PasswordBox
            {
                Height = 38, FontSize = 14,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            mainPanel.Children.Add(passwordBox);
            var message = new TextBlock
            {
                Text = "", FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26)),
                Margin = new Thickness(0, 0, 0, 16), Height = 18
            };
            mainPanel.Children.Add(message);
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button
            {
                Content = "Confirmer", Width = 110, Height = 38, FontSize = 13,
                FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x05, 0x96, 0x69)),
                BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand
            };
            var btnAnnuler = new Button
            {
                Content = "Annuler", Width = 100, Height = 38, FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE2, 0xE8, 0xF0)),
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
                if (mdpValide) { dialog.DialogResult = true; dialog.Close(); }
                else { message.Text = "Mot de passe incorrect !"; passwordBox.Clear(); passwordBox.Focus(); }
            };
            btnAnnuler.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            passwordBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == Key.Enter) btnOk.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnAnnuler);
            mainPanel.Children.Add(btnPanel);
            dialog.Content = mainPanel;
            dialog.Owner = Window.GetWindow(this);
            return dialog.ShowDialog() == true;
        }

        private void Page_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && ModalLivraisonNonSoldee.Visibility == Visibility.Visible)
            {
                FermerModal();
                e.Handled = true;
            }
        }
    }
}
