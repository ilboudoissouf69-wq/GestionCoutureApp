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
                PeriodeAffichee = c.DateDebutPeriode.ToString("dd/MM/yyyy") + " – " + c.DateFinPeriode.ToString("dd/MM/yyyy"),
                c.BaseCalcul,
                MontantAffiche = c.MontantCommission.ToString("N0"),
                PrimeAffiche   = c.PrimeQualite > 0 ? "+" + c.PrimeQualite.ToString("N0") : "—",
                TotalAffiche   = c.TotalAvecPrime.ToString("N0"),
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

            // Lire la prime zéro défaut
            if (!decimal.TryParse(TxtPrimeZeroDefaut.Text, out decimal primeZeroDefaut) || primeZeroDefaut < 0)
                primeZeroDefaut = 0m;

            DateTime dateDebut = DateDebut.SelectedDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime dateFin   = DateFin.SelectedDate   ?? DateTime.Today;

            _dernierApercu = _commissionService.CalculerApercu(
                dateDebut, dateFin, pourcentage, _surMontantEncaisse, _idCouturierSelectionne);

            // ── Enrichir chaque aperçu avec les données qualité ──────────
            try
            {
                using var ctx = _contextFactory.CreateDbContext();
                var retours = ctx.Retours
                    .Where(r => !r.EstAnnule &&
                                r.DateSignalement.Date >= dateDebut.Date &&
                                r.DateSignalement.Date <= dateFin.Date)
                    .ToList();

                foreach (var ap in _dernierApercu)
                {
                    ap.NbRetours = retours.Count(r => r.IdCouturier == ap.IdEmploye);
                    ap.TauxQualite = ap.NbCommandes > 0
                        ? Math.Round(100.0 * (ap.NbCommandes - ap.NbRetours) / ap.NbCommandes, 1)
                        : 100.0;
                    // Prime si ≥ 5 pièces et 0 retour
                    ap.PrimeQualite = (ap.NbCommandes >= 5 && ap.NbRetours == 0)
                        ? primeZeroDefaut
                        : 0m;
                }
            }
            catch { /* retours pas encore en base */ }

            decimal caTotalRetenu    = _dernierApercu.Sum(a => a.BaseCalcul);
            decimal totalCommissions = _dernierApercu.Sum(a => a.Commission);
            decimal totalPrimes      = _dernierApercu.Sum(a => a.PrimeQualite);

            // 5 cartes KPI
            decimal totalEncaisseGlobal   = _dernierApercu.Sum(a => a.TotalEncaisse);
            decimal totalMateriauxGlobal  = _dernierApercu.Sum(a => a.TotalMateriaux);
            decimal totalCoutureGlobal    = _dernierApercu.Sum(a => a.CaTotal);
            decimal resteAtelierGlobal    = totalEncaisseGlobal - totalCommissions - totalPrimes;

            TxtCaEncaisseTotal.Text   = totalEncaisseGlobal.ToString("N0");
            TxtTotalMateriauxKpi.Text = totalMateriauxGlobal.ToString("N0");
            TxtCaTotal.Text           = totalCoutureGlobal.ToString("N0");
            TxtTotalCommissions.Text  = totalCommissions.ToString("N0");
            TxtResteAtelier.Text      = resteAtelierGlobal.ToString("N0");

            // Bande primes
            if (totalPrimes > 0)
            {
                int nbEligibles = _dernierApercu.Count(a => a.PrimeQualite > 0);
                TxtTotalPrimes.Text  = totalPrimes.ToString("N0") + " FCFA";
                TxtDetailPrimes.Text = $"({nbEligibles} couturier(s) éligible(s) — zéro retour)";
            }
            else
            {
                TxtTotalPrimes.Text  = "0 FCFA";
                TxtDetailPrimes.Text = "(aucun couturier éligible sur cette période)";
            }

            GridCommissions.ItemsSource = null;
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
            decimal totalPrimes      = _dernierApercu.Sum(a => a.PrimeQualite);
            int nbPrimes             = _dernierApercu.Count(a => a.PrimeQualite > 0);

            string detailPrimes = nbPrimes > 0
                ? $"\n🎁 Prime Zéro Défaut : +{totalPrimes:N0} FCFA ({nbPrimes} couturier(s) éligible(s))"
                : "\n(Aucun couturier éligible à la prime zéro défaut)";

            var confirmation = MessageBox.Show(
                $"Vous allez enregistrer {totalCommissions:N0} FCFA de commissions pour " +
                $"{_dernierApercu.Count} couturier(s).{detailPrimes}\n\n" +
                $"Total à verser : {(totalCommissions + totalPrimes):N0} FCFA\n\n" +
                "Les commandes concernées seront verrouillées.\n\nConfirmer ?",
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
