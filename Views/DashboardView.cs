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

        // D5 : mémorise qu'un refresh a été demandé pendant qu'un autre était en cours.
        // Quand le refresh en cours se termine, il repart une seule fois.
        private bool _refreshEnAttente = false;

        // Service de log pour journaliser les erreurs de rafraîchissement (D6).
        private readonly GestionCoutureApp.Services.ILogService _logService;

        public DashboardView()
        {
            InitializeComponent();

            _contextFactory  = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _alerteService   = App.Services.GetRequiredService<IAlerteService>();
            _whatsAppService = App.Services.GetRequiredService<IWhatsAppService>();
            _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();
            // D6 : logger injecté pour journaliser les erreurs de rafraîchissement
            _logService = App.Services.GetRequiredService<GestionCoutureApp.Services.ILogService>();

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
        /// D1  : les calculs lourds s'exécutent dans Task.Run (hors thread UI).
        /// D5  : si un refresh est déjà en cours, on mémorise la demande et on repart
        ///       dès que le refresh courant se termine — pas de perte d'événement.
        /// D6  : les erreurs sont journalisées via ILogService et signalées dans l'UI.
        /// </summary>
        private async Task RafraichirAsync()
        {
            // D5 : si déjà en cours, noter qu'une nouvelle demande est arrivée
            if (_refreshEnCours)
            {
                _refreshEnAttente = true;
                return;
            }
            _refreshEnCours = true;

            using var t = GestionCoutureApp.Helpers.PerfLogger.Start("DashboardView.RafraichirAsync");
            try
            {
                // Conserver la position de défilement (thread UI)
                double scrollPos = 0;
                if (this.Content is ScrollViewer sv)
                    scrollPos = sv.VerticalOffset;

                // ── D1 : calculs dans un thread arrière-plan ─────────────────
                // Chaque ChargerXxx retourne un DTO (pas d'entité EF suivie).
                // L'UI est mise à jour seulement après, sur le Dispatcher.
                var dtosTask = Task.Run(async () =>
                {
                    var cartesDto     = await ChargerCartesStatsDtoAsync().ConfigureAwait(false);
                    var couturiersDto = await ChargerStatsCouturiersDtoAsync().ConfigureAwait(false);
                    var graphiqueDto  = await ChargerGraphiqueRevenusDtoAsync().ConfigureAwait(false);
                    var antiFraudeDto = await ChargerAntiFraudeDtoAsync().ConfigureAwait(false);
                    return (cartesDto, couturiersDto, graphiqueDto, antiFraudeDto);
                });

                // Les sections qui dépendent du Dispatcher (AlerteService est async UI-safe)
                var echeancesTask  = ChargerEcheancesAsync();
                var retardTask     = ChargerStatsRetardAsync();
                var dernieresTask  = ChargerDernieresCommandesAsync();

                // Attendre les calculs lourds en arrière-plan
                var (cartesDto, couturiersDto, graphiqueDto, antiFraudeDto) = await dtosTask.ConfigureAwait(false);

                t.Checkpoint("CalcBackground");

                // ── Mise à jour UI sur le thread Dispatcher ──────────────────
                await Dispatcher.InvokeAsync(() =>
                {
                    AppliquerCartesStatsDto(cartesDto);
                    t.Checkpoint("CartesStats");
                    AppliquerStatsCouturiersDto(couturiersDto);
                    t.Checkpoint("StatsCouturiers");
                    AppliquerGraphiqueRevenusDto(graphiqueDto);
                    t.Checkpoint("GraphiqueRevenus");
                    AppliquerAntiFraudeDto(antiFraudeDto);
                    t.Checkpoint("AntiFraude");
                });

                // Attendre les tâches async déclenchées en parallèle
                await Task.WhenAll(echeancesTask, retardTask, dernieresTask).ConfigureAwait(false);
                t.Checkpoint("AsyncSections");

                await Dispatcher.InvokeAsync(() =>
                {
                    MettreAJourHorodatage();
                    // D6 : effacer le badge "données non actualisées" si présent
                    MasquerBadgeErreur();

                    if (this.Content is ScrollViewer scrollViewer)
                        scrollViewer.ScrollToVerticalOffset(scrollPos);
                });
            }
            catch (Exception ex)
            {
                // D6 : journaliser via ILogService, signaler dans l'UI
                _logService.LogError("[DashboardView.RafraichirAsync] " + ex.Message, ex);
                await Dispatcher.InvokeAsync(() => AfficherBadgeErreur());
            }
            finally
            {
                _refreshEnCours = false;

                // D5 : une demande était en attente → on repart une seule fois
                if (_refreshEnAttente)
                {
                    _refreshEnAttente = false;
                    _ = RafraichirAsync();
                }
            }
        }

        // ── Badge d'erreur discret (D6) ───────────────────────────────────

        private void AfficherBadgeErreur()
        {
            if (TxtDerniereMAJ != null)
                TxtDerniereMAJ.Text = $"⚠ données non actualisées ({DateTime.Now:HH:mm:ss})";
        }

        private void MasquerBadgeErreur()
        {
            // Ne rien faire ici — MettreAJourHorodatage remplace le texte
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
        // 4 cartes de statistiques — séparées en calcul (DTO, hors UI) + affichage
        // D1  : calcul dans Task.Run
        // D7  : bornes exclusives (< début du lendemain) au lieu de <= AddDays(1)
        // ------------------------------------------------------------------

        /// <summary>DTO pour les 4 cartes de stats — ne contient pas d'entités EF.</summary>
        private sealed record CartesStatsDto(
            decimal CaMois, decimal CaJour,
            int EnCours, int Retards,
            decimal DepMois, int NbClients,
            int PiecesAAttribuer,
            bool IsBoss);

        private Task<CartesStatsDto> ChargerCartesStatsDtoAsync() => Task.Run(() =>
        {
            using var context = _contextFactory.CreateDbContext();

            var aujourdhui   = DateTime.Today;
            var debutMois    = new DateTime(aujourdhui.Year, aujourdhui.Month, 1);
            // D7 : borne exclusive — début du mois suivant (pas finMois.AddDays(1))
            var debutMoisSuiv = debutMois.AddMonths(1);
            var debutDemain  = aujourdhui.AddDays(1);

            // KPI 1 — Encaissé ce mois
            // D7 : DatePaiement < debutMoisSuiv (exclusif) au lieu de <= finMois.AddDays(1)
            decimal caMois = context.Paiements
                .AsNoTracking()
                .Where(p => p.DatePaiement >= debutMois && p.DatePaiement < debutMoisSuiv && !p.EstAnnule)
                .Select(p => p.MontantPaye)
                .AsEnumerable()
                .Sum();

            // D7 : encaissé aujourd'hui — borne < debutDemain (exclusif)
            decimal caJour = context.Paiements
                .AsNoTracking()
                .Where(p => p.DatePaiement >= aujourdhui && p.DatePaiement < debutDemain && !p.EstAnnule)
                .Select(p => p.MontantPaye)
                .AsEnumerable()
                .Sum();

            // KPI 2 & 3 — Commandes en cours / retards
            // Perf-fix : COUNT SQL au lieu de charger toutes les commandes + pièces
            int enCours = context.Commandes
                .AsNoTracking()
                .Count(c => !c.EstSupprimee && !c.EstAnnulee
                         && c.Pieces.Any(p => p.Statut == "A faire" || p.Statut == "En cours"));

            int retards = context.Commandes
                .AsNoTracking()
                .Count(c => !c.EstSupprimee && !c.EstAnnulee
                         && c.DateFin < aujourdhui
                         && c.Pieces.Any(p => p.Statut == "A faire" || p.Statut == "En cours"));

            // KPI 4 — Dépenses du mois (Boss) ou Clients enregistrés (Secrétaire)
            decimal depMois = 0m;
            int nbClients   = 0;
            string role     = _roleConnecte; // snapshot thread-safe (string immuable)

            if (role == "Boss")
            {
                // D7 : borne exclusive debutMoisSuiv
                depMois = context.Depenses
                    .AsNoTracking()
                    .Where(d => d.DateDepense >= debutMois && d.DateDepense < debutMoisSuiv
                             && d.StatutValidation == "Validee")
                    .Select(d => d.Montant)
                    .AsEnumerable()
                    .Sum();
            }
            else
            {
                nbClients = context.Clients.AsNoTracking().Count();
            }

            int piecesAAttribuer = _commandeService.CompterPiecesAAttribuer();

            return new CartesStatsDto(caMois, caJour, enCours, retards, depMois, nbClients,
                piecesAAttribuer, role == "Boss");
        });

        private void AppliquerCartesStatsDto(CartesStatsDto dto)
        {
            TxtKpiEncaisseMois.Text      = dto.CaMois.ToString("N0");
            TxtCaJour.Text               = dto.CaJour.ToString("N0");
            TxtKpiCommandesEnCours.Text  = dto.EnCours.ToString();
            TxtKpiRetards.Text           = dto.Retards.ToString();

            if (dto.IsBoss)
            {
                CarteDepensesMois.Visibility = Visibility.Visible;
                CarteClients.Visibility      = Visibility.Collapsed;
                TxtKpiDepensesMois.Text      = dto.DepMois.ToString("N0");
            }
            else
            {
                CarteDepensesMois.Visibility = Visibility.Collapsed;
                CarteClients.Visibility      = Visibility.Visible;
                TxtTotalClients.Text         = dto.NbClients.ToString();
            }

            TxtAAttribuer.Text = dto.PiecesAAttribuer.ToString();
        }

        // Ancienne méthode conservée pour compatibilité interne (appelée nulle part après refacto)
        // — supprimée : AppliquerCartesStatsDto la remplace intégralement.

        // ------------------------------------------------------------------
        // Prochaines échéances
        // ------------------------------------------------------------------
        private async Task ChargerEcheancesAsync()
        {
            var aujourdhui = DateTime.Today;

            var retards = await _alerteService.ObtenirRetards().ConfigureAwait(false);
            var semaine = await _alerteService.ObtenirRendezVousSemaine().ConfigureAwait(false);

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

            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsLoaded) return;
                ItemsEcheancesUrgentes.ItemsSource = cartes;
                TxtZeroEcheances.Visibility = cartes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        // ------------------------------------------------------------------
        // Graphique barres 7 jours — calcul DTO hors UI + Apply UI
        // D1  : calcul dans Task.Run
        // D7  : borne exclusive < fin7j.AddDays(1) (déjà correct pour le jour)
        // ------------------------------------------------------------------

        private sealed record GraphiqueDto(List<(string Jour, decimal Montant)> Revenus);

        private Task<GraphiqueDto> ChargerGraphiqueRevenusDtoAsync() => Task.Run(() =>
        {
            using var context = _contextFactory.CreateDbContext();

            var debut7j = DateTime.Today.AddDays(-6);
            // D7 : borne exclusive — début du lendemain de fin7j
            var borneHaute = DateTime.Today.AddDays(1);

            var paiements7j = context.Paiements
                .AsNoTracking()
                .Where(p => p.DatePaiement >= debut7j && p.DatePaiement < borneHaute && !p.EstAnnule)
                .Select(p => new { p.DatePaiement, p.MontantPaye })
                .AsEnumerable()
                .GroupBy(p => p.DatePaiement.Date)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.MontantPaye));

            var revenus = new List<(string Jour, decimal Montant)>();
            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                decimal total = paiements7j.TryGetValue(date, out var m) ? m : 0m;
                revenus.Add((date.ToString("ddd dd"), total));
            }
            return new GraphiqueDto(revenus);
        });

        private void AppliquerGraphiqueRevenusDto(GraphiqueDto dto)
        {
            GridGraphique.Children.Clear();

            var revenus = dto.Revenus;
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
        // Charge par couturier — DTO hors UI + Apply UI
        // D1  : calcul dans Task.Run
        // D3  : correction bug CommandeEstValide (voir Étape 3 — identique ici)
        // ------------------------------------------------------------------

        private sealed record ChargeCouturierDtoItem(
            string NomComplet, int TotalPieces, int PiecesAFaire, int PiecesEnCours,
            int PiecesTerminees, int NbRetards, decimal CaTotal,
            GridLength LargeurTerminees, GridLength LargeurEnCours, GridLength LargeurReste);

        private Task<List<ChargeCouturierDtoItem>> ChargerStatsCouturiersDtoAsync() => Task.Run(() =>
        {
            using var context = _contextFactory.CreateDbContext();
            var aujourdhui = DateTime.Today;

            var couturiers = context.Employes
                .AsNoTracking()
                .Where(e => e.Role == "Couturier")
                .ToList();

            // D3 : CommandeEstValide = commande non null ET non supprimée ET non annulée
            var piecesParCouturier = context.PiecesCommande
                .AsNoTracking()
                .Where(p => p.IdCouturier.HasValue)
                .Select(p => new
                {
                    p.IdCouturier,
                    p.IdPieceCommande,
                    p.IdCommande,
                    p.Statut,
                    p.MontantCouture,
                    CommandeDateFin   = p.Commande != null ? p.Commande.DateFin : (DateTime?)null,
                    // D3 bugfix : exclure les commandes supprimées ET annulées
                    CommandeEstValide = p.Commande != null
                                        && !p.Commande.EstSupprimee
                                        && !p.Commande.EstAnnulee
                })
                .ToList();

            var groupes = piecesParCouturier
                .GroupBy(p => p.IdCouturier!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var modeles = new List<ChargeCouturierDtoItem>();
            foreach (var c in couturiers)
            {
                var pieces = groupes.TryGetValue(c.IdEmploye, out var lst) ? lst : new();

                var actives = pieces
                    .Where(p => p.Statut != "Livree" && p.CommandeEstValide)
                    .ToList();

                int terminees = actives.Count(p => p.Statut == "Terminee");
                int enCours   = actives.Count(p => p.Statut == "En cours");
                int aFaire    = actives.Count(p => p.Statut == "A faire");
                int total     = actives.Count;

                int nbRetards = pieces
                    .Where(p => p.Statut != "Livree" &&
                                p.CommandeEstValide &&
                                p.CommandeDateFin.HasValue &&
                                p.CommandeDateFin.Value.Date < aujourdhui)
                    .Select(p => p.IdCommande)
                    .Distinct()
                    .Count();

                decimal caTotal = pieces.Sum(p => p.MontantCouture);

                GridLength largTerminees, largEnCours, largReste;
                if (total > 0)
                {
                    largTerminees = new GridLength(terminees, GridUnitType.Star);
                    largEnCours   = new GridLength(enCours,   GridUnitType.Star);
                    largReste     = new GridLength(aFaire,    GridUnitType.Star);
                }
                else
                {
                    largTerminees = new GridLength(0, GridUnitType.Star);
                    largEnCours   = new GridLength(0, GridUnitType.Star);
                    largReste     = new GridLength(1, GridUnitType.Star);
                }

                modeles.Add(new ChargeCouturierDtoItem(
                    c.NomComplet, total, aFaire, enCours, terminees, nbRetards, caTotal,
                    largTerminees, largEnCours, largReste));
            }
            return modeles;
        });

        private void AppliquerStatsCouturiersDto(List<ChargeCouturierDtoItem> items)
        {
            ItemsChargeCouturiers.ItemsSource = items.Select(dto => new ChargeCouturierVm
            {
                NomComplet      = dto.NomComplet,
                TotalPieces     = dto.TotalPieces,
                PiecesAFaire    = dto.PiecesAFaire,
                PiecesEnCours   = dto.PiecesEnCours,
                PiecesTerminees = dto.PiecesTerminees,
                NbRetards       = dto.NbRetards,
                CaTotal         = dto.CaTotal,
                LargeurTerminees = dto.LargeurTerminees,
                LargeurEnCours   = dto.LargeurEnCours,
                LargeurReste     = dto.LargeurReste
            }).ToList();
        }

        // ------------------------------------------------------------------
        // Étape 7 — Statistiques retard (Boss uniquement)
        // D6  : erreurs propagées plutôt que swallowed
        // ------------------------------------------------------------------
        private async Task ChargerStatsRetardAsync()
        {
            if (CarteStatsRetard == null) return;

            if (_roleConnecte != "Boss")
            {
                await Dispatcher.InvokeAsync(() =>
                    CarteStatsRetard.Visibility = Visibility.Collapsed);
                return;
            }

            var retardService = App.Services.GetService<IRetardAlerteService>();
            if (retardService == null) return;

            var stats = await retardService.ObtenirStatsDashboardAsync().ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                CarteStatsRetard.Visibility = Visibility.Visible;
                if (TxtStatRetardsEnCours != null)
                    TxtStatRetardsEnCours.Text = stats.NbCommandesEnRetard.ToString();
                if (TxtStatReportsMois != null)
                    TxtStatReportsMois.Text = stats.NbReportsMois.ToString();
                if (TxtStatClientsPrevenusMois != null)
                    TxtStatClientsPrevenusMois.Text = stats.NbClientsPrevenusMois.ToString();
            });
        }

        // ------------------------------------------------------------------
        // Carte anti-fraude (Boss uniquement)
        // D1  : calcul dans Task.Run (DTO)
        // D4  : suppression de .GetAwaiter().GetResult() → await propre
        // ------------------------------------------------------------------

        private sealed record AntiFraudeDto(List<AntiFraudeVm> Items, bool IsBoss);

        private async Task<AntiFraudeDto> ChargerAntiFraudeDtoAsync()
        {
            if (_roleConnecte != "Boss")
                return new AntiFraudeDto(new List<AntiFraudeVm>(), false);

            var auditService = App.Services.GetService<IAuditService>();
            if (auditService == null)
                return new AntiFraudeDto(new List<AntiFraudeVm>(), true);

            var depuis = DateTime.UtcNow.AddDays(-30);

            // D4 : lecture du seuil en async AVANT d'entrer dans Task.Run
            int seuilAlerte = 3;
            try
            {
                var parametresService = App.Services.GetService<IParametresService>();
                if (parametresService != null)
                {
                    // D4 : await propre, pas de .GetAwaiter().GetResult() sur le thread UI
                    var valeur = await parametresService.ObtenirValeur("seuil_alerte_antifraude")
                        .ConfigureAwait(false);
                    if (int.TryParse(valeur, out int s) && s > 0)
                        seuilAlerte = s;
                }
            }
            catch { /* paramètre absent → seuil par défaut */ }

            // Calcul des indicateurs hors thread UI
            var indicateurs = await Task.Run(() =>
                auditService.ObtenirIndicateurAntiFraude(depuis)).ConfigureAwait(false);

            var vms = indicateurs.Select(i => new AntiFraudeVm
            {
                IdOperateur        = i.IdOperateur,
                NomOperateur       = i.NomOperateur,
                RoleOperateur      = i.RoleOperateur,
                Suppressions       = i.Suppressions,
                Annulations        = i.Annulations,
                ModificationsPrix  = i.ModificationsPrix,
                TentativesRefusees = i.TentativesRefusees,
                Total              = i.Total,
                EstAuDessusSeuil   = i.Total >= seuilAlerte,
                Couleur = i.Total >= seuilAlerte
                    ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
                    : new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
            }).ToList();

            return new AntiFraudeDto(vms, true);
        }

        private void AppliquerAntiFraudeDto(AntiFraudeDto dto)
        {
            if (CarteAntiFraude == null) return;

            if (!dto.IsBoss)
            {
                CarteAntiFraude.Visibility = Visibility.Collapsed;
                return;
            }

            CarteAntiFraude.Visibility = Visibility.Visible;

            if (ItemsAntiFraude != null)
                ItemsAntiFraude.ItemsSource = dto.Items;

            if (TxtZeroAntiFraude != null)
                TxtZeroAntiFraude.Visibility = dto.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

        // Étape 10 — Bouton "Voir tout le journal" sous la carte anti-fraude
        private void BtnVoirJournalAudit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new JournalAuditWindow { Owner = Window.GetWindow(this) };
                window.ShowDialog();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(ex.Message, "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ------------------------------------------------------------------
        // Dernières commandes — paginées
        // D6  : plus de "_ =" silencieux — la Task est awaitable et les erreurs
        //       remontent à RafraichirAsync via Task.WhenAll
        // ------------------------------------------------------------------
        private async Task ChargerDernieresCommandesAsync()
        {
            // Calcul hors UI (D1)
            var result = await _commandeService.ObtenirPageLightAsync(_dashPage, DASH_PAGE_SIZE)
                .ConfigureAwait(false);

            // Mise à jour UI sur le Dispatcher
            await Dispatcher.InvokeAsync(() =>
            {
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
            });
            // D6 : plus de catch silencieux — les exceptions remontent à RafraichirAsync via Task.WhenAll
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
