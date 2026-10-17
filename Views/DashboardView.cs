using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    public partial class DashboardView : Page
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ICommandeService _commandeService;
        private readonly IAlerteService   _alerteService;
        private readonly IWhatsAppService _whatsAppService;
        private readonly IEventAggregator _eventAggregator;
        private string _roleConnecte = string.Empty;

        // Pagination du tableau "Dernières commandes"
        private const int DASH_PAGE_SIZE = 10;
        private int _dashPage = 1;
        private int _dashTotal = 0;

        // ── Rafraîchissement automatique ─────────────────────────────────

        /// <summary>Timer de sécurité : rafraîchit toutes les 60 secondes même sans événement.</summary>
        private readonly DispatcherTimer _timerPeriodique;

        /// <summary>Timer anti-rebond : attend 500 ms après le dernier événement avant de lancer le refresh.</summary>
        private readonly DispatcherTimer _timerDebounce;

        /// <summary>Handlers nommés pour pouvoir se désabonner proprement.</summary>
        private readonly Action<DomainChangedEvent> _handlerDomaine;

        // ── État UI ───────────────────────────────────────────────────────
        private bool _refreshEnCours = false;

        public DashboardView()
        {
            InitializeComponent();

            _contextFactory  = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _alerteService   = App.Services.GetRequiredService<IAlerteService>();
            _whatsAppService = App.Services.GetRequiredService<IWhatsAppService>();
            _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();

            // Anti-rebond : 500 ms après le dernier événement
            _timerDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timerDebounce.Tick += (_, _) => { _timerDebounce.Stop(); _ = RafraichirAsync(); };

            // Périodique : toutes les 60 secondes
            _timerPeriodique = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _timerPeriodique.Tick += async (_, _) => await RafraichirAsync();

            // Handler nommé pour désabonnement propre
            _handlerDomaine = _ => PlanifierRefresh();

            Loaded   += DashboardView_Loaded;
            Unloaded += DashboardView_Unloaded;
        }

        // ── Cycle de vie ─────────────────────────────────────────────────

        private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            // Abonnements événements domaine
            foreach (DomainChangedType t in Enum.GetValues<DomainChangedType>())
                _eventAggregator.Subscribe(t, _handlerDomaine);

            _timerPeriodique.Start();

            try
            {
                ConfigurerBanniere();
                await RafraichirAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur chargement dashboard : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DashboardView_Unloaded(object sender, RoutedEventArgs e)
        {
            _timerDebounce.Stop();
            _timerPeriodique.Stop();

            foreach (DomainChangedType t in Enum.GetValues<DomainChangedType>())
                _eventAggregator.Unsubscribe(t, _handlerDomaine);
        }

        /// <summary>
        /// Planifie un refresh après un délai anti-rebond de 500 ms.
        /// Peut être appelé depuis n'importe quel thread.
        /// </summary>
        private void PlanifierRefresh()
        {
            Dispatcher.BeginInvoke(() =>
            {
                _timerDebounce.Stop();
                _timerDebounce.Start();
            });
        }

        // ── Rafraîchissement principal ────────────────────────────────────

        /// <summary>
        /// Rafraîchit toutes les sections du dashboard de manière asynchrone.
        /// Ne bloque pas l'UI, ne fait pas clignoter la page, préserve le défilement.
        /// </summary>
        private async Task RafraichirAsync()
        {
            // Guard contre les appels concurrents
            if (_refreshEnCours) return;
            _refreshEnCours = true;

            using var t = GestionCoutureApp.Helpers.PerfLogger.Start("DashboardView.RafraichirAsync");
            try
            {
                // Conserver la position de défilement
                double scrollPos = 0;
                if (this.Content is ScrollViewer sv)
                    scrollPos = sv.VerticalOffset;

                await Task.Run(() =>
                {
                    // Toutes les lectures DB se font en arrière-plan
                    // On retourne des DTO sérialisables, pas des entités EF suivies
                }).ConfigureAwait(false);

                // Mise à jour UI sur le thread Dispatcher
                await Dispatcher.InvokeAsync(async () =>
                {
                    using var tUI = GestionCoutureApp.Helpers.PerfLogger.Start("DashboardView.UI");
                    ChargerCartesStats();
                    tUI.Checkpoint("CartesStats");
                    await ChargerEcheancesAsync();
                    tUI.Checkpoint("Echeances");
                    ChargerGraphiqueRevenus();
                    tUI.Checkpoint("GraphiqueRevenus");
                    ChargerStatsCouturiers();
                    tUI.Checkpoint("StatsCouturiers");
                    ChargerCarteAntiFraude();
                    tUI.Checkpoint("AntiFraude");
                    await ChargerStatsRetardAsync(); // Étape 7
                    tUI.Checkpoint("StatsRetard");
                    ChargerDernieresCommandes();
                    tUI.Checkpoint("DernieresCommandes");

                    MettreAJourHorodatage();

                    // Restaurer position de défilement
                    if (this.Content is ScrollViewer scrollViewer)
                        scrollViewer.ScrollToVerticalOffset(scrollPos);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DashboardView.RafraichirAsync] " + ex.Message);
            }
            finally
            {
                _refreshEnCours = false;
            }
        }

        private void MettreAJourHorodatage()
        {
            if (TxtDerniereMAJ != null)
                TxtDerniereMAJ.Text = $"Mise à jour : {DateTime.Now:HH:mm:ss}";
        }

        // ── Boutons ───────────────────────────────────────────────────────

        private async void BtnActualiser_Click(object sender, RoutedEventArgs e)
        {
            await RafraichirAsync();
        }

        private void BtnNouvelleCommande_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null)
                NavigationService.Navigate(new CommandesView());
        }

        private void BtnVoirAlertes_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw && mw.BtnAlertes != null)
                mw.BtnAlertes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (NavigationService != null)
                NavigationService.Navigate(new AlertesView());
        }

        private void BtnVoirAtelier_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw && mw.BtnStatut != null)
                mw.BtnStatut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (NavigationService != null)
                NavigationService.Navigate(new StatutView());
        }

        // ------------------------------------------------------------------
        // Bannière : badge rôle + personnalisation + date du jour
        // ------------------------------------------------------------------
        private void ConfigurerBanniere()
        {
            var authService = App.Services.GetRequiredService<IAuthService>();
            var utilisateur = authService.UtilisateurConnecte;
            if (utilisateur != null)
            {
                TxtBienvenueNom.Text = $"Bonjour, {utilisateur.Prenom} {utilisateur.Nom} 👋";
                _roleConnecte = utilisateur.Role;
            }

            TxtRoleBadge.Text = "RÔLE : " + (_roleConnecte == "Secretaire"
                ? "SECRÉTAIRE"
                : _roleConnecte.ToUpperInvariant());

            TxtDateDuJour.Text = DateTime.Now.ToString("dddd dd MMMM yyyy",
                new System.Globalization.CultureInfo("fr-FR"));
        }

        // ------------------------------------------------------------------
        // 4 cartes de statistiques — formules alignées sur TresorerieService
        // ------------------------------------------------------------------
        private void ChargerCartesStats()
        {
            using var context = _contextFactory.CreateDbContext();

            var aujourdhui = DateTime.Today;
            var debutMois  = new DateTime(aujourdhui.Year, aujourdhui.Month, 1);
            var finMois    = debutMois.AddMonths(1).AddDays(-1);

            // KPI 1 — Encaissé ce mois
            // MÊME formule que TresorerieService.CalculerChiffreAffaires :
            //   SUM(Paiements.MontantPaye WHERE !EstAnnule AND date IN [debutMois, aujourd'hui])
            decimal caMois = context.Paiements
                .Where(p => p.DatePaiement.Date >= debutMois
                         && p.DatePaiement.Date <= aujourdhui
                         && !p.EstAnnule)
                .AsEnumerable()
                .Sum(p => p.MontantPaye);
            TxtKpiEncaisseMois.Text = caMois.ToString("N0");

            decimal caJour = context.Paiements
                .Where(p => p.DatePaiement.Date == aujourdhui && !p.EstAnnule)
                .AsEnumerable()
                .Sum(p => p.MontantPaye);
            TxtCaJour.Text = caJour.ToString("N0");

            // KPI 2 — Commandes non livrées (StatutGlobal est [NotMapped], calcul en mémoire)
            var commandesAvecPieces = context.Commandes
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Include(c => c.Pieces)
                .ToList();

            int enCours = commandesAvecPieces.Count(c => c.StatutGlobal != "Livree");
            TxtKpiCommandesEnCours.Text = enCours.ToString();

            // KPI 3 — Retards
            int retards = commandesAvecPieces.Count(c =>
                c.StatutGlobal != "Livree" &&
                c.DateFin != default &&
                c.DateFin.Date < aujourdhui);
            TxtKpiRetards.Text = retards.ToString();

            // KPI 4 — Dépenses du mois (Boss) ou Clients enregistrés (Secrétaire)
            if (_roleConnecte == "Boss")
            {
                CarteDepensesMois.Visibility = Visibility.Visible;
                CarteClients.Visibility      = Visibility.Collapsed;

                // MÊME formule que TresorerieService.CalculerTotalDepenses :
                //   SUM(Depenses.Montant WHERE StatutValidation == "Validee" AND date IN période)
                decimal depMois = context.Depenses
                    .Where(d => d.DateDepense.Date >= debutMois
                             && d.DateDepense.Date <= finMois
                             && d.StatutValidation == "Validee")
                    .AsEnumerable()
                    .Sum(d => d.Montant);
                TxtKpiDepensesMois.Text = depMois.ToString("N0");
            }
            else
            {
                CarteDepensesMois.Visibility = Visibility.Collapsed;
                CarteClients.Visibility      = Visibility.Visible;
                TxtTotalClients.Text = context.Clients.Count().ToString();
            }

            // Badge "À attribuer"
            TxtAAttribuer.Text = _commandeService.CompterPiecesAAttribuer().ToString();
        }

        // ------------------------------------------------------------------
        // Prochaines échéances
        // ------------------------------------------------------------------
        private async Task ChargerEcheancesAsync()
        {
            var aujourdhui = DateTime.Today;

            var retards = await _alerteService.ObtenirRetards();
            var semaine = await _alerteService.ObtenirRendezVousSemaine();

            var toutes = retards
                .Concat(semaine)
                .GroupBy(a => a.IdPieceCommande)
                .Select(g => g.First())
                .ToList();

            var cartes = toutes
                .GroupBy(a => a.IdCommande)
                .Select(g =>
                {
                    var ordered = g.OrderBy(a => a.DateRendezVous).ToList();
                    var plusUrgente = ordered.First();
                    bool aPiecePrete = ordered.Any(a => a.PiecePrete);

                    return new EcheanceDashboardVm
                    {
                        IdCommande    = g.Key,
                        NomClient     = plusUrgente.NomClient,
                        Telephone     = plusUrgente.Telephone,
                        DateEcheance  = plusUrgente.DateRendezVous,
                        DetailPieces  = string.Join(" • ", ordered.Select(a => $"{a.TypeVetement} ({a.Statut})")),
                        EstEnRetard   = ordered.Any(a => a.NiveauAlerte == "retard") && !aPiecePrete,
                        EstAujourdhui = ordered.Any(a => a.DateRendezVous.Date == aujourdhui),
                        APiecePrete   = aPiecePrete
                    };
                })
                .OrderBy(vm => vm.EstEnRetard ? 0 : vm.EstAujourdhui ? 1 : 2)
                .ThenBy(vm => vm.DateEcheance)
                .Take(6)
                .ToList();

            if (!IsLoaded) return;

            ItemsEcheancesUrgentes.ItemsSource = cartes;
            TxtZeroEcheances.Visibility = cartes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------------------------------------------
        // Graphique barres 7 jours
        // ------------------------------------------------------------------
        private void ChargerGraphiqueRevenus()
        {
            using var context = _contextFactory.CreateDbContext();
            GridGraphique.Children.Clear();

            var revenus = new List<(string Jour, decimal Montant)>();
            for (int i = 6; i >= 0; i--)
            {
                var date  = DateTime.Today.AddDays(-i);
                decimal total = context.Paiements
                    .Where(p => p.DatePaiement.Date == date && !p.EstAnnule)
                    .AsEnumerable()
                    .Sum(p => p.MontantPaye);
                revenus.Add((date.ToString("ddd dd"), total));
            }

            decimal maxMontant = revenus.Max(r => r.Montant);
            if (maxMontant == 0) maxMontant = 1;

            var grille = new Grid();
            grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 7; i++)
                grille.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            int rowIndex = 0;
            foreach (var (jour, montant) in revenus)
            {
                var label = new TextBlock
                {
                    Text = jour,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetRow(label, rowIndex);
                Grid.SetColumn(label, 0);
                grille.Children.Add(label);

                double proportion = (double)montant / (double)maxMontant;
                if (proportion < 0.05 && montant > 0) proportion = 0.05;

                var barre = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Background = montant > 0
                        ? new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06))
                        : new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                    Margin = new Thickness(0, 5, 0, 5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Height = 26,
                    Child = new TextBlock
                    {
                        Text = montant > 0 ? montant.ToString("N0") : "-",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = montant > 0
                            ? Brushes.White
                            : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 0)
                    }
                };

                var conteneur = new Grid();
                conteneur.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(proportion, GridUnitType.Star) });
                conteneur.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(barre, 0);
                conteneur.Children.Add(barre);

                Grid.SetRow(conteneur, rowIndex);
                Grid.SetColumn(conteneur, 1);
                grille.Children.Add(conteneur);
                rowIndex++;
            }

            GridGraphique.Children.Add(grille);
        }

        // ------------------------------------------------------------------
        // Charge par couturier
        // ------------------------------------------------------------------
        private void ChargerStatsCouturiers()
        {
            using var context = _contextFactory.CreateDbContext();
            var aujourdhui = DateTime.Today;

            var couturiers = context.Employes
                .Where(e => e.Role == "Couturier")
                .ToList();

            var modeles = new List<ChargeCouturierVm>();

            foreach (var c in couturiers)
            {
                var pieces = context.PiecesCommande
                    .Include(p => p.Commande)
                    .Where(p => p.IdCouturier == c.IdEmploye)
                    .ToList();

                var actives = pieces
                    .Where(p => p.Statut != "Livree" && p.Commande != null)
                    .ToList();

                int terminees = actives.Count(p => p.Statut == "Terminee");
                int enCours   = actives.Count(p => p.Statut == "En cours");
                int aFaire    = actives.Count(p => p.Statut == "A faire");
                int total     = actives.Count;

                int nbRetards = pieces
                    .Where(p => p.Statut != "Livree" &&
                                p.Commande != null &&
                                p.Commande.DateFin != default &&
                                p.Commande.DateFin.Date < aujourdhui)
                    .Select(p => p.IdCommande)
                    .Distinct()
                    .Count();

                decimal caTotal = pieces.Sum(p => p.MontantCouture);

                var vm = new ChargeCouturierVm
                {
                    NomComplet    = c.NomComplet,
                    TotalPieces   = total,
                    PiecesAFaire  = aFaire,
                    PiecesEnCours = enCours,
                    PiecesTerminees = terminees,
                    NbRetards = nbRetards,
                    CaTotal   = caTotal
                };

                if (total > 0)
                {
                    vm.LargeurTerminees = new GridLength(terminees, GridUnitType.Star);
                    vm.LargeurEnCours   = new GridLength(enCours, GridUnitType.Star);
                    vm.LargeurReste     = new GridLength(aFaire, GridUnitType.Star);
                }
                else
                {
                    vm.LargeurTerminees = new GridLength(0, GridUnitType.Star);
                    vm.LargeurEnCours   = new GridLength(0, GridUnitType.Star);
                    vm.LargeurReste     = new GridLength(1, GridUnitType.Star);
                }

                modeles.Add(vm);
            }

            ItemsChargeCouturiers.ItemsSource = modeles;
        }

        // ------------------------------------------------------------------
        // Étape 7 — Statistiques retard (Boss uniquement)
        // ------------------------------------------------------------------
        private async Task ChargerStatsRetardAsync()
        {
            if (CarteStatsRetard == null) return;

            if (_roleConnecte != "Boss")
            {
                CarteStatsRetard.Visibility = Visibility.Collapsed;
                return;
            }

            CarteStatsRetard.Visibility = Visibility.Visible;

            try
            {
                var retardService = App.Services.GetService<IRetardAlerteService>();
                if (retardService == null) return;

                var stats = await retardService.ObtenirStatsDashboardAsync();

                if (TxtStatRetardsEnCours != null)
                    TxtStatRetardsEnCours.Text = stats.NbCommandesEnRetard.ToString();
                if (TxtStatReportsMois != null)
                    TxtStatReportsMois.Text = stats.NbReportsMois.ToString();
                if (TxtStatClientsPrevenusMois != null)
                    TxtStatClientsPrevenusMois.Text = stats.NbClientsPrevenusMois.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Dashboard] ChargerStatsRetard : " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Carte anti-fraude (Boss uniquement)
        // ------------------------------------------------------------------
        private void ChargerCarteAntiFraude()
        {
            if (_roleConnecte != "Boss")
            {
                if (CarteAntiFraude != null)
                    CarteAntiFraude.Visibility = Visibility.Collapsed;
                return;
            }

            if (CarteAntiFraude != null)
                CarteAntiFraude.Visibility = Visibility.Visible;

            var auditService = App.Services.GetService<IAuditService>();
            if (auditService == null) return;

            var depuis = DateTime.UtcNow.AddDays(-30);
            var indicateurs = auditService.ObtenirIndicateurAntiFraude(depuis);

            int seuilAlerte = 3;
            try
            {
                var parametresService = App.Services.GetService<IParametresService>();
                if (parametresService != null)
                {
                    var valeur = parametresService.ObtenirValeur("seuil_alerte_antifraude")
                        .GetAwaiter().GetResult();
                    if (int.TryParse(valeur, out int s) && s > 0)
                        seuilAlerte = s;
                }
            }
            catch { }

            var vms = indicateurs.Select(i => new AntiFraudeVm
            {
                IdOperateur      = i.IdOperateur,
                NomOperateur     = i.NomOperateur,
                RoleOperateur    = i.RoleOperateur,
                Suppressions     = i.Suppressions,
                Annulations      = i.Annulations,
                ModificationsPrix = i.ModificationsPrix,
                Total            = i.Total,
                EstAuDessusSeuil = i.Total >= seuilAlerte,
                Couleur = i.Total >= seuilAlerte
                    ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
                    : new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
            }).ToList();

            if (ItemsAntiFraude != null)
                ItemsAntiFraude.ItemsSource = vms;

            if (TxtZeroAntiFraude != null)
                TxtZeroAntiFraude.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Handler clic sur une ligne anti-fraude
        private void LigneAntiFraude_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not AntiFraudeVm vm)
                return;

            var window = new JournalAuditWindow(vm.IdOperateur, vm.NomOperateur);
            window.Owner = Window.GetWindow(this);
            window.ShowDialog();
        }

        // ------------------------------------------------------------------
        // Dernières commandes — paginées
        // ------------------------------------------------------------------
        private void ChargerDernieresCommandes() => _ = ChargerDernieresCommandesAsync();

        private async Task ChargerDernieresCommandesAsync()
        {
            try
            {
                var result = await _commandeService.ObtenirPageLightAsync(_dashPage, DASH_PAGE_SIZE);
                _dashTotal = result.TotalCount;

                GridDernieresCommandes.ItemsSource = result.Items
                    .Select(c => new
                    {
                        Client  = c.Client != null ? c.Client.Nom + " " + c.Client.Prenom : "-",
                        Type    = c.TypeVetementAffiche,
                        Montant = c.MontantTotalCalcule.ToString("N0") + " FCFA",
                        Statut  = c.StatutGlobalAffiche,
                        DateFin = c.DateFin != default ? c.DateFin.ToString("dd/MM/yyyy") : "-"
                    })
                    .ToList();

                int start = _dashTotal == 0 ? 0 : (_dashPage - 1) * DASH_PAGE_SIZE + 1;
                int end   = Math.Min(_dashPage * DASH_PAGE_SIZE, _dashTotal);

                if (TxtDashPaginationInfo != null)
                    TxtDashPaginationInfo.Text = _dashTotal == 0
                        ? "Aucune commande"
                        : $"{start}–{end} / {_dashTotal}";

                if (BtnDashPagePrec != null)
                    BtnDashPagePrec.IsEnabled = _dashPage > 1;
                if (BtnDashPageSuiv != null)
                    BtnDashPageSuiv.IsEnabled = end < _dashTotal;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DashboardView.ChargerDernières: " + ex.Message);
            }
        }

        private async void BtnDashPagePrec_Click(object sender, RoutedEventArgs e)
        {
            if (_dashPage > 1) { _dashPage--; await ChargerDernieresCommandesAsync(); }
        }

        private async void BtnDashPageSuiv_Click(object sender, RoutedEventArgs e)
        {
            _dashPage++;
            await ChargerDernieresCommandesAsync();
        }

        // ------------------------------------------------------------------
        // WhatsApp "Prêt"
        // ------------------------------------------------------------------
        private async void BtnWhatsAppPret_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not EcheanceDashboardVm vm) return;

            if (string.IsNullOrWhiteSpace(vm.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone enregistré.",
                    "Numéro manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var commande = _commandeService.ObtenirParId(vm.IdCommande);
            if (commande == null)
            {
                MessageBox.Show("La commande est introuvable.",
                    "Commande manquante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (commande.Client == null)
            {
                MessageBox.Show("Le client de cette commande est introuvable.",
                    "Client manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                btn.IsEnabled = false;
                await _whatsAppService.NotifierCommandePreteAsync(commande);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur WhatsApp : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }
}
