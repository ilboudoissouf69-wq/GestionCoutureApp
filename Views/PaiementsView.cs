using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using GestionCoutureApp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class PaiementsView : Page
    {
        // ── Services ────────────────────────────────────────────────
        private readonly IPaiementService   _paiementService;
        private readonly ICommandeService   _commandeService;
        private readonly IAuthService       _authService;
        private readonly IReceiptService    _receiptService;
        private readonly ILanguageService   _languageService;
        private readonly IEventAggregator   _eventAggregator;
        private readonly ApplicationDbContext _context;

        // ── État ────────────────────────────────────────────────────
        private Employe?   _operateurConnecte;
        private Commande?  _commandeSelectionnee;   // commande active dans la modale encaissement

        // ── Pagination ──────────────────────────────────────────────
        private const int PAGE_SIZE  = 15;
        private int       _currentPage = 1;

        // ── Anti double-clic ────────────────────────────────────────
        private bool _enCoursEnregistrement = false;

        // ── Paiement cible de l'annulation (modale Boss) ────────────
        private Paiement? _paiementAnnulationEnCours;

        // ── Délai de frappe pour la recherche (300 ms) ──────────────
        private System.Windows.Threading.DispatcherTimer? _rechercheTimer;

        // ── Propriété exposée pour le binding Visibility bouton Annuler ──
        /// <summary>True si l'opérateur connecté est Boss — bindé dans le DataGrid template.</summary>
        public bool EstBoss => _operateurConnecte?.Role == "Boss";

        // ================================================================
        // CONSTRUCTEURS
        // ================================================================

        /// <summary>
        /// Surcharge : ouvre PaiementsView et présélectionne la commande indiquée.
        /// La sélection est effectuée dans Loaded pour laisser CmbCommandesNonSoldees
        /// s'initialiser. Si la commande est déjà soldée, un message informatif
        /// est affiché à la place de la modale vide.
        /// </summary>
        public PaiementsView(int idCommandePreselectionnee) : this()
        {
            Loaded += (s, e) => OuvrirModalEncaissementAvecCommande(idCommandePreselectionnee);
        }

        public PaiementsView()
        {
            InitializeComponent();

            _paiementService = App.Services.GetRequiredService<IPaiementService>();
            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _authService     = App.Services.GetRequiredService<IAuthService>();
            _receiptService  = App.Services.GetRequiredService<IReceiptService>();
            _languageService = App.Services.GetRequiredService<ILanguageService>();
            _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();

            _commandeService.CommandeChanged += OnCommandeChanged;
            _eventAggregator.Subscribe(SettingsChangedType.Language,     OnLanguageChanged);
            _eventAggregator.Subscribe(SettingsChangedType.AccentColor,  OnThemeChanged);

            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();

            Unloaded += (s, e) =>
            {
                _context.Dispose();
                _commandeService.CommandeChanged -= OnCommandeChanged;
                _eventAggregator.Unsubscribe(SettingsChangedType.Language,    OnLanguageChanged);
                _eventAggregator.Unsubscribe(SettingsChangedType.AccentColor, OnThemeChanged);
                _rechercheTimer?.Stop();
            };

            _operateurConnecte = _authService.UtilisateurConnecte;

            if (_operateurConnecte != null)
                TxtOperateurConnecte.Text =
                    "Opérateur : " + _operateurConnecte.Prenom + " " + _operateurConnecte.Nom;

            // Timer de délai recherche (300 ms, single-shot)
            _rechercheTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _rechercheTimer.Tick += async (s, e) =>
            {
                _rechercheTimer.Stop();
                _currentPage = 1;
                await ChargerPaiements();
            };

            _ = ChargerPaiements();
        }

        // ================================================================
        // LANGUE / THÈME
        // ================================================================

        private void OnLanguageChanged(SettingsChangedEvent evt) =>
            Dispatcher.Invoke(UpdateTranslations);

        private void OnThemeChanged(SettingsChangedEvent evt)
        {
            // Couleurs via DynamicResource — mise à jour automatique
        }

        private void UpdateTranslations()
        {
            // PaiementsView n'a pas encore de textes multilingues côté vue
        }

        // ================================================================
        // CHARGEMENT TABLEAU
        // ================================================================

        private async Task ChargerPaiements()
        {
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                GridPaiements.IsEnabled     = false;

                string?             recherche = TxtRecherche.Text.Trim();
                if (string.IsNullOrEmpty(recherche)) recherche = null;

                StatutFiltrePaiement filtre = RbAnnules.IsChecked == true
                    ? StatutFiltrePaiement.Annules
                    : RbTous.IsChecked == true
                        ? StatutFiltrePaiement.Tous
                        : StatutFiltrePaiement.Valides;

                var result = await _paiementService.ObtenirPageLightAsync(
                    _currentPage, PAGE_SIZE, recherche, filtre);

                GridPaiements.ItemsSource = result.Items;

                BtnPagePrecedente.IsEnabled = result.HasPrevious;
                BtnPageSuivante.IsEnabled   = result.HasNext;

                int start = result.TotalCount == 0 ? 0 : (result.Page - 1) * result.PageSize + 1;
                int end   = Math.Min(result.Page * result.PageSize, result.TotalCount);
                TxtPaginationInfo.Text = result.TotalCount == 0
                    ? "Aucun paiement"
                    : $"{start}–{end} / {result.TotalCount} paiement{(result.TotalCount > 1 ? "s" : "")}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du chargement des paiements : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                GridPaiements.IsEnabled     = true;
            }
        }

        // ── Pagination ──────────────────────────────────────────────

        private async void BtnPagePrecedente_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1) { _currentPage--; await ChargerPaiements(); }
        }

        private async void BtnPageSuivante_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await ChargerPaiements();
        }

        // ── Recherche ───────────────────────────────────────────────

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Relance le timer à chaque frappe (debounce 300 ms)
            _rechercheTimer?.Stop();
            _rechercheTimer?.Start();
        }

        // ── Filtres statut ──────────────────────────────────────────

        private async void FiltreStatut_Changed(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            await ChargerPaiements();
        }

        // ================================================================
        // COMMANDES ENCAISSABLES (pour la modale)
        // ================================================================

        private void ChargerCommandes()
        {
            try
            {
                // Réutilise exactement la requête existante : ObtenirTous inclut
                // Client, Paiements, Pieces, MaterielSupplements via CommandeService.
                var commandes = _commandeService.ObtenirTous()
                    .Where(c => !c.EstSupprimee && c.ResteAPayer > 0.01m)
                    .ToList();

                CmbCommandesNonSoldees.ItemsSource = commandes.Select(c => new
                {
                    c.IdCommande,
                    DisplayText = (c.Client?.Prenom ?? "") + " " + (c.Client?.Nom ?? "")
                                  + " — " + (c.TypeVetementAffiche ?? "(aucun vêtement)")
                                  + "  (" + c.MontantTotalAvecMateriaux.ToString("N0") + " FCFA"
                                  + " · reste : " + c.ResteAPayer.ToString("N0") + " FCFA)"
                }).ToList();
                CmbCommandesNonSoldees.SelectedValuePath = "IdCommande";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du chargement des commandes : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================================================================
        // MODAL ENCAISSEMENT — ouverture / fermeture
        // ================================================================

        private void BtnOuvrirModalEncaissement_Click(object sender, RoutedEventArgs e)
        {
            ChargerCommandes();

            if (CmbCommandesNonSoldees.Items.Count == 0)
            {
                MessageBox.Show(
                    "Aucune commande n'est encaissable pour le moment.\n" +
                    "Toutes les commandes sont soit soldées, soit sans reste à payer.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ReinitialiserModalEncaissement();
            OverlayEncaissement.Visibility = Visibility.Visible;
        }

        private void OuvrirModalEncaissementAvecCommande(int idCommande)
        {
            ChargerCommandes();

            // Vérifier si la commande est encore encaissable
            var commande = _commandeService.ObtenirParId(idCommande);
            if (commande == null)
            {
                MessageBox.Show("Commande introuvable.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            decimal reste = commande.ResteAPayer;
            if (reste <= 0.01m)
            {
                MessageBox.Show(
                    $"La commande CMD #{idCommande} est déjà entièrement soldée.\n" +
                    $"Montant total : {commande.MontantTotalAvecMateriaux:N0} FCFA — " +
                    $"encaissé : {commande.MontantEncaisse:N0} FCFA.",
                    "Commande soldée", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (CmbCommandesNonSoldees.Items.Count == 0)
            {
                MessageBox.Show(
                    "Aucune commande encaissable disponible.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ReinitialiserModalEncaissement();
            OverlayEncaissement.Visibility = Visibility.Visible;

            // Présélectionner la commande dans le ComboBox
            CmbCommandesNonSoldees.SelectedValue = idCommande;
            CmbCommande_SelectionChanged(null!, null!);
        }

        private void BtnFermerModalEncaissement_Click(object sender, RoutedEventArgs e)
        {
            OverlayEncaissement.Visibility = Visibility.Collapsed;
            _commandeSelectionnee = null;
        }

        private void ReinitialiserModalEncaissement()
        {
            CmbCommandesNonSoldees.SelectedIndex = -1;
            TxtClientNom.Text          = "Client : —";
            TxtResteExact.Text         = "— FCFA";
            TxtResteApres.Text         = "— FCFA";
            TxtMontantPaiement.Text    = "";
            CmbModePaiement.SelectedIndex = 0;
            _commandeSelectionnee      = null;
        }

        // ================================================================
        // MODAL ENCAISSEMENT — sélection commande
        // ================================================================

        private void CmbCommande_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (CmbCommandesNonSoldees.SelectedValue == null)
                {
                    TxtClientNom.Text  = "Client : —";
                    TxtResteExact.Text = "— FCFA";
                    TxtResteApres.Text = "— FCFA";
                    _commandeSelectionnee = null;
                    return;
                }

                int idCmd = (int)CmbCommandesNonSoldees.SelectedValue;
                _commandeSelectionnee = _commandeService.ObtenirParId(idCmd);
                if (_commandeSelectionnee == null) return;

                string prenom = _commandeSelectionnee.Client?.Prenom ?? "";
                string nom    = _commandeSelectionnee.Client?.Nom    ?? "";
                TxtClientNom.Text = "Client : " + prenom + " " + nom;

                decimal totalValide = _paiementService.TotalValideParCommande(idCmd);
                decimal montantTotal = _commandeSelectionnee.MontantTotalAvecMateriaux;
                decimal reste        = Math.Max(0m, montantTotal - totalValide);

                TxtResteExact.Text = reste.ToString("N0") + " FCFA";

                // Pré-remplir le montant avec le reste exact
                TxtMontantPaiement.Text = reste.ToString("N0");
                ActualiserResteApres();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la sélection de la commande : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtMontantPaiement_TextChanged(object sender, TextChangedEventArgs e)
        {
            ActualiserResteApres();
        }

        private void ActualiserResteApres()
        {
            if (_commandeSelectionnee == null) { TxtResteApres.Text = "— FCFA"; return; }

            string raw = TxtMontantPaiement.Text.Replace(" ", "").Replace("\u00A0", "");
            if (!decimal.TryParse(raw, out decimal montant) || montant <= 0)
            {
                TxtResteApres.Text = "— FCFA";
                return;
            }

            decimal totalValide  = _paiementService.TotalValideParCommande(_commandeSelectionnee.IdCommande);
            decimal montantTotal = _commandeSelectionnee.MontantTotalAvecMateriaux;
            decimal reste        = Math.Max(0m, montantTotal - totalValide);
            decimal resteApres   = Math.Max(0m, reste - montant);

            TxtResteApres.Text = resteApres.ToString("N0") + " FCFA";
        }

        // ================================================================
        // MODAL ENCAISSEMENT — validation (BtnEnregistrer_Click migré)
        // ================================================================

        private void BtnValiderPaiement_Click(object sender, RoutedEventArgs e)
        {
            if (_enCoursEnregistrement)
            {
                MessageBox.Show("Enregistrement en cours, veuillez patienter…",
                    "Opération en cours", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _enCoursEnregistrement   = true;
            BtnValiderPaiement.IsEnabled = false;

            try
            {
                // ── Validations ──────────────────────────────────────
                if (CmbCommandesNonSoldees.SelectedValue == null)
                { Alerte("Sélectionnez une commande."); return; }

                string raw = TxtMontantPaiement.Text.Replace(" ", "").Replace("\u00A0", "");
                if (!decimal.TryParse(raw, out decimal montant) || montant <= 0)
                { Alerte("Le montant doit être un nombre positif."); return; }

                if (_commandeSelectionnee == null)
                { Alerte("Commande introuvable."); return; }

                if (_operateurConnecte == null)
                { Alerte("Aucun opérateur connecté."); return; }

                if (CmbModePaiement.SelectedItem == null)
                { Alerte("Sélectionnez un mode de paiement."); return; }

                // ── Vérification solde en temps réel ─────────────────
                decimal totalValide  = _paiementService.TotalValideParCommande(_commandeSelectionnee.IdCommande);
                decimal reste        = _commandeSelectionnee.MontantTotalAvecMateriaux - totalValide;

                if (reste <= 0.01m)
                { Alerte("Cette commande est déjà entièrement payée."); return; }

                if (montant > reste + 0.01m)
                {
                    Alerte($"Le montant saisi ({montant:N0} FCFA) dépasse\nle reste à payer ({reste:N0} FCFA).");
                    return;
                }

                // ── Mode de paiement ──────────────────────────────────
                string modeChoisi = CmbModePaiement.SelectedItem is ComboBoxItem item
                    ? item.Content?.ToString() ?? "Espèces"
                    : CmbModePaiement.SelectedItem?.ToString() ?? "Espèces";

                // ── Confirmation ──────────────────────────────────────
                decimal totalFacture  = _commandeSelectionnee.MontantTotalAvecMateriaux;
                decimal totalMateriaux = _commandeSelectionnee.TotalMateriaux;
                string ligneTotal = totalMateriaux > 0
                    ? $"Total facture : {totalFacture:N0} FCFA (dont {totalMateriaux:N0} matériaux)\n"
                    : $"Total facture : {totalFacture:N0} FCFA\n";

                string nomClient   = (_commandeSelectionnee.Client?.Prenom ?? "") + " "
                                   + (_commandeSelectionnee.Client?.Nom    ?? "");
                string nomOperateur = _operateurConnecte.Prenom + " " + _operateurConnecte.Nom;

                var confirmation = MessageBox.Show(
                    $"Confirmer l'enregistrement du paiement ?\n\n" +
                    $"Client   : {nomClient}\n" +
                    ligneTotal +
                    $"Montant  : {montant:N0} FCFA\n" +
                    $"Mode     : {modeChoisi}\n" +
                    $"Reste après : {(reste - montant):N0} FCFA\n\n" +
                    $"Opérateur : {nomOperateur}",
                    "Confirmation du paiement",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes) return;

                // ── Enregistrement via service ────────────────────────
                var paiement = new Paiement
                {
                    IdCommande   = _commandeSelectionnee.IdCommande,
                    MontantPaye  = montant,
                    ModePaiement = modeChoisi
                };

                _paiementService.Ajouter(paiement, _operateurConnecte.IdEmploye, nomOperateur);

                // ── Fermer la modale, rafraîchir ──────────────────────
                OverlayEncaissement.Visibility = Visibility.Collapsed;
                _commandeSelectionnee = null;
                _ = ChargerPaiements();
                ChargerCommandes();   // met à jour la liste des encaissables

                // ── Proposer l'impression du reçu ─────────────────────
                var imprimer = MessageBox.Show(
                    $"Paiement enregistré avec succès !\n\n" +
                    $"N° Reçu  : {paiement.RecuNumero}\n" +
                    $"Montant  : {paiement.MontantPaye:N0} FCFA\n" +
                    $"Mode     : {paiement.ModePaiement}\n" +
                    $"Opérateur : {paiement.NomOperateur}\n\n" +
                    $"Voulez-vous imprimer le reçu ?",
                    "Paiement enregistré",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (imprimer == MessageBoxResult.Yes)
                    OuvrirFenetreRecu(paiement);
            }
            catch (InvalidOperationException ex)
            {
                Alerte("Erreur : " + ex.Message);
            }
            catch (Exception ex)
            {
                Alerte("Erreur inattendue : " + ex.Message);
            }
            finally
            {
                _enCoursEnregistrement       = false;
                BtnValiderPaiement.IsEnabled = true;
            }
        }

        // ================================================================
        // ACTIONS DE LIGNE — Reçu
        // ================================================================

        private void BtnLigneRecu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Paiement paiement)
                OuvrirFenetreRecu(paiement);
        }

        private void OuvrirFenetreRecu(Paiement paiement)
        {
            if (paiement.EstAnnule)
            {
                Alerte("Ce paiement est annulé. Impossible d'imprimer un reçu annulé.");
                return;
            }

            var commande = _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.MaterielSupplements)
                .FirstOrDefault(c => c.IdCommande == paiement.IdCommande);

            if (commande == null) { Alerte("Commande introuvable."); return; }

            // Les mesures sont portées par chaque pièce — on les rassemble
            // pour rétrocompatibilité avec FenetreRecu (liste plate de Mesure).
            var mesures = commande.Pieces.SelectMany(p => p.Mesures).ToList();

            var fenetre = new FenetreRecu(commande, paiement, mesures,
                                          paiement.NomOperateur, _receiptService);
            fenetre.Owner = Window.GetWindow(this);
            fenetre.Show();
        }

        // ================================================================
        // ACTIONS DE LIGNE — Annuler (Boss uniquement)
        // ================================================================

        private void BtnLigneAnnuler_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Paiement paiement) return;

            if (paiement.EstAnnule)
            { Alerte("Ce paiement est déjà annulé."); return; }

            if (_operateurConnecte?.Role != "Boss")
            { Alerte("Seul le Boss peut annuler un paiement."); return; }

            // Ouvrir la modale d'annulation
            _paiementAnnulationEnCours = paiement;
            TxtInfoAnnulation.Text =
                $"Paiement : {paiement.RecuNumero}\n" +
                $"Montant  : {paiement.MontantPaye:N0} FCFA\n" +
                $"Opérateur : {paiement.NomOperateur}\n" +
                $"Date     : {paiement.DatePaiement:dd/MM/yyyy HH:mm}";
            TxtMotifAnnulation.Text   = "";
            PwdBossAnnulation.Clear();
            TxtErreurMotif.Text       = "";
            TxtErreurMdp.Text         = "";
            OverlayAnnulation.Visibility = Visibility.Visible;
        }

        // ================================================================
        // MODAL ANNULATION — fermeture
        // ================================================================

        private void BtnFermerModalAnnulation_Click(object sender, RoutedEventArgs e)
        {
            OverlayAnnulation.Visibility  = Visibility.Collapsed;
            _paiementAnnulationEnCours    = null;
        }

        // ================================================================
        // MODAL ANNULATION — confirmation (DemanderMotifAnnulation migré)
        // ================================================================

        private void BtnConfirmerAnnulation_Click(object sender, RoutedEventArgs e)
        {
            // ── Validation motif ──────────────────────────────────────
            TxtErreurMotif.Text = "";
            TxtErreurMdp.Text   = "";

            string motif = TxtMotifAnnulation.Text.Trim();
            if (string.IsNullOrWhiteSpace(motif))
            {
                TxtErreurMotif.Text = "Le motif est obligatoire.";
                TxtMotifAnnulation.Focus();
                return;
            }
            if (motif.Length < 10)
            {
                TxtErreurMotif.Text = "Le motif doit contenir au moins 10 caractères.";
                TxtMotifAnnulation.Focus();
                return;
            }

            // ── Validation mot de passe Boss ──────────────────────────
            string mdpSaisi = PwdBossAnnulation.Password.Trim();
            if (string.IsNullOrEmpty(mdpSaisi))
            {
                TxtErreurMdp.Text = "Le mot de passe Boss est obligatoire.";
                PwdBossAnnulation.Focus();
                return;
            }

            // Vérification avec hashage sécurisé (même logique que DemanderMotifAnnulation)
            var boss = _context.Employes
                .Where(emp => emp.Role == "Boss" && emp.Statut == "Actif")
                .AsEnumerable()
                .FirstOrDefault(emp =>
                    PasswordHasher.EstAncienFormatSha256(emp.MotDePasse)
                        ? emp.MotDePasse == PasswordHasher.HasherAncienSha256(mdpSaisi)
                        : PasswordHasher.Verifier(mdpSaisi, emp.MotDePasse));

            if (boss == null)
            {
                TxtErreurMdp.Text = "Mot de passe Boss incorrect.";
                PwdBossAnnulation.Clear();
                PwdBossAnnulation.Focus();
                return;
            }

            if (_paiementAnnulationEnCours == null) return;

            // ── Confirmation finale irréversible ──────────────────────
            var confirmation = MessageBox.Show(
                $"ATTENTION : Cette action est irréversible !\n\n" +
                $"Paiement  : {_paiementAnnulationEnCours.RecuNumero}\n" +
                $"Montant   : {_paiementAnnulationEnCours.MontantPaye:N0} FCFA\n" +
                $"Opérateur : {_paiementAnnulationEnCours.NomOperateur}\n" +
                $"Motif     : {motif}\n\n" +
                $"Confirmer l'annulation ?",
                "Confirmation annulation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes) return;

            try
            {
                _paiementService.Annuler(
                    _paiementAnnulationEnCours.IdPaiement,
                    motif,
                    _operateurConnecte!.IdEmploye,
                    _operateurConnecte.Prenom + " " + _operateurConnecte.Nom);

                OverlayAnnulation.Visibility  = Visibility.Collapsed;
                _paiementAnnulationEnCours    = null;
                _ = ChargerPaiements();

                MessageBox.Show(
                    $"Paiement annulé.\n" +
                    $"Le montant de {_paiementAnnulationEnCours?.MontantPaye.ToString("N0") ?? ""} FCFA est désormais\n" +
                    $"réintégré dans le solde de la commande.",
                    "Annulation effectuée",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                Alerte("Erreur : " + ex.Message);
            }
            catch (Exception ex)
            {
                Alerte("Erreur inattendue : " + ex.Message);
            }
        }

        // ================================================================
        // TEMPS RÉEL — OnCommandeChanged
        // ================================================================

        private void OnCommandeChanged(object? sender, CommandeChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Toujours rafraîchir le tableau
                _ = ChargerPaiements();

                // Si la modale d'encaissement est ouverte et concerne cette commande
                if (OverlayEncaissement.Visibility == Visibility.Visible
                    && _commandeSelectionnee?.IdCommande == e.IdCommande)
                {
                    var commandeMAJ = _commandeService.ObtenirParId(e.IdCommande);

                    if (commandeMAJ == null)
                    {
                        // Commande supprimée — fermer la modale avec message
                        OverlayEncaissement.Visibility = Visibility.Collapsed;
                        _commandeSelectionnee = null;
                        MessageBox.Show(
                            "La commande sélectionnée a été supprimée.\nLa fenêtre d'encaissement a été fermée.",
                            "Commande supprimée", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Recalculer reste / montant
                    _commandeSelectionnee = commandeMAJ;
                    decimal totalValide  = _paiementService.TotalValideParCommande(e.IdCommande);
                    decimal montantTotal = commandeMAJ.MontantTotalAvecMateriaux;
                    decimal reste        = Math.Max(0m, montantTotal - totalValide);

                    TxtResteExact.Text = reste.ToString("N0") + " FCFA";

                    if (reste <= 0.01m)
                    {
                        // Commande soldée pendant que la modale était ouverte
                        OverlayEncaissement.Visibility = Visibility.Collapsed;
                        _commandeSelectionnee = null;
                        MessageBox.Show(
                            "Cette commande vient d'être soldée.\nLa fenêtre d'encaissement a été fermée.",
                            "Commande soldée", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    ActualiserResteApres();
                }

                // Rafraîchir aussi la liste des commandes encaissables si la modale est ouverte
                if (OverlayEncaissement.Visibility == Visibility.Visible)
                    ChargerCommandes();
            });
        }

        // ================================================================
        // HELPERS
        // ================================================================

        private void Alerte(string message) =>
            MessageBox.Show(message, "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);

        /// <summary>Validation saisie numérique décimale — même logique que l'existant.</summary>
        private void TxtMontant_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
            }
            catch
            {
                e.Handled = true;
            }
        }
    }
}
