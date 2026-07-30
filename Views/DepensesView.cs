using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    public partial class DepensesView : Page
    {
        private readonly IDepenseService _depenseService;

        public DepensesView()
        {
            InitializeComponent();

            if (!RoleAutorise()) return;

            _depenseService = App.Services.GetRequiredService<IDepenseService>();

            // Date par defaut : aujourd'hui
            DateDepense.SelectedDate = DateTime.Today;

            Loaded += (s, e) => ChargerDonnees();
        }

        private bool RoleAutorise()
        {
            var authService = App.Services.GetRequiredService<IAuthService>();
            if (authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Acces reserve au Boss.");
            return true;
        }

        private void ChargerDonnees()
        {
            try
            {
                var depenses = _depenseService.ObtenirTous();
                GridDepenses.ItemsSource = depenses;

                // Statistiques
                var maintenant = DateTime.Now;
                var debutMois = new DateTime(maintenant.Year, maintenant.Month, 1);
                var finMois = debutMois.AddMonths(1).AddDays(-1);

                var totalMois = _depenseService.TotalParPeriode(debutMois, finMois);
                var totalJour = _depenseService.TotalParPeriode(maintenant, maintenant);

                TxtTotalMois.Text = totalMois.ToString("N0");
                TxtTotalJour.Text = totalJour.ToString("N0");
                TxtNbDepenses.Text = depenses
                    .Count(d => d.DateDepense.Date >= debutMois
                             && d.DateDepense.Date <= finMois)
                    .ToString();
            }
            catch (Exception ex)
            {
                TxtMessage.Text = "Erreur : " + ex.Message;
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void BtnAjouter_Click(object sender, RoutedEventArgs e)
        {
            // Validation type
            if (CmbTypeDepense.SelectedItem == null)
            {
                TxtMessage.Text = "Selectionnez un type de depense.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            // Validation montant
            if (!decimal.TryParse(TxtMontant.Text, out decimal montant) || montant <= 0)
            {
                TxtMessage.Text = "Saisissez un montant valide (superieur a 0).";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            // Validation date
            if (DateDepense.SelectedDate == null)
            {
                TxtMessage.Text = "Selectionnez une date.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            try
            {
                var authService = App.Services.GetRequiredService<IAuthService>();
                var operateur = authService.UtilisateurConnecte;

                var depense = new Depense
                {
                    TypeDepense = ((ComboBoxItem)CmbTypeDepense.SelectedItem).Content.ToString()!,
                    Montant = montant,
                    DateDepense = DateDepense.SelectedDate.Value,
                    Description = TxtDescription.Text.Trim(),
                    NomOperateur = operateur != null
                        ? $"{operateur.Prenom} {operateur.Nom}"
                        : ""
                };

                _depenseService.Ajouter(depense);

                // Reinitialiser le formulaire
                CmbTypeDepense.SelectedIndex = -1;
                TxtMontant.Text = string.Empty;
                TxtDescription.Text = string.Empty;

                TxtMessage.Text = "Depense enregistree.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Green;

                ChargerDonnees();
            }
            catch (Exception ex)
            {
                TxtMessage.Text = "Erreur : " + ex.Message;
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (GridDepenses.SelectedItem is not Depense depense)
            {
                MessageBox.Show("Selectionnez une depense a supprimer.",
                    "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Supprimer cette depense de {depense.Montant:N0} FCFA ({depense.TypeDepense}) ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _depenseService.Supprimer(depense.IdDepense);
                TxtMessage.Text = "Depense supprimee.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Green;
                ChargerDonnees();
            }
            catch (Exception ex)
            {
                TxtMessage.Text = "Erreur : " + ex.Message;
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
    }
}