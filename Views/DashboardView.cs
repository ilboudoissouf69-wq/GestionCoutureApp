using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    public partial class DashboardView : Page
    {
        private readonly ApplicationDbContext _context;
        private readonly ICommandeService _commandeService;
        private readonly IAlerteService _alerteService;
        private readonly IWhatsAppService _whatsAppService;
        private string _roleConnecte = string.Empty;

        public DashboardView()
        {
            InitializeComponent();

            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();
            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _alerteService   = App.Services.GetRequiredService<IAlerteService>();
            _whatsAppService = App.Services.GetRequiredService<IWhatsAppService>();
            Unloaded += (s, e) => _context.Dispose();

            Loaded += DashboardView_Loaded;
        }

        private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfigurerBanniere();
                ChargerCartesStats();
                await ChargerEcheancesAsync();
                ChargerGraphiqueRevenus();
                ChargerStatsCouturiers();
                ChargerDernieresCommandes();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur chargement dashboard : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Bouton action rapide "Nouvelle commande" → navigue vers CommandesView
        private void BtnNouvelleCommande_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null)
                NavigationService.Navigate(new CommandesView());
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

            // Date du jour en français
            TxtDateDuJour.Text = DateTime.Now.ToString("dddd dd MMMM yyyy",
                new System.Globalization.CultureInfo("fr-FR"));
        }

        // ------------------------------------------------------------------
        // 4 cartes de statistiques
        // ------------------------------------------------------------------
        private void ChargerCartesStats()
        {
            var aujourdhui = DateTime.Today;
            var debutMois = new DateTime(aujourdhui.Year, aujourdhui.Month, 1);
            var finMois = debutMois.AddMonths(1).AddDays(-1);

            // KPI 1 — Encaissé ce mois : uniquement les paiements NON annulés
            // sur la plage 1er du mois → aujourd'hui.
            // AsEnumerable() : Sum sur decimal non supporté par SQLite côté SQL.
            decimal caMois = _context.Paiements
                .Where(p => p.DatePaiement.Date >= debutMois
                         && p.DatePaiement.Date <= aujourdhui
                         && !p.EstAnnule)
                .AsEnumerable()
                .Sum(p => p.MontantPaye);
            TxtKpiEncaisseMois.Text = caMois.ToString("N0");

            // Ligne secondaire du KPI 1 : encaissé du jour (ancien calcul, conservé tel quel)
            decimal caJour = _context.Paiements
                .Where(p => p.DatePaiement.Date == aujourdhui && !p.EstAnnule)
                .AsEnumerable()
                .Sum(p => p.MontantPaye);
            TxtCaJour.Text = caJour.ToString("N0");

            // KPI 2 — commandes non livrées (calcul actuel conservé ; il inclut
            // les commandes terminées non livrées → sous-texte « Non livrées… »).
            // StatutGlobal est [NotMapped] et calculé depuis Pieces en mémoire :
            // on charge les commandes avec leurs pièces puis on filtre en mémoire.
            var commandesAvecPieces = _context.Commandes
                .Include(c => c.Pieces)
                .ToList();

            int enCours = commandesAvecPieces.Count(c => c.StatutGlobal != "Livree");
            TxtKpiCommandesEnCours.Text = enCours.ToString();

            // KPI 3 — retards : calcul actuel conservé (via StatutGlobal en mémoire)
            int retards = commandesAvecPieces.Count(c =>
                c.StatutGlobal != "Livree" &&
                c.DateFin != default(DateTime) &&
                c.DateFin.Date < aujourdhui);
            TxtKpiRetards.Text = retards.ToString();

            // KPI 4 — selon le rôle : Dépenses du mois (Boss) ou Clients (Secrétaire)
            if (_roleConnecte == "Boss")
            {
                CarteDepensesMois.Visibility = Visibility.Visible;
                CarteClients.Visibility = Visibility.Collapsed;

                var depenseService = App.Services.GetRequiredService<IDepenseService>();
                TxtKpiDepensesMois.Text = depenseService
                    .TotalParPeriode(debutMois, finMois)
                    .ToString("N0");
            }
            else
            {
                CarteDepensesMois.Visibility = Visibility.Collapsed;
                CarteClients.Visibility = Visibility.Visible;
                TxtTotalClients.Text = _context.Clients.Count().ToString();
            }

            // Badge "À attribuer" (pièces actives sans couturier)
            TxtAAttribuer.Text = _commandeService.CompterPiecesAAttribuer().ToString();
        }

        // ------------------------------------------------------------------
        // Prochaines échéances — source IAlerteService (aucune requête Commande)
        // ------------------------------------------------------------------
        private async Task ChargerEcheancesAsync()
        {
            var aujourdhui = DateTime.Today;

            var retards = await _alerteService.ObtenirRetards();
            var semaine = await _alerteService.ObtenirRendezVousSemaine();

            // Fusion + dédoublonnage par IdPieceCommande, comme
            // AlertesView.RepartirEtAfficher (les retards gardent la priorité).
            var toutes = retards
                .Concat(semaine)
                .GroupBy(a => a.IdPieceCommande)
                .Select(g => g.First())
                .ToList();

            // Groupement par commande (comme AlertesView.Grouper)
            var cartes = toutes
                .GroupBy(a => a.IdCommande)
                .Select(g =>
                {
                    var ordered = g.OrderBy(a => a.DateRendezVous).ToList();
                    var plusUrgente = ordered.First();
                    bool aPiecePrete = ordered.Any(a => a.PiecePrete);

                    return new EcheanceDashboardVm
                    {
                        IdCommande = g.Key,
                        NomClient = plusUrgente.NomClient,
                        Telephone = plusUrgente.Telephone,
                        DateEcheance = plusUrgente.DateRendezVous,
                        DetailPieces = string.Join(" • ", ordered.Select(a =>
                            $"{a.TypeVetement} ({a.Statut})")),
                        EstEnRetard = ordered.Any(a => a.NiveauAlerte == "retard")
                                      && !aPiecePrete,
                        EstAujourdhui = ordered.Any(a => a.DateRendezVous.Date == aujourdhui),
                        APiecePrete = aPiecePrete
                    };
                })
                // Retards d'abord (plus ancien en premier), puis aujourd'hui,
                // puis date croissante — 6 cartes maximum.
                .OrderBy(vm => vm.EstEnRetard ? 0 : vm.EstAujourdhui ? 1 : 2)
                .ThenBy(vm => vm.DateEcheance)
                .Take(6)
                .ToList();

            // La page a pu être déchargée pendant les appels asynchrones
            if (!IsLoaded) return;

            ItemsEcheancesUrgentes.ItemsSource = cartes;
            TxtZeroEcheances.Visibility = cartes.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // ------------------------------------------------------------------
        // « Voir toutes les alertes → » : déclenche le bouton de la barre
        // latérale (garde de rôle + surbrillance conservées), repli sur
        // navigation directe si la fenêtre hôte n'est pas MainWindow.
        // ------------------------------------------------------------------
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
        // 💬 Prêt — branche « commande prête » de AlertesView.BtnWhatsApp_Click
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

            // Charger la commande complète pour IWhatsAppService
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

        // ------------------------------------------------------------------
        // Graphique barres : revenus des 7 derniers jours
        // ------------------------------------------------------------------
        private void ChargerGraphiqueRevenus()
        {
            GridGraphique.Children.Clear();

            var revenus = new List<(string Jour, decimal Montant)>();
            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                // AsEnumerable() : Sum sur decimal non supporté par SQLite côté SQL
                decimal total = _context.Paiements
                    .Where(p => p.DatePaiement.Date == date && !p.EstAnnule)
                    .AsEnumerable()
                    .Sum(p => p.MontantPaye);
                string nomJour = date.ToString("ddd dd");
                revenus.Add((nomJour, total));
            }

            decimal maxMontant = revenus.Max(r => r.Montant);
            if (maxMontant == 0) maxMontant = 1;

            // Créer la grille du graphique
            var grille = new Grid();
            grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < 7; i++)
                grille.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            int rowIndex = 0;
            foreach (var (jour, montant) in revenus)
            {
                // Label jour
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

                // Barre
                double proportion = (double)montant / (double)maxMontant;
                if (proportion < 0.05 && montant > 0) proportion = 0.05;

                var barre = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    // Ambre-600 (#D97706) quand actif, Slate-200 sinon
                    Background = montant > 0
                        ? new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06))
                        : new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                    Margin = new Thickness(0, 5, 0, 5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Height = 26
                };

                // Conteneur pour la largeur proportionnelle
                var conteneur = new Grid();
                conteneur.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(proportion, GridUnitType.Star) });
                conteneur.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(barre, 0);
                conteneur.Children.Add(barre);

                // Texte montant dans la barre
                var txtMontant = new TextBlock
                {
                    Text = montant > 0 ? montant.ToString("N0") : "-",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    // Blanc sur ambre, gris sur fond vide
                    Foreground = montant > 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                barre.Child = txtMontant;

                Grid.SetRow(conteneur, rowIndex);
                Grid.SetColumn(conteneur, 1);
                grille.Children.Add(conteneur);

                rowIndex++;
            }

            GridGraphique.Children.Add(grille);
        }

        // ------------------------------------------------------------------
        // Charge par couturier — jauge 3 segments (prêtes / en cours / reste)
        // + retards et CA conservés de l'ancien tableau Performance
        // ------------------------------------------------------------------
        private void ChargerStatsCouturiers()
        {
            var aujourdhui = DateTime.Today;

            var couturiers = _context.Employes
                .Where(e => e.Role == "Couturier")
                .ToList();

            var modeles = new List<ChargeCouturierVm>();

            // IdCouturier sur Commande est déprécié (Étape 1b-i) : il est null
            // pour toutes les commandes créées après la migration multi-pièces.
            // On passe par PiecesCommande, où le couturier est désormais stocké.
            foreach (var c in couturiers)
            {
                // Une seule requête par couturier (Include Commande pour les retards)
                var pieces = _context.PiecesCommande
                    .Include(p => p.Commande)
                    .Where(p => p.IdCouturier == c.IdEmploye)
                    .ToList();

                // Pièces actives : non livrées et rattachées à une commande
                var actives = pieces
                    .Where(p => p.Statut != "Livree" && p.Commande != null)
                    .ToList();

                int terminees = actives.Count(p => p.Statut == "Terminee");
                int enCours   = actives.Count(p => p.Statut == "En cours");
                int aFaire    = actives.Count(p => p.Statut == "A faire");
                int total     = actives.Count;

                // Retards : commandes DISTINCTES en retard — même calcul que
                // l'ancien écran (une commande comptée une seule fois même si
                // le couturier y a plusieurs pièces en retard).
                int nbRetards = pieces
                    .Where(p => p.Statut != "Livree" &&
                                p.Commande != null &&
                                p.Commande.DateFin != default(DateTime) &&
                                p.Commande.DateFin.Date < aujourdhui)
                    .Select(p => p.IdCommande)
                    .Distinct()
                    .Count();

                // CA = somme des MontantCouture de TOUTES les pièces du couturier
                decimal caTotal = pieces.AsEnumerable().Sum(p => p.MontantCouture);

                var vm = new ChargeCouturierVm
                {
                    NomComplet = c.NomComplet,
                    TotalPieces = total,
                    PiecesAFaire = aFaire,
                    PiecesEnCours = enCours,
                    PiecesTerminees = terminees,
                    NbRetards = nbRetards,
                    CaTotal = caTotal
                };

                // Jauge : largeurs étoiles proportionnelles — aucune division,
                // donc 0 pièce donne une barre entièrement grise.
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
        // 5 dernières commandes
        // ------------------------------------------------------------------
        private void ChargerDernieresCommandes()
        {
            // Include(Pieces) indispensable : TypeVetementAffiche, MontantTotalCalcule
            // et StatutGlobalAffiche sont [NotMapped] et calculés depuis Pieces en mémoire.
            // Sans ce Include, les 3 colonnes afficheraient "(aucune pièce)", "0 FCFA", "A faire".
            var dernieres = _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces)
                .OrderByDescending(c => c.IdCommande)
                .Take(5)
                .AsEnumerable()          // bascule en mémoire pour les propriétés calculées
                .Select(c => new
                {
                    Client = c.Client != null ? c.Client.Nom + " " + c.Client.Prenom : "-",
                    Type = c.TypeVetementAffiche,
                    Montant = c.MontantTotalCalcule.ToString("N0") + " FCFA",
                    Statut = c.StatutGlobalAffiche,
                    DateFin = c.DateFin != default(DateTime)
                        ? c.DateFin.ToString("dd/MM/yyyy") : "-"
                })
                .ToList();

            GridDernieresCommandes.ItemsSource = dernieres;
        }
    }
}
