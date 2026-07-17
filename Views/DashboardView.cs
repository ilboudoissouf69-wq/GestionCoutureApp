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
            _context = App.Services.GetRequiredService<ApplicationDbContext>();
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

            int enCours = _context.Commandes
                .Count(c => c.Statut != "Livree");
            TxtCommandesEnCours.Text = enCours.ToString();

            // CA du jour — uniquement les paiements NON annules
            var aujourdhui = DateTime.Today;
            double caJour = _context.Paiements
                .Where(p => p.DatePaiement.Date == aujourdhui && !p.EstAnnule)
                .Sum(p => (double?)p.MontantPaye) ?? 0;
            TxtCaJour.Text = caJour.ToString("N0");

            // Retards
            int retards = _context.Commandes
                .Count(c => c.Statut != "Livree" &&
                            c.DateFin.Date < aujourdhui &&
                            c.DateFin != default(DateTime));
            TxtRetards.Text = retards.ToString();
        }

        // ------------------------------------------------------------------
        // Graphique barres : revenus des 7 derniers jours
        // ------------------------------------------------------------------
        private void ChargerGraphiqueRevenus()
        {
            GridGraphique.Children.Clear();

            var revenus = new List<(string Jour, double Montant)>();
            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                // Uniquement les paiements valides (non annules)
                double total = _context.Paiements
                    .Where(p => p.DatePaiement.Date == date && !p.EstAnnule)
                    .Sum(p => (double?)p.MontantPaye) ?? 0;

                string nomJour = date.ToString("ddd dd");
                revenus.Add((nomJour, total));
            }

            double maxMontant = revenus.Max(r => r.Montant);
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
                double proportion = (double)(montant / maxMontant);
                if (proportion < 0.05 && montant > 0) proportion = 0.05;

                var barre = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = montant > 0
                        ? new SolidColorBrush(Color.FromRgb(46, 134, 193))
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

            var stats = couturiers.Select(c => new
            {
                Nom = c.Nom + " " + c.Prenom,
                NbCommandes = _context.Commandes.Count(cmd => cmd.IdCouturier == c.IdEmploye),
                NbTerminees = _context.Commandes.Count(cmd =>
                    cmd.IdCouturier == c.IdEmploye && cmd.Statut == "Terminee"),
                NbRetards = _context.Commandes.Count(cmd =>
                    cmd.IdCouturier == c.IdEmploye &&
                    cmd.Statut != "Livree" &&
                    cmd.DateFin != default(DateTime) &&
                    cmd.DateFin.Date < aujourdhui),
                CaTotal = _context.Commandes
                    .Where(cmd => cmd.IdCouturier == c.IdEmploye)
                    .Sum(cmd => cmd.MontantTotal)
            }).ToList();

            GridCouturiers.ItemsSource = stats;
        }

        // ------------------------------------------------------------------
        // 5 dernières commandes
        // ------------------------------------------------------------------
        private void ChargerDernieresCommandes()
        {
            var dernieres = _context.Commandes
                .Include(c => c.Client)
                .OrderByDescending(c => c.IdCommande)
                .Take(5)
                .Select(c => new
                {
                    Client = c.Client != null ? c.Client.Nom + " " + c.Client.Prenom : "-",
                    Type = c.TypeVetement,
                    Montant = c.MontantTotal,
                    Statut = c.Statut,
                    DateFin = c.DateFin != default(DateTime)
                        ? c.DateFin.ToString("dd/MM/yyyy") : "-"
                })
                .ToList();

            GridDernieresCommandes.ItemsSource = dernieres;
        }
    }
}