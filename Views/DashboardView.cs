using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;

namespace GestionCoutureApp.Views
{
    public partial class DashboardView : Page
    {
        private readonly ApplicationDbContext _context;

        public DashboardView()
        {
            InitializeComponent();

            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();
            Unloaded += (s, e) => _context.Dispose();

            Loaded += DashboardView_Loaded;
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ChargerCartesStats();
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

        // ------------------------------------------------------------------
        // 4 cartes de statistiques
        // ------------------------------------------------------------------
        private void ChargerCartesStats()
        {
            int totalClients = _context.Clients.Count();
            TxtTotalClients.Text = totalClients.ToString();

            // StatutGlobal est [NotMapped] et calculé depuis Pieces en mémoire.
            // On charge les commandes avec leurs pièces puis on filtre en mémoire.
            var commandesAvecPieces = _context.Commandes
                .Include(c => c.Pieces)
                .ToList();

            int enCours = commandesAvecPieces.Count(c => c.StatutGlobal != "Livree");
            TxtCommandesEnCours.Text = enCours.ToString();

            // CA du jour — uniquement les paiements NON annulés
            // AsEnumerable() : Sum sur decimal non supporté par SQLite côté SQL
            var aujourdhui = DateTime.Today;
            decimal caJour = _context.Paiements
                .Where(p => p.DatePaiement.Date == aujourdhui && !p.EstAnnule)
                .AsEnumerable()
                .Sum(p => p.MontantPaye);
            TxtCaJour.Text = caJour.ToString("N0");

            // Retards : pièces non terminées/livrées dont le rendez-vous est passé.
            // On passe par StatutGlobal calculé en mémoire (nécessite Pieces chargées).
            int retards = commandesAvecPieces.Count(c =>
                c.StatutGlobal != "Livree" &&
                c.DateFin != default(DateTime) &&
                c.DateFin.Date < aujourdhui);
            TxtRetards.Text = retards.ToString();
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
                    CornerRadius = new CornerRadius(4),
                    Background = montant > 0
                        ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
                        : new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                    Margin = new Thickness(0, 6, 0, 6),
                    VerticalAlignment = VerticalAlignment.Center,
                    Height = 24
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
                    Foreground = montant > 0 ? Brushes.White : Brushes.Gray,
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
        // Stats par couturier
        // ------------------------------------------------------------------
        private void ChargerStatsCouturiers()
        {
            var aujourdhui = DateTime.Today;

            var couturiers = _context.Employes
                .Where(e => e.Role == "Couturier")
                .ToList();

            // IdCouturier sur Commande est déprécié (Étape 1b-i) : il est null
            // pour toutes les commandes créées après la migration multi-pièces.
            // On passe par PiecesCommande, où le couturier est désormais stocké.
            // Une même commande peut avoir plusieurs pièces pour le même couturier
            // (ex. 3 pantalons) : on déduplique par IdCommande pour ne compter
            // chaque commande qu'une seule fois.
            var stats = couturiers.Select(c => new
            {
                Nom = c.Nom + " " + c.Prenom,

                NbCommandes = _context.PiecesCommande
                    .Where(p => p.IdCouturier == c.IdEmploye)
                    .Select(p => p.IdCommande)
                    .Distinct()
                    .Count(),

                NbTerminees = _context.PiecesCommande
                    .Where(p => p.IdCouturier == c.IdEmploye && p.Statut == "Terminee")
                    .Select(p => p.IdCommande)
                    .Distinct()
                    .Count(),

                NbRetards = _context.PiecesCommande
                    .Where(p => p.IdCouturier == c.IdEmploye &&
                                p.Statut != "Livree" &&
                                p.Commande != null &&
                                p.Commande.DateFin != default(DateTime) &&
                                p.Commande.DateFin.Date < aujourdhui)
                    .Select(p => p.IdCommande)
                    .Distinct()
                    .Count(),

                // CA = somme des MontantCouture des pièces de ce couturier
                CaTotal = _context.PiecesCommande
                    .Where(p => p.IdCouturier == c.IdEmploye)
                    .AsEnumerable()
                    .Sum(p => p.MontantCouture)
            }).ToList();

            GridCouturiers.ItemsSource = stats;
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