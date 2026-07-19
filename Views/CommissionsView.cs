using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Views
{
    public partial class CommissionsView : Page
    {
        private readonly ICommissionService _commissionService;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IAuthService _authService;

        private int? _idCouturierSelectionne;
        private bool _surMontantEncaisse = true; // "Encaisse" = index 0 = recommande par defaut
        private List<ApercuCommission> _dernierApercu = new();

        public CommissionsView()
        {
            _authService = App.Services.GetRequiredService<IAuthService>();
            if (_authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            InitializeComponent();

            _commissionService = App.Services.GetRequiredService<ICommissionService>();
            _contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

            DateDebut.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateFin.SelectedDate = DateTime.Today;
            CmbBaseCalcul.SelectedIndex = 0;

            ChargerCouturiers();
            ChargerHistorique();

            Loaded += (s, e) => BtnCalculer_Click(null!, null!);
        }

        // ------------------------------------------------------------------
        // Chargement des listes
        // ------------------------------------------------------------------
        private void ChargerCouturiers()
        {
            using var context = _contextFactory.CreateDbContext();

            var couturiers = context.Employes
                .Where(emp => emp.Statut == "Actif" &&
                             (emp.Role == "Couturier" || emp.Role == "Boss"))
                .OrderBy(e => e.Nom)
                .ToList();

            var liste = new List<object>
            {
                new { IdEmploye = 0, DisplayText = "-- Tous les couturiers --" }
            };
            foreach (var c in couturiers)
                liste.Add(new { c.IdEmploye, DisplayText = c.Prenom + " " + c.Nom + " (" + c.Role + ")" });

            CmbCouturier.ItemsSource = liste;
            CmbCouturier.DisplayMemberPath = "DisplayText";
            CmbCouturier.SelectedValuePath = "IdEmploye";
            CmbCouturier.SelectedIndex = 0;
        }

        private void ChargerHistorique()
        {
            var historique = _commissionService.ObtenirHistorique();

            var affichage = historique.Select(c => new
            {
                c.IdCommission,
                DateCalculAffichee = c.DateCalcul.ToString("dd/MM/yyyy HH:mm"),
                c.NomEmployeSnapshot,
                PeriodeAffichee = c.DateDebutPeriode.ToString("dd/MM/yyyy") + " - " + c.DateFinPeriode.ToString("dd/MM/yyyy"),
                c.BaseCalcul,
                MontantAffiche = c.MontantCommission.ToString("N0"),
                c.StatutAffichage,
                c.NomOperateur
            }).ToList();

            GridHistorique.ItemsSource = affichage;
        }

        // ------------------------------------------------------------------
        // Filtres
        // ------------------------------------------------------------------
        private void CmbCouturier_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCouturier.SelectedValue is int id && id > 0)
                _idCouturierSelectionne = id;
            else
                _idCouturierSelectionne = null;

            BtnCalculer_Click(null!, null!);
        }

        private void CmbBaseCalcul_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbBaseCalcul.SelectedItem is ComboBoxItem item)
            {
                _surMontantEncaisse = item.Tag?.ToString() == "Encaisse";

                TxtAvertissementBase.Visibility = _surMontantEncaisse
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                TxtAvertissementBase.Text = _surMontantEncaisse
                    ? ""
                    : "Attention : cette base inclut le montant total des commandes même si le client n'a pas fini de payer. " +
                      "Une commission peut alors être calculée sur de l'argent que l'atelier n'a pas encore reçu.";
            }

            BtnCalculer_Click(null!, null!);
        }

        // ------------------------------------------------------------------
        // Aperçu (rien n'est enregistré)
        // ------------------------------------------------------------------
        private void BtnCalculer_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(TxtPourcentage.Text, out decimal pourcentage) || pourcentage <= 0)
            {
                MessageBox.Show("Saisissez un pourcentage valide.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                BtnEnregistrer.IsEnabled = false;
                return;
            }

            DateTime dateDebut = DateDebut.SelectedDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime dateFin = DateFin.SelectedDate ?? DateTime.Today;

            _dernierApercu = _commissionService.CalculerApercu(
                dateDebut, dateFin, pourcentage, _surMontantEncaisse, _idCouturierSelectionne);

            decimal caTotalRetenu = _dernierApercu.Sum(a => a.BaseCalcul);
            decimal totalCommissions = _dernierApercu.Sum(a => a.Commission);

            TxtCaTotal.Text = caTotalRetenu.ToString("N0");
            TxtTotalCommissions.Text = totalCommissions.ToString("N0");
            TxtResteAtelier.Text = (caTotalRetenu - totalCommissions).ToString("N0");

            GridCommissions.ItemsSource = _dernierApercu;

            BtnEnregistrer.IsEnabled = _dernierApercu.Count > 0;
        }

        // ------------------------------------------------------------------
        // Enregistrement définitif (verrouille les commandes concernées)
        // ------------------------------------------------------------------
        private void BtnEnregistrer_Click(object sender, RoutedEventArgs e)
        {
            if (_dernierApercu.Count == 0)
            {
                MessageBox.Show("Aucun aperçu à enregistrer. Cliquez d'abord sur \"Aperçu\".",
                    "Rien à enregistrer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal totalCommissions = _dernierApercu.Sum(a => a.Commission);
            var confirmation = MessageBox.Show(
                $"Vous allez enregistrer {totalCommissions:N0} FCFA de commissions pour " +
                $"{_dernierApercu.Count} couturier(s).\n\n" +
                "Les commandes concernées seront verrouillées et ne pourront plus jamais être " +
                "recomptées dans un calcul futur.\n\nConfirmer ?",
                "Confirmation d'enregistrement",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes) return;

            decimal.TryParse(TxtPourcentage.Text, out decimal pourcentage);
            DateTime dateDebut = DateDebut.SelectedDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime dateFin = DateFin.SelectedDate ?? DateTime.Today;

            try
            {
                _commissionService.EnregistrerCommissions(
                    _dernierApercu, dateDebut, dateFin, pourcentage, _surMontantEncaisse,
                    _authService.UtilisateurConnecte!.IdEmploye,
                    _authService.UtilisateurConnecte!.Prenom + " " + _authService.UtilisateurConnecte!.Nom);

                MessageBox.Show("Commissions enregistrées avec succès.", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                ChargerHistorique();
                BtnCalculer_Click(null!, null!); // rafraîchit l'aperçu (les commandes verrouillées disparaissent)
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'enregistrement : " + ex.Message, "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------------
        // Historique / annulation
        // ------------------------------------------------------------------
        private void GridHistorique_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TxtMessageHistorique.Text = string.Empty;
        }

        private void BtnAnnulerCommission_Click(object sender, RoutedEventArgs e)
        {
            if (GridHistorique.SelectedItem == null)
            {
                TxtMessageHistorique.Text = "Sélectionnez une commission dans l'historique.";
                TxtMessageHistorique.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            dynamic item = GridHistorique.SelectedItem;
            int idCommission = (int)item.IdCommission;

            if (string.IsNullOrWhiteSpace(TxtMotifAnnulation.Text))
            {
                TxtMessageHistorique.Text = "Le motif d'annulation est obligatoire.";
                TxtMessageHistorique.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            var confirmation = MessageBox.Show(
                "Annuler cette commission ? Les commandes concernées redeviendront éligibles à un futur calcul.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes) return;

            try
            {
                _commissionService.Annuler(
                    idCommission,
                    TxtMotifAnnulation.Text.Trim(),
                    _authService.UtilisateurConnecte!.Prenom + " " + _authService.UtilisateurConnecte!.Nom);

                TxtMessageHistorique.Text = "Commission annulée.";
                TxtMessageHistorique.Foreground = System.Windows.Media.Brushes.Green;
                TxtMotifAnnulation.Clear();
                ChargerHistorique();
                BtnCalculer_Click(null!, null!);
            }
            catch (Exception ex)
            {
                TxtMessageHistorique.Text = "Erreur : " + ex.Message;
                TxtMessageHistorique.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
    }
}
