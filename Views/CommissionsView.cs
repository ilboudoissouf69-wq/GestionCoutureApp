using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using GestionCoutureApp.Helpers;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Views
{
    public partial class CommissionsView : Page
    {
        private readonly ICommissionService _commissionService;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IAuthService _authService;
        private readonly IParametresService _parametresService;

        private int? _idCouturierSelectionne;
        private bool _surMontantEncaisse = true; // "Encaisse" = index 0 = recommande par defaut
        private List<ApercuCommission> _dernierApercu = new();
        private const int TAILLE_PAGE_COMMISSION = 5;
        private int _pageApercu = 1;
        private int _pageHistorique = 1;
        private int _totalPagesApercu = 1;
        private int _totalPagesHistorique = 1;
        
        // ✅ Protection contre double-clic
        private bool _enCoursEnregistrement = false;
        private bool _initialise;
        private bool _dejaCharge;
        private bool _suspendreCalculFiltres;
        private int _versionCalcul;

        private sealed class CouturierOption
        {
            public int IdEmploye { get; init; }
            public string DisplayText { get; init; } = string.Empty;
        }

        private sealed class DerniereAnnulationCommission
        {
            public DateTime DateDebut { get; init; }
            public DateTime DateFin { get; init; }
        }

        private sealed class LigneDetailPieceCommission
        {
            public string Commande { get; init; } = string.Empty;
            public string Client { get; init; } = string.Empty;
            public string Telephone { get; init; } = string.Empty;
            public string Piece { get; init; } = string.Empty;
            public string Couturier { get; init; } = string.Empty;
            public string Statut { get; init; } = string.Empty;
            public string DateTerminee { get; init; } = string.Empty;
            public string Couture { get; init; } = string.Empty;
            public string Materiaux { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
        }

        public CommissionsView()
        {
            _authService = App.Services.GetRequiredService<IAuthService>();
            if (_authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            InitializeComponent();

            _commissionService = App.Services.GetRequiredService<ICommissionService>();
            _contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _parametresService = App.Services.GetRequiredService<IParametresService>();

            DateDebut.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateFin.SelectedDate = DateTime.Today;
            CmbBaseCalcul.SelectedIndex = 0;

            Loaded += async (s, e) =>
            {
                bool premierChargement = !_dejaCharge;
                int? couturierConserve = _idCouturierSelectionne;
                _initialise = false;
                try
                {
                    // Tout le travail de lecture initial se fait hors du fil graphique.
                    var parametresTask = Task.Run(async () => (
                        Taux: await _parametresService.ObtenirTauxCommissionDefaut(),
                        Prime: await _parametresService.ObtenirPrimeZeroDefaut()));
                    var couturiersTask = Task.Run(ChargerCouturiersDepuisBase);
                    int pageHistoriqueConservee = Math.Max(1, _pageHistorique);
                    var historiqueTask = Task.Run(() => _commissionService.ObtenirHistoriquePage(pageHistoriqueConservee, TAILLE_PAGE_COMMISSION));
                    var annulationTask = premierChargement
                        ? Task.Run(ChargerDerniereAnnulationDepuisBase)
                        : Task.FromResult<DerniereAnnulationCommission?>(null);
                    await Task.WhenAll(parametresTask, couturiersTask, historiqueTask, annulationTask);
                    if (!IsLoaded) return;

                    AfficherCouturiers(couturiersTask.Result, couturierConserve);
                    AfficherHistorique(historiqueTask.Result);
                    if (premierChargement)
                    {
                        TxtPourcentage.Text = parametresTask.Result.Taux.ToString("G");
                        TxtPrimeZeroDefaut.Text = parametresTask.Result.Prime.ToString("N0");
                    }

                    // Après un redémarrage, restaurer la période/couturier de la
                    // dernière commission annulée. Sans cela l'écran revenait au
                    // mois courant et les pièces libérées semblaient avoir disparu.
                    if (premierChargement && annulationTask.Result is { } annulation)
                    {
                        _suspendreCalculFiltres = true;
                        try
                        {
                            DateDebut.SelectedDate = annulation.DateDebut;
                            DateFin.SelectedDate = annulation.DateFin;
                            CmbCouturier.SelectedIndex = 0;
                            _idCouturierSelectionne = null;
                        }
                        finally
                        {
                            _suspendreCalculFiltres = false;
                        }
                    }

                    _initialise = true;
                    _dejaCharge = true;
                    await CalculerApercuAsync(reinitialiserPage: premierChargement);
                }
                catch (Exception ex)
                {
                    // Une erreur de lecture ne doit jamais fermer l'application.
                    _initialise = true;
                    TxtMessageHistorique.Text = "Chargement des commissions impossible : " + ex.GetBaseException().Message;
                    BtnEnregistrer.IsEnabled = false;
                }
            };
        }

        // ------------------------------------------------------------------
        // Chargement des listes
        // ------------------------------------------------------------------
        private List<CouturierOption> ChargerCouturiersDepuisBase()
        {
            using var context = _contextFactory.CreateDbContext();

            var couturiers = context.Employes
                .Where(emp => emp.Statut == "Actif" &&
                             (emp.Role == "Couturier" || emp.Role == "Boss"))
                .OrderBy(e => e.Nom)
                .ToList();

            var liste = new List<CouturierOption>
            {
                new() { IdEmploye = 0, DisplayText = "-- Tous les couturiers --" }
            };
            foreach (var c in couturiers)
                liste.Add(new CouturierOption
                {
                    IdEmploye = c.IdEmploye,
                    DisplayText = c.Prenom + " " + c.Nom + " (" + c.Role + ")"
                });

            return liste;
        }

        private DerniereAnnulationCommission? ChargerDerniereAnnulationDepuisBase()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Commissions
                .AsNoTracking()
                .Where(c => c.EstAnnulee && c.DateAnnulation.HasValue)
                .OrderByDescending(c => c.DateAnnulation)
                .ThenByDescending(c => c.IdCommission)
                .Select(c => new DerniereAnnulationCommission
                {
                    DateDebut = c.DateDebutPeriode,
                    DateFin = c.DateFinPeriode
                })
                .FirstOrDefault();
        }

        private void AfficherCouturiers(List<CouturierOption> liste, int? selectionConservee = null)
        {
            CmbCouturier.ItemsSource = liste;
            CmbCouturier.DisplayMemberPath = "DisplayText";
            CmbCouturier.SelectedValuePath = "IdEmploye";
            if (selectionConservee.HasValue && liste.Any(c => c.IdEmploye == selectionConservee.Value))
                CmbCouturier.SelectedValue = selectionConservee.Value;
            else
                CmbCouturier.SelectedIndex = 0;
        }

        private async void ChargerHistorique()
        {
            await ChargerHistoriquePageAsync(_pageHistorique);
        }

        private async Task ChargerHistoriquePageAsync(int page)
        {
            try
            {
                int pageDemandee = Math.Max(1, page);
                var resultat = await Task.Run(() => _commissionService.ObtenirHistoriquePage(
                    pageDemandee, TAILLE_PAGE_COMMISSION));
                if (!IsLoaded) return;
                _pageHistorique = resultat.Page;
                AfficherHistorique(resultat);
            }
            catch (Exception ex)
            {
                TxtMessageHistorique.Text = "Erreur de chargement : " + ex.Message;
            }
        }

        private void AfficherHistorique(PagedResult<Commission> page)
        {
            var affichage = page.Items.Select(c => new
            {
                c.IdCommission,
                c.IdEmploye,
                c.DateDebutPeriode,
                c.DateFinPeriode,
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
            GridHistorique.Height = Math.Max(76, 38 + page.Items.Count * 36);
            _totalPagesHistorique = Math.Max(1, page.TotalPages);
            TxtPageHistorique.Text = $"Page {_pageHistorique} / {_totalPagesHistorique} — {page.TotalCount} commission(s)";
            BtnPageHistoriquePrecedent.IsEnabled = _pageHistorique > 1;
            BtnPageHistoriqueSuivant.IsEnabled = _pageHistorique < _totalPagesHistorique;
        }

        private void BtnPageHistorique_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out int delta)) return;
            int page = Math.Clamp(_pageHistorique + delta, 1, _totalPagesHistorique);
            if (page != _pageHistorique) _ = ChargerHistoriquePageAsync(page);
        }

        private void AfficherPageApercu()
        {
            _totalPagesApercu = Math.Max(1, (int)Math.Ceiling((double)_dernierApercu.Count / TAILLE_PAGE_COMMISSION));
            _pageApercu = Math.Clamp(_pageApercu, 1, _totalPagesApercu);
            GridCommissions.ItemsSource = _dernierApercu
                .Skip((_pageApercu - 1) * TAILLE_PAGE_COMMISSION)
                .Take(TAILLE_PAGE_COMMISSION)
                .ToList();
            int elementsVisibles = Math.Min(TAILLE_PAGE_COMMISSION,
                Math.Max(0, _dernierApercu.Count - ((_pageApercu - 1) * TAILLE_PAGE_COMMISSION)));
            GridCommissions.Height = Math.Max(76, 38 + elementsVisibles * 36);
            TxtPageApercu.Text = $"Page {_pageApercu} / {_totalPagesApercu} — {_dernierApercu.Count} couturier(s)";
            BtnPageApercuPrecedent.IsEnabled = _pageApercu > 1;
            BtnPageApercuSuivant.IsEnabled = _pageApercu < _totalPagesApercu;
        }

        private void BtnPageApercu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out int delta)) return;
            _pageApercu = Math.Clamp(_pageApercu + delta, 1, _totalPagesApercu);
            AfficherPageApercu();
        }

        // ------------------------------------------------------------------
        // Filtres
        // ------------------------------------------------------------------
        private void CmbCouturier_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialise || _suspendreCalculFiltres) return;
            if (CmbCouturier.SelectedValue is int id && id > 0)
                _idCouturierSelectionne = id;
            else
                _idCouturierSelectionne = null;

            BtnCalculer_Click(null!, null!);
        }

        private void CmbBaseCalcul_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialise) return;
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
        private async void BtnCalculer_Click(object sender, RoutedEventArgs e)
        {
            if (!_initialise) return;
            await CalculerApercuAsync();
        }

        private async Task CalculerApercuAsync(bool reinitialiserPage = true)
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

            int version = Interlocked.Increment(ref _versionCalcul);
            BtnEnregistrer.IsEnabled = false;
            TxtMessageHistorique.Text = "Calcul de l'aperçu en cours…";
            List<ApercuCommission> apercu;
            try
            {
                apercu = await Task.Run(() => _commissionService.CalculerApercu(
                    dateDebut, dateFin, pourcentage, _surMontantEncaisse, _idCouturierSelectionne));
            }
            catch (Exception ex)
            {
                if (version == _versionCalcul)
                {
                    TxtMessageHistorique.Text = "Erreur de calcul : " + ex.Message;
                    BtnEnregistrer.IsEnabled = false;
                }
                return;
            }
            if (version != _versionCalcul || !IsLoaded) return;
            _dernierApercu = apercu;
            TxtMessageHistorique.Text = string.Empty;

            // Phase 2 : le calcul qualité (NbRetours, TauxQualite, PrimeQualite) est
            // désormais fait par CommissionService.CalculerApercu() — plus besoin ici.

            decimal caTotalRetenu    = _dernierApercu.Sum(a => a.BaseCalcul);
            decimal totalCommissions = _dernierApercu.Sum(a => a.Commission);
            decimal totalPrimes      = _dernierApercu.Sum(a => a.PrimeQualite);

            // KPI globaux :
            // - CaTotal    = valeur couture totale des pièces terminées (base commission)
            // - TotalEncaisse = argent couture réellement reçu (peut être < CaTotal si impayé)
            // - TotalMateriaux = matériaux (informatif)
            // - ResteAtelier = couture facturée - commissions (ce que l'atelier garde)
            //   NB : on utilise CaTotal (pas TotalEncaisse) car la commission est calculée
            //   sur le travail terminé, même si le client n'a pas encore payé.
            decimal totalEncaisseGlobal   = _dernierApercu.Sum(a => a.TotalEncaisse); // encaissé réel
            decimal totalMateriauxGlobal  = _dernierApercu.Sum(a => a.TotalMateriaux);
            decimal totalCoutureGlobal    = _dernierApercu.Sum(a => a.CaTotal);       // couture facturée
            // Reste atelier = couture facturée (pas juste encaissée) - commissions
            // car le couturier est payé sur le travail terminé
            decimal resteAtelierGlobal    = totalCoutureGlobal - totalCommissions - totalPrimes;

            // Carte 1 = total facturé au client (couture + matériaux), qu'il ait payé ou non
            // → donne une vue complète du chiffre d'affaires généré sur la période
            decimal totalFactureClient = totalCoutureGlobal + totalMateriauxGlobal;
            TxtCaEncaisseTotal.Text   = totalFactureClient.ToString("N0");
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

            if (reinitialiserPage) _pageApercu = 1;
            AfficherPageApercu();

            BtnEnregistrer.IsEnabled = _dernierApercu.Count > 0;
        }

        // ------------------------------------------------------------------
        // Enregistrement définitif (verrouille les commandes concernées)
        // ------------------------------------------------------------------
        private async void BtnEnregistrer_Click(object sender, RoutedEventArgs e)
        {
            // ✅ Protection contre double-clic
            if (_enCoursEnregistrement)
            {
                MessageBox.Show("Enregistrement en cours, veuillez patienter...",
                    "Opération en cours", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
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

            _enCoursEnregistrement = true;
            BtnEnregistrer.IsEnabled = false;
            
            try
            {
                decimal.TryParse(TxtPourcentage.Text, out decimal pourcentage);
                DateTime dateDebut = DateDebut.SelectedDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                DateTime dateFin = DateFin.SelectedDate ?? DateTime.Today;

                _commissionService.EnregistrerCommissions(
                    _dernierApercu, dateDebut, dateFin, pourcentage, _surMontantEncaisse,
                    _authService.UtilisateurConnecte!.IdEmploye,
                    _authService.UtilisateurConnecte!.Prenom + " " + _authService.UtilisateurConnecte!.Nom);

                MessageBox.Show("Commissions enregistrées avec succès.", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                await ChargerHistoriquePageAsync(_pageHistorique);
                await CalculerApercuAsync(); // Les pièces verrouillées disparaissent immédiatement de l'aperçu.
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'enregistrement : " + ex.Message, "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // ✅ Toujours réactiver le bouton
                _enCoursEnregistrement = false;
                BtnEnregistrer.IsEnabled = true;
            }
        }

        // ------------------------------------------------------------------
        // Historique / annulation
        // ------------------------------------------------------------------
        private async void GridCommissions_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridCommissions.SelectedItem is not ApercuCommission ligne) return;
            try
            {
                var pieces = await Task.Run(() => ChargerDetailsPieces(ligne.IdsPieces));
                var resume = new List<(string Label, string Valeur)>
                {
                    ("Couturier", ligne.Nom),
                    ("Période", $"{DateDebut.SelectedDate:dd/MM/yyyy} – {DateFin.SelectedDate:dd/MM/yyyy}"),
                    ("Pièces", ligne.IdsPieces.Count.ToString()),
                    ("Commandes", ligne.NbCommandes.ToString()),
                    ("Base de calcul", _surMontantEncaisse ? "Montant encaissé" : "Montant total"),
                    ("Montant de base", $"{ligne.BaseCalcul:N0} FCFA"),
                    ("Couture totale", $"{ligne.CaTotal:N0} FCFA"),
                    ("Encaissé couture", $"{ligne.CaEncaisse:N0} FCFA"),
                    ("Matériaux", $"{ligne.TotalMateriaux:N0} FCFA"),
                    ("Taux qualité", ligne.TauxQualite.ToString("0.#") + "%"),
                    ("Retours", ligne.NbRetours.ToString()),
                    ("Commission", $"{ligne.Commission:N0} FCFA"),
                    ("Prime qualité", $"{ligne.PrimeQualite:N0} FCFA"),
                    ("Total à verser", $"{ligne.TotalAvecPrime:N0} FCFA"),
                    ("Reste atelier", $"{ligne.ResteAtelier:N0} FCFA"),
                    ("Impayé client", $"{ligne.MontantImpaye:N0} FCFA")
                };
                OuvrirFenetreDetail("Détail de l’aperçu de commission", ligne.Nom, resume, pieces);
            }
            catch (Exception ex)
            {
                TxtMessageHistorique.Text = "Impossible de charger le détail : " + ex.GetBaseException().Message;
            }
        }

        private async void GridHistorique_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridHistorique.SelectedItem == null) return;
            try
            {
                dynamic selection = GridHistorique.SelectedItem;
                int idCommission = (int)selection.IdCommission;
                var resultat = await Task.Run(() => ChargerDetailsCommission(idCommission));
                if (resultat == null)
                {
                    TxtMessageHistorique.Text = "Cette commission n’existe plus dans l’historique.";
                    return;
                }
                var details = resultat.Value;
                OuvrirFenetreDetail("Détail de la commission enregistrée",
                    $"Commission #{details.Commission.IdCommission} — {details.Commission.NomEmployeSnapshot}",
                    details.Resume, details.Pieces);
            }
            catch (Exception ex)
            {
                TxtMessageHistorique.Text = "Impossible de charger le détail : " + ex.GetBaseException().Message;
            }
        }

        private List<LigneDetailPieceCommission> ChargerDetailsPieces(IEnumerable<int> idsPieces)
        {
            int[] ids = idsPieces.Distinct().ToArray();
            if (ids.Length == 0) return new List<LigneDetailPieceCommission>();
            using var context = _contextFactory.CreateDbContext();
            var pieces = context.PiecesCommande.AsNoTracking()
                .Where(p => ids.Contains(p.IdPieceCommande))
                .Include(p => p.Commande).ThenInclude(c => c!.Client)
                .Include(p => p.Couturier)
                .Include(p => p.MaterielSupplements)
                .OrderBy(p => p.IdCommande).ThenBy(p => p.IdPieceCommande)
                .ToList();
            return pieces.Select(ConstruireLigneDetailPiece).ToList();
        }

        private (Commission Commission, List<(string Label, string Valeur)> Resume,
                 List<LigneDetailPieceCommission> Pieces)? ChargerDetailsCommission(int idCommission)
        {
            using var context = _contextFactory.CreateDbContext();
            var commission = context.Commissions.AsNoTracking()
                .FirstOrDefault(c => c.IdCommission == idCommission);
            if (commission == null) return null;

            var pieces = context.PiecesCommande.AsNoTracking()
                .Where(p => p.IdCommission == idCommission)
                .Include(p => p.Commande).ThenInclude(c => c!.Client)
                .Include(p => p.Couturier)
                .Include(p => p.MaterielSupplements)
                .OrderBy(p => p.IdCommande).ThenBy(p => p.IdPieceCommande)
                .ToList()
                .Select(ConstruireLigneDetailPiece)
                .ToList();

            // Compatibilité avec les anciennes commissions verrouillées au niveau commande.
            if (pieces.Count == 0)
            {
                var commandes = context.Commandes.AsNoTracking()
                    .Where(c => c.IdCommission == idCommission)
                    .Include(c => c.Client)
                    .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                    .Include(c => c.Pieces).ThenInclude(p => p.MaterielSupplements)
                    .OrderBy(c => c.IdCommande)
                    .ToList();
                pieces = commandes.SelectMany(c => c.Pieces.Select(p => new LigneDetailPieceCommission
                {
                    Commande = $"CMD #{c.IdCommande}",
                    Client = c.Client?.NomComplet ?? "Client inconnu",
                    Telephone = c.Client?.Telephone ?? "",
                    Piece = p.TypeVetement,
                    Couturier = p.Couturier == null ? "—" : $"{p.Couturier.Prenom} {p.Couturier.Nom}",
                    Statut = p.StatutAffiche,
                    DateTerminee = p.DateTerminee?.ToString("dd/MM/yyyy") ?? "—",
                    Couture = $"{p.MontantCouture:N0} FCFA",
                    Materiaux = $"{p.MaterielSupplements.Sum(m => m.Quantite * m.PrixUnitaire):N0} FCFA",
                    Description = p.DescriptionPrecision
                })).ToList();
            }

            var resume = new List<(string Label, string Valeur)>
            {
                ("Statut", commission.EstAnnulee ? "ANNULÉE" : "VALIDÉE"),
                ("Couturier", commission.NomEmployeSnapshot),
                ("Période travaillée", commission.PeriodeAffichee),
                ("Date de validation", commission.DateCalcul.ToString("dd/MM/yyyy HH:mm")),
                ("Base de calcul", commission.BaseCalcul),
                ("Taux", $"{commission.Pourcentage:N2}%"),
                ("Montant de base", $"{commission.BaseMontant:N0} FCFA"),
                ("Commandes", commission.NbCommandes.ToString()),
                ("Commission", $"{commission.MontantCommission:N0} FCFA"),
                ("Prime qualité", $"{commission.PrimeQualite:N0} FCFA"),
                ("Total", $"{commission.TotalAvecPrime:N0} FCFA"),
                ("Enregistré par", commission.NomOperateur)
            };
            if (commission.EstAnnulee)
            {
                resume.Add(("Annulée le", commission.DateAnnulation?.ToString("dd/MM/yyyy HH:mm") ?? "—"));
                resume.Add(("Annulée par", commission.NomAnnulateur ?? "—"));
                resume.Add(("Motif d’annulation", commission.MotifAnnulation ?? "—"));
            }

            return (commission, resume, pieces);
        }

        private static LigneDetailPieceCommission ConstruireLigneDetailPiece(PieceCommande p)
        {
            var commande = p.Commande;
            return new LigneDetailPieceCommission
            {
                Commande = $"CMD #{p.IdCommande}",
                Client = commande?.Client?.NomComplet ?? "Client inconnu",
                Telephone = commande?.Client?.Telephone ?? "",
                Piece = p.TypeVetement,
                Couturier = p.Couturier == null ? "—" : $"{p.Couturier.Prenom} {p.Couturier.Nom}",
                Statut = p.StatutAffiche,
                DateTerminee = p.DateTerminee?.ToString("dd/MM/yyyy") ?? "—",
                Couture = $"{p.MontantCouture:N0} FCFA",
                Materiaux = $"{p.MaterielSupplements.Sum(m => m.Quantite * m.PrixUnitaire):N0} FCFA",
                Description = p.DescriptionPrecision
            };
        }

        private void OuvrirFenetreDetail(string titre, string sousTitre,
            IEnumerable<(string Label, string Valeur)> resume,
            IEnumerable<LigneDetailPieceCommission> pieces)
        {
            var contenu = new Grid { Background = System.Windows.Media.Brushes.White };
            contenu.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contenu.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contenu.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            contenu.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var entete = new StackPanel { Margin = new Thickness(22, 18, 22, 14) };
            entete.Children.Add(new TextBlock
            {
                Text = titre, FontSize = 21, FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x17, 0x2A))
            });
            entete.Children.Add(new TextBlock
            {
                Text = sousTitre, FontSize = 13, Margin = new Thickness(0, 5, 0, 0),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B))
            });
            Grid.SetRow(entete, 0);
            contenu.Children.Add(entete);

            var grilleResume = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 4, Margin = new Thickness(22, 0, 22, 14)
            };
            foreach (var (label, valeur) in resume)
            {
                var bloc = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
                bloc.Children.Add(new TextBlock
                {
                    Text = label.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8))
                });
                bloc.Children.Add(new TextBlock
                {
                    Text = valeur, FontSize = 12, FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x29, 0x3B))
                });
                grilleResume.Children.Add(new Border
                {
                    Padding = new Thickness(9, 7, 5, 7), Margin = new Thickness(0, 0, 5, 5),
                    CornerRadius = new CornerRadius(5),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF1, 0xF5, 0xF9)),
                    Child = bloc
                });
            }
            Grid.SetRow(grilleResume, 1);
            contenu.Children.Add(grilleResume);

            var grillePieces = new DataGrid
            {
                ItemsSource = pieces.ToList(), AutoGenerateColumns = false, IsReadOnly = true,
                CanUserAddRows = false, EnableRowVirtualization = true,
                Margin = new Thickness(22, 0, 22, 12), MinHeight = 180
            };
            VirtualizingPanel.SetIsVirtualizing(grillePieces, true);
            VirtualizingPanel.SetVirtualizationMode(grillePieces, VirtualizationMode.Recycling);
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Commande", Binding = new System.Windows.Data.Binding("Commande"), Width = 85 });
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Client", Binding = new System.Windows.Data.Binding("Client"), Width = 150 });
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Téléphone", Binding = new System.Windows.Data.Binding("Telephone"), Width = 100 });
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Pièce", Binding = new System.Windows.Data.Binding("Piece"), Width = 105 });
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Couturier", Binding = new System.Windows.Data.Binding("Couturier"), Width = 130 });
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Statut", Binding = new System.Windows.Data.Binding("Statut"), Width = 85 });
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Terminée le", Binding = new System.Windows.Data.Binding("DateTerminee"), Width = 95 });
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Couture", Binding = new System.Windows.Data.Binding("Couture"), Width = 100 });
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Matériaux", Binding = new System.Windows.Data.Binding("Materiaux"), Width = 100 });
            grillePieces.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new System.Windows.Data.Binding("Description"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            Grid.SetRow(grillePieces, 2);
            contenu.Children.Add(grillePieces);

            var fermer = new Button
            {
                Content = "Fermer", Width = 110, Height = 36, Margin = new Thickness(0, 0, 22, 16),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0),
                IsDefault = true
            };
            fermer.Click += (_, _) => Window.GetWindow(fermer)?.Close();
            Grid.SetRow(fermer, 3);
            contenu.Children.Add(fermer);

            var fenetre = new Window
            {
                Title = titre, Width = 1220, Height = 720, MinWidth = 900, MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this), ResizeMode = ResizeMode.CanResize,
                Background = System.Windows.Media.Brushes.White, Content = contenu
            };
            fenetre.ShowDialog();
        }

        private void GridHistorique_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TxtMessageHistorique.Text = string.Empty;
        }

        private async void BtnAnnulerCommission_Click(object sender, RoutedEventArgs e)
        {
            if (GridHistorique.SelectedItem == null)
            {
                TxtMessageHistorique.Text = "Sélectionnez une commission dans l'historique.";
                TxtMessageHistorique.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            dynamic item = GridHistorique.SelectedItem;
            int idCommission = (int)item.IdCommission;
            DateTime dateDebutCommission = (DateTime)item.DateDebutPeriode;
            DateTime dateFinCommission = (DateTime)item.DateFinPeriode;
            string? motif = DemanderMotifAnnulation(TxtMotifAnnulation.Text);
            if (string.IsNullOrWhiteSpace(motif)) return;

            try
            {
                _commissionService.Annuler(
                    idCommission,
                    motif,
                    _authService.UtilisateurConnecte!.IdEmploye,
                    _authService.UtilisateurConnecte!.Prenom + " " + _authService.UtilisateurConnecte!.Nom);

                TxtMessageHistorique.Text = "Commission annulée.";
                TxtMessageHistorique.Foreground = System.Windows.Media.Brushes.Green;
                TxtMotifAnnulation.Clear();

                // Revenir à la période annulée et afficher tous les couturiers :
                // les pièces libérées de plusieurs annulations doivent apparaître
                // ensemble, avec la pagination normale de cinq couturiers.
                _suspendreCalculFiltres = true;
                try
                {
                    DateDebut.SelectedDate = dateDebutCommission;
                    DateFin.SelectedDate = dateFinCommission;
                    CmbCouturier.SelectedIndex = 0;
                    _idCouturierSelectionne = null;
                }
                finally
                {
                    _suspendreCalculFiltres = false;
                }

                await ChargerHistoriquePageAsync(_pageHistorique);
                await CalculerApercuAsync();
            }
            catch (Exception ex)
            {
                TxtMessageHistorique.Text = "Erreur : " + ex.Message;
                TxtMessageHistorique.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private string? DemanderMotifAnnulation(string motifInitial)
        {
            var fenetre = new Window
            {
                Title = "Annuler une commission",
                Width = 480,
                Height = 285,
                MinWidth = 440,
                MinHeight = 260,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.White,
                ShowInTaskbar = false
            };

            var contenu = new StackPanel { Margin = new Thickness(22) };
            contenu.Children.Add(new TextBlock
            {
                Text = "Saisissez le motif de l'annulation. Les pièces de cette commission redeviendront éligibles à un prochain calcul.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var txtMotif = new TextBox
            {
                Text = motifInitial?.Trim() ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 72,
                Padding = new Thickness(10),
                FontSize = 13,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCB, 0xD5, 0xE1)),
                BorderThickness = new Thickness(1)
            };
            contenu.Children.Add(txtMotif);

            var txtErreur = new TextBlock
            {
                Text = "Le motif est obligatoire.",
                Foreground = System.Windows.Media.Brushes.Firebrick,
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = Visibility.Collapsed
            };
            contenu.Children.Add(txtErreur);

            var boutons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var btnRetour = new Button
            {
                Content = "Retour",
                MinWidth = 95,
                Height = 36,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 0, 12, 0),
                IsCancel = true
            };
            btnRetour.Click += (_, _) => fenetre.DialogResult = false;

            var btnValider = new Button
            {
                Content = "Valider l'annulation",
                MinWidth = 155,
                Height = 36,
                Padding = new Thickness(12, 0, 12, 0),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold
            };
            btnValider.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(txtMotif.Text))
                {
                    txtErreur.Visibility = Visibility.Visible;
                    txtMotif.Focus();
                    return;
                }
                fenetre.DialogResult = true;
            };

            boutons.Children.Add(btnRetour);
            boutons.Children.Add(btnValider);
            contenu.Children.Add(boutons);
            fenetre.Content = contenu;
            fenetre.Loaded += (_, _) => txtMotif.Focus();

            return fenetre.ShowDialog() == true ? txtMotif.Text.Trim() : null;
        }

        // ✅ Validation du pourcentage (nombres décimaux)
        private void TxtPourcentage_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtPourcentage_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            ValidationHelper.TextBox_Pasting(sender, e);
        }
    }
}
