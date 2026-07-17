using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;

namespace GestionCoutureApp.Views
{
    public partial class CommissionsView : Page
    {
        private readonly ApplicationDbContext _context;
        private int? _idCouturierSelectionne;

        public CommissionsView()
        {
            InitializeComponent();
            _context = App.Services.GetRequiredService<ApplicationDbContext>();

            DateDebut.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateFin.SelectedDate = DateTime.Today;

            ChargerCouturiers();

            Loaded += (s, e) => BtnCalculer_Click(null!, null!);
        }

        private void ChargerCouturiers()
        {
            var couturiers = _context.Employes
                .Where(emp => emp.Statut == "Actif" &&
                             (emp.Role == "Couturier" || emp.Role == "Boss"))
                .OrderBy(e => e.Nom)
                .ToList();

            var liste = new List<object>();
            liste.Add(new { IdEmploye = 0, DisplayText = "-- Tous les couturiers --" });
            foreach (var c in couturiers)
                liste.Add(new { c.IdEmploye, DisplayText = c.Prenom + " " + c.Nom + " (" + c.Role + ")" });

            CmbCouturier.ItemsSource = liste;
            CmbCouturier.DisplayMemberPath = "DisplayText";
            CmbCouturier.SelectedValuePath = "IdEmploye";
            CmbCouturier.SelectedIndex = 0;
        }

        private void CmbCouturier_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCouturier.SelectedValue is int id && id > 0)
                _idCouturierSelectionne = id;
            else
                _idCouturierSelectionne = null;

            BtnCalculer_Click(null!, null!);
        }

        private void BtnCalculer_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(TxtPourcentage.Text, out double pourcentage) || pourcentage <= 0)
            {
                MessageBox.Show("Saisissez un pourcentage valide.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime dateDebut = DateDebut.SelectedDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime dateFin = DateFin.SelectedDate ?? DateTime.Today;

            // Commandes terminees ou livrees dans la periode
            var query = _context.Commandes
                .Include(c => c.Client)
                .Where(c => (c.Statut == "Terminee" || c.Statut == "Livree") &&
                            c.DateFin.Date >= dateDebut.Date &&
                            c.DateFin.Date <= dateFin.Date &&
                            c.IdCouturier.HasValue);

            // Filtrer par couturier si selectionne
            if (_idCouturierSelectionne.HasValue)
                query = query.Where(c => c.IdCouturier == _idCouturierSelectionne.Value);

            var commandes = query.ToList();

            double caTotal = commandes.Sum(c => c.MontantTotal);
            TxtCaTotal.Text = caTotal.ToString("N0");

            // Tous les couturiers actifs
            var couturiers = _context.Employes
                .Where(emp => emp.Statut == "Actif" &&
                             (emp.Role == "Couturier" || emp.Role == "Boss"))
                .ToList();

            var details = new List<object>();
            double totalCommissions = 0;

            foreach (var couturier in couturiers)
            {
                var commandesDuCouturier = commandes
                    .Where(c => c.IdCouturier == couturier.IdEmploye)
                    .ToList();

                int nbCommandes = commandesDuCouturier.Count;
                double ca = commandesDuCouturier.Sum(c => c.MontantTotal);
                double commission = ca * (pourcentage / 100);
                totalCommissions += commission;

                details.Add(new
                {
                    Nom = couturier.Prenom + " " + couturier.Nom,
                    NbCommandes = nbCommandes,
                    CaTotal = ca.ToString("N0"),
                    Commission = commission.ToString("N0")
                });
            }

            GridCommissions.ItemsSource = details;
            TxtTotalCommissions.Text = totalCommissions.ToString("N0");
            TxtResteAtelier.Text = (caTotal - totalCommissions).ToString("N0");
        }
    }
}