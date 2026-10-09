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
    /// ANOMALIE CONNUE (non corrigée) : rétrogradation Boss sans motif
    /// silencieuse — AvecControleLivraison n'intercepte que
    /// LivraisonNonSoldeeException, pas InvalidOperationException du flux.
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
                catch { }

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
            await ForcerStatutCommande(cmd.IdCommande, suivant);
        }

        // ── Fiche atelier ─────────────────────────────────────────────
        private void BtnFicheAtelier_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Commande cmd) return;
            try
            {
                var commandeComplete = _commandeService.ObtenirParId(cmd.IdCommande);
                if (commandeComplete == null)
                {
                    MessageBox.Show("Commande introuvable.",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var fiche = FicheAtelierWindow.Creer(commandeComplete);
                fiche.Owner = Window.GetWindow(this);
                fiche.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur fiche atelier : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            void Add(string lbl, string s) { var it = new MenuItem { Header = lbl }; it.Click += async (x, y) => await ForcerStatutCommande(cmd.IdCommande, s); menu.Items.Add(it); }
            Add("⬜  À faire",  "A faire");
            Add("🟡  En cours",  "En cours");
            Add("🟢  Terminée",  "Terminee");
            Add("🔵  Livrée",    "Livree");

            btn.ContextMenu = menu;
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }

        private async Task ForcerStatutCommande(int idCommande, string statut)
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
                await Task.Run(() => _commandeService.ForcerStatutToutesPieces(idCommande, statut, idOp, nomOp, motif));
            });
            if (ok) await ChargerCommandes();
        }

        // ── AvecControleLivraison ─────────────────────────────────────
        private async Task<bool> AvecControleLivraison(Func<string?, Task> action)
        {
            try { await action(null); return true; }
            catch (LivraisonNonSoldeeException ex) when (ex.PeutForcer)
            {
                _actionEnAttente         = action;
                TxtInfoSoldeRestant.Text = $"Cette commande présente un solde restant de {ex.ResteAPayer:N0} FCFA.";
                TxtMotifLivraison.Text   = string.Empty;
                ModalLivraisonNonSoldee.Visibility = Visibility.Visible;
                TxtMotifLivraison.Focus();
                return false;
            }
            catch (LivraisonNonSoldeeException ex) when (!ex.PeutForcer)
            {
                MessageBox.Show(ex.Message, "Livraison impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private async void BtnConfirmerLivraisonNonSoldee_Click(object sender, RoutedEventArgs e)
        {
            string motif = TxtMotifLivraison.Text.Trim();
            if (string.IsNullOrWhiteSpace(motif))
            {
                MessageBox.Show("Le motif est obligatoire.", "Motif manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_actionEnAttente == null) { FermerModal(); return; }
            var action = _actionEnAttente;
            FermerModal();
            try
            {
                await action(motif);
                await ChargerCommandes();
            }
            catch (LivraisonNonSoldeeException ex) when (!ex.PeutForcer)
            {
                MessageBox.Show(ex.Message, "Livraison impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "Opération impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (UnauthorizedAccessException ex) { MessageBox.Show(ex.Message, "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (Exception ex) { MessageBox.Show("Erreur : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnAnnulerLivraisonNonSoldee_Click(object sender, RoutedEventArgs e) => FermerModal();

        private void FermerModal()
        {
            ModalLivraisonNonSoldee.Visibility = Visibility.Collapsed;
            TxtMotifLivraison.Text = string.Empty;
            _actionEnAttente       = null;
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
