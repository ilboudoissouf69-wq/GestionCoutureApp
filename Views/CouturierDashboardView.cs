using System.Windows.Controls;
using GestionCoutureApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class CouturierDashboardView : Page
    {
        private readonly Models.Employe _couturier;
        private readonly ApplicationDbContext _context;

        public CouturierDashboardView(Models.Employe couturier)
        {
            InitializeComponent();
            _couturier = couturier;

            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();
            Unloaded += (s, e) => _context.Dispose();

            TxtNomCouturier.Text = $"Bienvenue, {couturier.Prenom} {couturier.Nom}";

            ChargerDonnees();
        }

        private void ChargerDonnees()
        {
            // CORRECTIF (Étape 1b-i) : Commande.IdCouturier/Statut/MontantTotal
            // ne sont plus jamais renseignés par CommandeService (dépréciés,
            // voir Commande.cs) — filtrer/sommer directement dessus ici
            // aurait affiché "0 commande, 0 FCFA" pour CHAQUE couturier,
            // silencieusement (aucune erreur, juste un tableau de bord vide).
            // On filtre maintenant via Pieces.IdCouturier, et les colonnes du
            // DataGrid (voir CouturierDashboardView.xaml) lisent les
            // propriétés calculées TypeVetementAffiche/StatutGlobalAffiche/
            // MontantTotalCalcule ajoutées sur Commande à cet effet.
            var commandes = _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces)
                .Where(c => c.Pieces.Any(p => p.IdCouturier == _couturier.IdEmploye))
                .ToList();

            GridMesCommandes.ItemsSource = commandes;

            TxtEnCours.Text = commandes.Count(c => c.StatutGlobal == "En cours").ToString();
            TxtTerminees.Text = commandes.Count(c => c.StatutGlobal == "Terminee").ToString();
            TxtLivrees.Text = commandes.Count(c => c.StatutGlobal == "Livree").ToString();
            // Revenus : uniquement la part de couture des pièces confiées à
            // CE couturier (et non le montant total de la commande, qui
            // pourrait un jour inclure les pièces d'autres couturiers).
            TxtRevenus.Text = commandes
                .SelectMany(c => c.Pieces)
                .Where(p => p.IdCouturier == _couturier.IdEmploye)
                .Sum(p => p.MontantCouture)
                .ToString("N0");
        }
    }
}