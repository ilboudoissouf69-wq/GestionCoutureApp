using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
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
            _context = App.Services.GetRequiredService<ApplicationDbContext>();

            TxtNomCouturier.Text = $"Bienvenue, {couturier.Prenom} {couturier.Nom}";

            ChargerDonnees();
        }

        private void ChargerDonnees()
        {
            var commandes = _context.Commandes
                .Include(c => c.Client)
                .Where(c => c.IdCouturier == _couturier.IdEmploye)
                .ToList();

            GridMesCommandes.ItemsSource = commandes;

            TxtEnCours.Text = commandes.Count(c => c.Statut == "En cours").ToString();
            TxtTerminees.Text = commandes.Count(c => c.Statut == "Terminee").ToString();
            TxtLivrees.Text = commandes.Count(c => c.Statut == "Livree").ToString();
            TxtRevenus.Text = commandes.Sum(c => c.MontantTotal).ToString("N0");
        }
    }
}