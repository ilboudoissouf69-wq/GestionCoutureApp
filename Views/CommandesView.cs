using System.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class CommandesView : Page
    {
        // ================================================================
        // SERVICES & ÉTAT
        // ================================================================
        private readonly ICommandeService _commandeService;
        private readonly IClientService _clientService;
        private readonly ApplicationDbContext _context;
        private readonly IMaterielService _materielService;
        private readonly IWhatsAppService _whatsApp;
        private readonly ILanguageService _languageService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPaiementService _paiementService;
        private readonly IReceiptService _receiptService;

        private int _commandeSelectionneeId;
        private int? _pieceSelectionneeId;
        private string? _motifExceptionAjoutPiece;
        private decimal _prixBaseActuel;
        private List<TypeVetement> _typesVetement = new();
        private bool _chargementEnCours = false;
        private string _cheminPhotoTemporaire = string.Empty;
        private string _roleUtilisateur;

        // Pièces chargées pour la commande sélectionnée (legacy panel droit)
        private List<PieceCommande> _piecesCommande = new();

        // Buffer temporaire matériaux avant sauvegarde pièce
        private readonly List<MaterielSupplement> _materiauxTemporaires = new();

        // Pagination
        private const int PAGE_SIZE = 15;
        private int _currentPage = 1;
        private string _currentSearch = "";
        private string? _filtreStatut = null;
        private int? _filtreCouturierId = null;

        // Date RDV par défaut
        private DateTime _dateRdvDefaut = DateTime.Today.AddDays(1);

        // ── MODE MODAL ──────────────────────────────────────────────────
        // true = création (champs vides), false = édition (pré-rempli)
        private bool _modalModeCreation = true;
        // Commande en cours d'édition dans la modale (null si création)
        private Commande? _commandeEnEdition = null;
        // Liste des cartes de pièces dynamiques dans PanneauCartesPieces
        // Chaque carte est un objet anonyme encapsulé dans une classe interne
        private readonly List<Cartepiece> _cartesPieces = new();

        // Liste partagée des couturiers disponibles (utilisée dans AjouterCarte)
        private List<Employe> _couturiers = new();

        // ================================================================
        // HELPER OPÉRATEUR
        // ================================================================
        private (int id, string nom) OperateurConnecte()
        {
            var op = App.Services.GetRequiredService<IAuthService>().UtilisateurConnecte;
            return op != null
                ? (op.IdEmploye, $"{op.Prenom} {op.Nom}".Trim())
                : (0, string.Empty);
        }

        private bool AvecControleLivraison(Action<string?> action)
        {
            try { action(null); return true; }
            catch (LivraisonNonSoldeeException ex) when (ex.PeutForcer)
            {
                var rep = MessageBox.Show(
                    ex.Message + "\n\nAutoriser quand même la livraison ?",
                    "Livraison non soldée", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (rep != MessageBoxResult.Yes) return false;
                string? motif = DemanderMotif("Motif de la livraison non soldée");
                if (string.IsNullOrWhiteSpace(motif)) return false;
                action(motif);
                return true;
            }
        }


        // ================================================================
        // CONSTRUCTEUR
        // ================================================================
        public CommandesView()
        {
            InitializeComponent();

            try
            {
                _commandeService = App.Services.GetRequiredService<ICommandeService>();
                _clientService   = App.Services.GetRequiredService<IClientService>();
                _materielService = App.Services.GetRequiredService<IMaterielService>();
                _whatsApp        = App.Services.GetRequiredService<IWhatsAppService>();
                _languageService = App.Services.GetRequiredService<ILanguageService>();
                _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();
                _paiementService = App.Services.GetRequiredService<IPaiementService>();
                _receiptService  = App.Services.GetRequiredService<IReceiptService>();

                var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                _context = contextFactory.CreateDbContext();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'initialisation des services : " + ex.Message,
                    "Erreur d'initialisation", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Unloaded += (s, e) =>
            {
                _context?.Dispose();
                _eventAggregator.Unsubscribe(SettingsChangedType.Language, OnLanguageChanged);
                _eventAggregator.Unsubscribe(SettingsChangedType.AccentColor, OnThemeChanged);
            };
            _eventAggregator.Subscribe(SettingsChangedType.Language, OnLanguageChanged);
            _eventAggregator.Subscribe(SettingsChangedType.AccentColor, OnThemeChanged);

            // Fix 2 — Reste à payer : recharger les commandes quand on revient sur cette page
            // (par ex. après un paiement enregistré depuis BtnActionPaiement_Click)
            Loaded += (s, e) =>
            {
                if (NavigationService != null)
                    NavigationService.Navigated += OnNavigatedBackToThis;

                // Fix sidebar : activer le bouton Commandes quelle que soit la page d'origine
                if (Window.GetWindow(this) is MainWindow mainWin)
                    mainWin.ActiverBoutonCommandes();
            };
            Unloaded += (s, e) =>
            {
                if (NavigationService != null)
                    NavigationService.Navigated -= OnNavigatedBackToThis;
            };

            var authService = App.Services.GetRequiredService<IAuthService>();
            _roleUtilisateur = authService.UtilisateurConnecte?.Role ?? "";

            // ── Visibilité BtnOuvrirModalCommande selon le rôle ──
            if (_roleUtilisateur == "Couturier")
                BtnOuvrirModalCommande.Visibility = Visibility.Collapsed;

            // ── Droits legacy (pour les méthodes du panneau droit gardées) ──
            if (_roleUtilisateur == "Secretaire")
            {
                BtnSupprimer.Visibility    = Visibility.Visible;
                BtnSupprimerPiece.Visibility = Visibility.Visible;
            }
            if (_roleUtilisateur == "Couturier")
            {
                BtnCreer.Visibility          = Visibility.Collapsed;
                BtnSupprimer.Visibility      = Visibility.Collapsed;
                BtnSupprimerPiece.Visibility = Visibility.Collapsed;
                BtnAjouterPiece.Visibility   = Visibility.Collapsed;
            }
            if (_roleUtilisateur == "Boss")
                BtnSupprimerPiece.Visibility = Visibility.Visible;

            // ── Remplir CmbClient (legacy) et CmbModalClient ──
            var clients = _clientService.ObtenirTous();
            CmbClient.ItemsSource      = clients;
            CmbModalClient.ItemsSource = clients;

            // ── Couturiers (filtre rôle Couturier + Boss) ──
            _couturiers = _context.Employes
                .Where(e => e.Statut == "Actif" && (e.Role == "Couturier" || e.Role == "Boss"))
                .OrderBy(e => e.Prenom)
                .ToList()
                .Select(e => new Employe
                {
                    IdEmploye   = e.IdEmploye,
                    Nom         = e.Nom,
                    Prenom      = e.Role == "Boss" ? e.Prenom + " (Boss)" : e.Prenom,
                    Role        = e.Role, Statut = e.Statut,
                    Identifiant = e.Identifiant, MotDePasse = e.MotDePasse
                }).ToList();
            CmbCouturier.ItemsSource = _couturiers;

            // ── CmbFiltreCouturier (avec entrée "Tous") ──
            if (CmbFiltreCouturier != null)
            {
                var filtreCouturiers = new List<Employe>
                {
                    new Employe { IdEmploye = 0, Prenom = "Tous", Nom = "les couturiers",
                                  Identifiant = "", MotDePasse = "" }
                };
                filtreCouturiers.AddRange(_couturiers);
                CmbFiltreCouturier.ItemsSource  = filtreCouturiers;
                CmbFiltreCouturier.SelectedIndex = 0;
            }

            // ── Types de vêtements ──
            try
            {
                _typesVetement = _context.TypesVetements
                    .Include(t => t.MesuresRequises)
                    .Include(t => t.Descriptions)
                    .ToList();
                if (_typesVetement == null || _typesVetement.Count == 0)
                {
                    _typesVetement = new List<TypeVetement>();
                    MessageBox.Show("Aucun type de vêtement enregistré. Créez-en d'abord.",
                        "Données manquantes", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _typesVetement = new List<TypeVetement>();
                MessageBox.Show("Erreur chargement types vêtements : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // ── CmbTypeVetement legacy ──
            CmbTypeVetement.ItemsSource = _typesVetement.Select(t => new
            {
                t.IdTypeVetement,
                DisplayText = t.Nom + " (" + t.PrixBase + " FCFA)"
            }).ToList();
            CmbTypeVetement.SelectedValuePath = "IdTypeVetement";

            for (int i = 0; i <= 20; i++)
                CmbAjustement.Items.Add(i * 500);
            CmbAjustement.SelectedIndex = 0;

            // ── Heure dépôt par défaut ──
            TxtHeureDebut.Text = DateTime.Now.ToString("HH:mm");

            var (dateRdvDef, heureRdvDef) = CalculerRdvParDefaut();
            _dateRdvDefaut      = dateRdvDef;
            TxtHeureFin.Text    = heureRdvDef.ToString(@"hh\:mm");
            TxtModalHeure.Text  = heureRdvDef.ToString(@"hh\:mm");
            DpModalDateFin.SelectedDate = dateRdvDef;

            // ── Acompte champ : attachement pasting (syntaxe RoutedEvent) ──
            TxtModalAcompte.AddHandler(DataObject.PastingEvent,
                new DataObjectPastingEventHandler(TxtModalAcompte_Pasting));

            _ = ChargerCommandes();
            ViderChamps();
        }

        private void OnLanguageChanged(SettingsChangedEvent evt) => Dispatcher.Invoke(() => { });
        private void OnThemeChanged(SettingsChangedEvent evt) { }

        /// <summary>
        /// Fix 2 + Fix 3 — Appelé chaque fois que le Frame navigue vers n'importe quelle page.
        /// Si c'est CETTE page qui devient active (retour depuis PaiementsView par ex.) :
        ///   - recharge le tableau pour que "Reste à payer" reflète les paiements récents
        ///   - réactive le bouton Commandes dans la sidebar
        /// </summary>
        private void OnNavigatedBackToThis(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            if (e.Content is CommandesView)
            {
                _ = ChargerCommandes();
                if (Window.GetWindow(this) is MainWindow mainWin)
                    mainWin.ActiverBoutonCommandes();
            }
        }


        // ================================================================
        // CHARGEMENT COMMANDES + FILTRES + PAGINATION
        // ================================================================
        private async Task ChargerCommandes()
        {
            if (_commandeService == null) return;
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                GridCommandes.IsEnabled     = false;

                PagedResult<Commande> result;
                if (_filtreStatut != null || _filtreCouturierId != null)
                {
                    result = await _commandeService.ObtenirPageCommandesStatutAsync(
                        _filtreStatut, _currentPage, PAGE_SIZE,
                        string.IsNullOrWhiteSpace(_currentSearch) ? null : _currentSearch);
                    if (_filtreCouturierId.HasValue)
                        result = new PagedResult<Commande>
                        {
                            Items      = result.Items.Where(c =>
                                c.Pieces.Any(p => p.IdCouturier == _filtreCouturierId.Value)).ToList(),
                            TotalCount = result.TotalCount,
                            Page       = result.Page,
                            PageSize   = result.PageSize
                        };
                }
                else if (!string.IsNullOrWhiteSpace(_currentSearch))
                    result = await _commandeService.RechercherPageLightAsync(_currentSearch, _currentPage, PAGE_SIZE);
                else
                    result = await _commandeService.ObtenirPageLightAsync(_currentPage, PAGE_SIZE);

                // Filtrer les pièces vides (sans type) pour l'affichage
                foreach (var c in result.Items)
                    c.Pieces = c.Pieces.Where(p => !string.IsNullOrEmpty(p.TypeVetement)).ToList();

                GridCommandes.ItemsSource = result.Items;

                if (_commandeSelectionneeId > 0)
                {
                    var item = result.Items.FirstOrDefault(c => c.IdCommande == _commandeSelectionneeId);
                    if (item != null)
                    {
                        _chargementEnCours = true;
                        try { GridCommandes.SelectedItem = item; }
                        finally { _chargementEnCours = false; }
                    }
                }

                BtnPagePrecedente.IsEnabled = result.HasPrevious;
                BtnPageSuivante.IsEnabled   = result.HasNext;
                int start = result.TotalCount == 0 ? 0 : (result.Page - 1) * result.PageSize + 1;
                int end   = Math.Min(result.Page * result.PageSize, result.TotalCount);
                TxtPaginationInfo.Text = $"{start}-{end} / {result.TotalCount} commandes";

                _ = MettreAJourCompteurRetard();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur chargement commandes : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                GridCommandes.IsEnabled     = true;
            }
        }

        private async void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            string saisie = TxtRecherche.Text.Trim();
            _currentSearch = saisie;
            _currentPage   = 1;

            // Si la saisie est un nombre, filtrer directement par IdCommande côté client
            // (plus rapide et plus intuitif que la recherche texte)
            if (int.TryParse(saisie, out int idSaisi) && idSaisi > 0)
            {
                var tout = await _commandeService.ObtenirPageLightAsync(1, 999);
                var match = tout.Items.Where(c => c.IdCommande == idSaisi).ToList();
                GridCommandes.ItemsSource   = match;
                BtnPagePrecedente.IsEnabled = false;
                BtnPageSuivante.IsEnabled   = false;
                TxtPaginationInfo.Text      = match.Count > 0
                    ? $"1 commande trouvée (n°{idSaisi})"
                    : $"Aucune commande n°{idSaisi}";
                return;
            }

            await ChargerCommandes();
        }

        private async void BtnPagePrecedente_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1) { _currentPage--; await ChargerCommandes(); }
        }

        private async void BtnPageSuivante_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await ChargerCommandes();
        }

        private async void CmbFiltreStatut_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_commandeService == null) return;
            if (CmbFiltreStatut.SelectedItem is ComboBoxItem item)
            {
                _filtreStatut = item.Tag?.ToString();
                if (string.IsNullOrEmpty(_filtreStatut)) _filtreStatut = null;
            }
            _currentPage = 1;
            await ChargerCommandes();
        }

        private async void CmbFiltreCouturier_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_commandeService == null) return;
            if (CmbFiltreCouturier.SelectedItem is Employe emp && emp.IdEmploye > 0)
                _filtreCouturierId = emp.IdEmploye;
            else
                _filtreCouturierId = null;
            _currentPage = 1;
            await ChargerCommandes();
        }

        private async void BtnCompteurRetard_Click(object sender, RoutedEventArgs e)
        {
            _filtreStatut = _filtreStatut == "Retard" ? null : "Retard";
            _currentPage  = 1;
            await ChargerCommandes();
        }

        private async Task MettreAJourCompteurRetard()
        {
            try
            {
                if (_commandeService == null) return;
                var result = await _commandeService.ObtenirPageCommandesStatutAsync("Retard", 1, 999, null);
                int nb = result.TotalCount;
                Dispatcher.Invoke(() =>
                {
                    if (TxtCompteurRetard != null)
                        TxtCompteurRetard.Text = nb == 0
                            ? "✅ 0 retard"
                            : $"🔴 {nb} retard{(nb > 1 ? "s" : "")}";
                });
            }
            catch { /* silencieux */ }
        }


        // ================================================================
        // DOUBLE-CLIC SUR UNE LIGNE → OUVRIR MODALE EN MODE ÉDITION
        // ================================================================
        private void GridCommandes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridCommandes.SelectedItem is not Commande cmd) return;
            OuvrirModalEdition(cmd);
        }

        // Appelé également par GridCommandes_SelectionChanged pour mettre à jour
        // _commandeSelectionneeId (utilisé par les méthodes legacy du panneau droit)
        private void GridCommandes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridCommandes.SelectedItem is not Commande cmd) return;
            _commandeSelectionneeId = cmd.IdCommande;

            // ── Mise à jour legacy (panneau droit caché mais code-behind référencé) ──
            _chargementEnCours = true;
            try
            {
                CmbClient.SelectedValue  = cmd.IdClient;
                TxtHeureDebut.Text       = cmd.HeureDebut.ToString(@"hh\:mm");
                TxtHeureFin.Text         = cmd.HeureFin?.ToString(@"hh\:mm") ?? "";
                DateFin.SelectedDate     = cmd.DateFin;
                _piecesCommande          = _commandeService.ObtenirPiecesCommande(cmd.IdCommande);
                RafraichirListePieces();
                if (_piecesCommande.Count > 0)
                {
                    var premiere = _piecesCommande[0];
                    _pieceSelectionneeId = premiere.IdPieceCommande;
                    AfficherFormulairePiece(false);
                    var typeMatch = _typesVetement.FirstOrDefault(t => t.Nom == premiere.TypeVetement);
                    if (typeMatch != null) CmbTypeVetement.SelectedValue = typeMatch.IdTypeVetement;
                    TxtMontant.Text = premiere.MontantCouture.ToString();
                    if (premiere.IdCouturier.HasValue) CmbCouturier.SelectedValue = premiere.IdCouturier.Value;
                    BtnForcerStatut.Visibility =
                        (_piecesCommande.Count > 1 && (_roleUtilisateur == "Boss" || _roleUtilisateur == "Secretaire"))
                        ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    AfficherFormulairePiece(true);
                }
                BtnCreer.Visibility = _roleUtilisateur == "Couturier"
                    ? Visibility.Collapsed : Visibility.Visible;
            }
            finally { _chargementEnCours = false; }

            if (_pieceSelectionneeId.HasValue) RafraichirMateriaux();
            MettreAJourBoutonsWhatsApp(cmd);
        }

        // ================================================================
        // 5 ICÔNES D'ACTION PAR LIGNE
        // ================================================================

        /// <summary>🏷️ Fiche Atelier — réutilise BtnFicheAtelier_Click existant</summary>
        private void BtnActionFicheAtelier_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Commande cmd) return;
            try
            {
                var commandeComplete = _commandeService.ObtenirParId(cmd.IdCommande);
                if (commandeComplete == null)
                { MessageBox.Show("Commande introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                var fiche = FicheAtelierWindow.Creer(commandeComplete);
                fiche.Owner = Window.GetWindow(this);
                fiche.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur fiche atelier : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>🖨️ Reçu — dernier paiement de la commande</summary>
        private void BtnActionRecu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Commande cmd) return;

            var paiements = _paiementService.ObtenirParCommande(cmd.IdCommande)
                .Where(p => !p.EstAnnule)
                .OrderByDescending(p => p.DatePaiement)
                .ToList();

            if (paiements.Count == 0)
            {
                MessageBox.Show("Aucun paiement enregistré pour cette commande.\nEnregistrez d'abord un paiement.",
                    "Pas de reçu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dernier   = paiements[0];
            var commande  = _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.MaterielSupplements)
                .FirstOrDefault(c => c.IdCommande == cmd.IdCommande);

            if (commande == null) { MessageBox.Show("Commande introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var mesures = commande.Pieces.SelectMany(p => p.Mesures).ToList();
            var fenetre = new FenetreRecu(commande, dernier, mesures, dernier.NomOperateur, _receiptService);
            fenetre.Owner = Window.GetWindow(this);
            fenetre.Show();
        }

        /// <summary>✏️ Éditer — ouvre la modale en mode édition (même logique que double-clic)</summary>
        private void BtnActionEditer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Commande cmd) return;
            OuvrirModalEdition(cmd);
        }


        private void BtnActionPaiement_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Commande cmd) return;

            // Fix 3 — Sidebar : activer le bouton Paiements dans la sidebar
            if (Window.GetWindow(this) is MainWindow mainWin)
                mainWin.ActiverBoutonPaiements();

            NavigationService?.Navigate(new PaiementsView(cmd.IdCommande));
        }

        /// <summary>💬 WhatsApp — factorisé : Prête si Terminée/Livrée, sinon RDV</summary>
        private void BtnActionWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Commande cmd) return;
            _ = EnvoyerWhatsAppApproprie(cmd);
        }

        /// <summary>
        /// Méthode commune WhatsApp : appelle NotifierCommandePreteAsync si statut
        /// Terminée/Livrée, sinon NotifierRappelRdvAsync s'il y a une date de RDV.
        /// Regroupe l'ancienne logique de BtnWhatsAppPrete_Click et BtnWhatsAppRdv_Click.
        /// </summary>
        private async Task EnvoyerWhatsAppApproprie(Commande cmd)
        {
            if (cmd == null) return;
            if (string.IsNullOrWhiteSpace(cmd.Client?.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone.",
                    "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                string statut = cmd.StatutGlobalAffiche ?? "";
                if (statut == "Terminée" || statut == "Livrée")
                    await _whatsApp.NotifierCommandePreteAsync(cmd);
                else if (cmd.DateFin != default)
                    await _whatsApp.NotifierRappelRdvAsync(cmd);
                else
                    MessageBox.Show("Aucune date de RDV définie pour cette commande.",
                        "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir WhatsApp :\n" + ex.Message,
                    "Erreur WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>🗑️ Supprimer — réutilise exactement BtnSupprimer_Click</summary>
        private async void BtnActionSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Commande cmd) return;
            _commandeSelectionneeId = cmd.IdCommande;
            await SupprimerCommandeAsync(_commandeSelectionneeId);
        }


        // ================================================================
        // MODALE — OUVERTURE / FERMETURE
        // ================================================================

        private void BtnOuvrirModalCommande_Click(object sender, RoutedEventArgs e)
        {
            OuvrirModalCreation();
        }

        private void BtnFermerModal_Click(object sender, RoutedEventArgs e)
        {
            FermerModal();
        }

        private void OverlayFond_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Clic sur le fond semi-opaque ferme la modale
            FermerModal();
        }

        private void FermerModal()
        {
            OverlayModalCommande.Visibility = Visibility.Collapsed;
            _cartesPieces.Clear();
            _commandeEnEdition = null;
        }

        private void OuvrirModalCreation()
        {
            _modalModeCreation = true;
            _commandeEnEdition  = null;
            _cartesPieces.Clear();

            TxtTitreModal.Text     = "Nouvelle Commande";
            TxtSousTitreModal.Text = "Remplissez les informations de la commande";
            TxtBtnValider.Text     = "Valider la Commande";

            // Réinitialiser les champs de la modale
            CmbModalClient.SelectedIndex    = -1;
            var (dateRdv, heureRdv)         = CalculerRdvParDefaut();
            DpModalDateFin.SelectedDate     = dateRdv;
            TxtHeureDebut.Text              = DateTime.Now.ToString("HH:mm");
            TxtModalHeure.Text              = heureRdv.ToString(@"hh\:mm");
            TxtModalAcompte.Text            = "0";
            CmbModalModePaiement.SelectedIndex = 0;

            // Acompte visible seulement en création
            PanneauAcompte.Visibility = Visibility.Visible;

            // Bouton Forcer statut invisible en création
            BtnForcerStatut.Visibility = Visibility.Collapsed;

            // Vider le panneau des cartes
            PanneauCartesPieces.Children.Clear();

            // Ajouter une première carte vide
            AjouterCarte(null);

            OverlayModalCommande.Visibility = Visibility.Visible;
        }

        private void OuvrirModalEdition(Commande cmd)
        {
            _modalModeCreation = true; // désactivé en édition → pas d'acompte
            _commandeEnEdition  = _commandeService.ObtenirParId(cmd.IdCommande);
            if (_commandeEnEdition == null)
            {
                MessageBox.Show("Commande introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _modalModeCreation = false;
            _cartesPieces.Clear();

            TxtTitreModal.Text     = $"Modifier la Commande #{cmd.IdCommande}";
            TxtSousTitreModal.Text = "Modifiez les informations, puis sauvegardez.";
            TxtBtnValider.Text     = "Enregistrer les modifications";

            // Pré-remplir en-tête
            CmbModalClient.SelectedValue    = _commandeEnEdition.IdClient;
            DpModalDateFin.SelectedDate     = _commandeEnEdition.DateFin;
            TxtHeureDebut.Text              = _commandeEnEdition.HeureDebut.ToString(@"hh\:mm");
            TxtModalHeure.Text              = _commandeEnEdition.HeureFin?.ToString(@"hh\:mm") ?? "";

            // Acompte caché en édition
            PanneauAcompte.Visibility = Visibility.Collapsed;

            // Bouton Forcer statut : Boss uniquement, mode édition
            BtnForcerStatut.Visibility =
                (_roleUtilisateur == "Boss" && _commandeEnEdition.Pieces.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            // Charger les pièces existantes, une carte par pièce
            PanneauCartesPieces.Children.Clear();
            var pieces = _commandeService.ObtenirPiecesCommande(cmd.IdCommande);
            foreach (var piece in pieces)
                AjouterCarte(piece);

            OverlayModalCommande.Visibility = Visibility.Visible;
        }

        private void BtnAjouterPieceModal_Click(object sender, RoutedEventArgs e)
        {
            AjouterCarte(null);
        }


        // ================================================================
        // CLASSE INTERNE — Carte de pièce dans la modale
        // ================================================================
        private class Cartepiece
        {
            public int? IdPieceCommande { get; set; }   // null = nouvelle pièce
            public Border Conteneur    { get; set; } = null!;

            // Champs UI
            public ComboBox CmbType    { get; set; } = null!;
            public ComboBox CmbCouturier { get; set; } = null!;
            public TextBox  TxtPrix    { get; set; } = null!;
            public ComboBox CmbDesc    { get; set; } = null!;
            public ComboBox CmbStatutPiece { get; set; } = null!;
            public StackPanel PanelMesures { get; set; } = null!;
            public Image     ImgPiece   { get; set; } = null!;
            public TextBlock TxtPhotoPlaceholderCarte { get; set; } = null!;
            public Button    BtnSupprimerPhotoCarte   { get; set; } = null!;
            public string    CheminPhoto { get; set; } = "";

            // Matériaux — buffer (avant sauvegarde) + UI
            public List<MaterielSupplement> MateriauxBuffer { get; set; } = new();
            public StackPanel PanelListeMateriaux { get; set; } = null!;  // lignes existantes
            public TextBlock  TxtTotalMatCarte    { get; set; } = null!;  // "Total mat. : X FCFA"
        }

        // ================================================================
        // CONSTRUCTION DYNAMIQUE D'UNE CARTE DE PIÈCE
        // ================================================================
        private void AjouterCarte(PieceCommande? pieceExistante)
        {
            var carte = new Cartepiece
            {
                IdPieceCommande = pieceExistante?.IdPieceCommande
            };

            // ── Conteneur carte ──
            // ── Palette de couleurs tournante par numéro de pièce ──
            int numCarte = _cartesPieces.Count + 1;
            // Chaque pièce a une teinte d'accent légèrement différente (max 6 couleurs)
            var (accentHex, accentLight) = (numCarte % 6) switch
            {
                1 => ("#CC0000", "#FFF5F5"),   // rouge
                2 => ("#1D4ED8", "#EFF6FF"),   // bleu
                3 => ("#059669", "#F0FDF4"),   // vert
                4 => ("#D97706", "#FFFBEB"),   // ambre
                5 => ("#7C3AED", "#F5F3FF"),   // violet
                0 => ("#0891B2", "#F0F9FF"),   // cyan
                _ => ("#CC0000", "#FFF5F5")
            };
            var accentColor   = (Color)ColorConverter.ConvertFromString(accentHex);
            var accentBrush   = new SolidColorBrush(accentColor);
            var accentLightBg = (Color)ColorConverter.ConvertFromString(accentLight);

            // ── Bordure pointillée via DrawingBrush ──
            // WPF n'a pas de StrokeDashArray sur Border — on simule avec un DrawingBrush.
            var tiretBrush = new DrawingBrush
            {
                TileMode   = TileMode.Tile,
                Viewport   = new Rect(0, 0, 8, 1),
                ViewportUnits = BrushMappingMode.Absolute,
                Drawing    = new GeometryDrawing
                {
                    Brush    = accentBrush,
                    Geometry = new LineGeometry(new Point(0, 0.5), new Point(5, 0.5))
                }
            };

            // Conteneur principal avec bordure pleine colorée à gauche + fond clair
            var border = new Border
            {
                Background      = new SolidColorBrush(accentLightBg),
                BorderBrush     = accentBrush,
                BorderThickness = new Thickness(3, 0, 0, 0),  // barre gauche colorée
                CornerRadius    = new CornerRadius(0, 8, 8, 0),
                Margin          = new Thickness(0, 0, 0, 14),
                // Bordure externe pointillée émulée via un Border imbriqué
            };

            // Enveloppe extérieure avec bordure pointillée (simulée par tirets)
            var enveloppe = new Border
            {
                BorderBrush     = new SolidColorBrush(accentColor) { Opacity = 0.35 },
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Margin          = new Thickness(0, 0, 0, 14),
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color     = accentColor,
                    Opacity   = 0.08,
                    BlurRadius = 8,
                    ShadowDepth = 2,
                    Direction  = 270
                }
            };
            enveloppe.Child = border;
            border.Margin   = new Thickness(0);  // reset après imbrication
            carte.Conteneur = enveloppe;

            var sp = new StackPanel { Margin = new Thickness(0) };
            border.Child = sp;

            // ── Bandeau titre en haut de la carte ──
            var bandeau = new Border
            {
                Background   = accentBrush,
                CornerRadius = new CornerRadius(0, 8, 0, 0),
                Padding      = new Thickness(14, 8, 12, 8)
            };
            var bandeauGrid = new Grid();
            bandeauGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bandeauGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bandeauGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Badge numéro
            var badgeNum = new Border
            {
                Background   = new SolidColorBrush(Colors.White) { Opacity = 0.25 },
                CornerRadius = new CornerRadius(12),
                Padding      = new Thickness(8, 2, 8, 2),
                Margin       = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            int num = _cartesPieces.Count + 1;
            badgeNum.Child = new TextBlock
            {
                Text       = $"#{num}",
                FontSize   = 11, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Grid.SetColumn(badgeNum, 0);
            bandeauGrid.Children.Add(badgeNum);

            var lblNum = new TextBlock
            {
                Text      = $"Pièce {num}",
                FontSize  = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblNum, 1);
            bandeauGrid.Children.Add(lblNum);

            // Boutons Dupliquer / Supprimer dans le bandeau
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(btnRow, 2);
            bandeauGrid.Children.Add(btnRow);

            var btnDup = CreerBoutonIconeSurBandeau("📋", "Dupliquer cette pièce");
            btnDup.Tag = carte;
            btnDup.Click += BtnDupliquerCartePiece_Click;
            btnRow.Children.Add(btnDup);

            var btnSup = CreerBoutonIconeSurBandeau("🗑️", "Supprimer cette pièce");
            btnSup.Tag = carte;
            btnSup.Click += BtnSupprimerCartePiece_Click;
            // Secrétaire peut supprimer, Boss aussi
            if (_roleUtilisateur == "Couturier")
                btnSup.IsEnabled = false;
            btnRow.Children.Add(btnSup);

            bandeau.Child = bandeauGrid;
            sp.Children.Add(bandeau);

            // Corps de la carte avec padding interne
            var corps = new StackPanel { Margin = new Thickness(14, 10, 14, 14) };
            sp.Children.Add(corps);

            // Rediriger les ajouts suivants vers corps au lieu de sp
            // (on utilise une variable locale "conteneurChamps")
            var conteneurChamps = corps;

            // ── Ligne 1 : Type vêtement + Couturier ──
            var grille1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grille1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grille1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grille1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spType = new StackPanel(); Grid.SetColumn(spType, 0);
            spType.Children.Add(new TextBlock { Text = "Type de vêtement *",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4) });
            var cmbType = new ComboBox { Height = 36, FontSize = 12 };
            cmbType.ItemsSource = _typesVetement.Select(t => new
                { t.IdTypeVetement, DisplayText = t.Nom + " (" + t.PrixBase + " FCFA)" }).ToList();
            cmbType.DisplayMemberPath  = "DisplayText";
            cmbType.SelectedValuePath  = "IdTypeVetement";
            carte.CmbType = cmbType;
            cmbType.Tag   = carte;
            cmbType.SelectionChanged += CmbTypeCarte_SelectionChanged;
            spType.Children.Add(cmbType);
            grille1.Children.Add(spType);

            var spCout = new StackPanel(); Grid.SetColumn(spCout, 2);
            spCout.Children.Add(new TextBlock { Text = "Couturier",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4) });
            var cmbCout = new ComboBox { Height = 36, FontSize = 12,
                DisplayMemberPath = "Prenom", SelectedValuePath = "IdEmploye" };
            cmbCout.ItemsSource = _couturiers;
            carte.CmbCouturier  = cmbCout;
            spCout.Children.Add(cmbCout);
            grille1.Children.Add(spCout);
            conteneurChamps.Children.Add(grille1);

            // ── Ligne 2 : Prix + Description ──
            var grille2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grille2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grille2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grille2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spPrix = new StackPanel(); Grid.SetColumn(spPrix, 0);
            spPrix.Children.Add(new TextBlock { Text = "Prix confection (FCFA) *",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4) });
            var txtPrix = new TextBox { Height = 36, FontSize = 12, Padding = new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                BorderThickness = new Thickness(1) };
            txtPrix.PreviewTextInput += TxtPrixCarte_PreviewTextInput;
            carte.TxtPrix = txtPrix;
            // Secrétaire ne peut pas changer le prix en édition
            if (pieceExistante != null && _roleUtilisateur == "Secretaire")
            {
                txtPrix.IsReadOnly = true;
                txtPrix.Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xEC, 0xE8));
                txtPrix.ToolTip    = "Seul le Boss peut modifier le prix d'une pièce enregistrée.";
            }
            spPrix.Children.Add(txtPrix);
            grille2.Children.Add(spPrix);

            var spDesc = new StackPanel(); Grid.SetColumn(spDesc, 2);
            spDesc.Children.Add(new TextBlock { Text = "Description",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4) });
            var cmbDesc = new ComboBox { Height = 36, FontSize = 12, IsEditable = true };
            carte.CmbDesc = cmbDesc;
            spDesc.Children.Add(cmbDesc);
            grille2.Children.Add(spDesc);
            conteneurChamps.Children.Add(grille2);

            // ── Statut de la pièce ──
            var spStatut = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            spStatut.Children.Add(new TextBlock { Text = "Statut",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4) });
            var cmbStatut = new ComboBox { Height = 36, FontSize = 12, Width = 160,
                HorizontalAlignment = HorizontalAlignment.Left };
            cmbStatut.Items.Add(new ComboBoxItem { Content = "A faire" });
            cmbStatut.Items.Add(new ComboBoxItem { Content = "En cours" });
            // En édition, proposer aussi Terminee et Livree
            if (pieceExistante != null)
            {
                cmbStatut.Items.Add(new ComboBoxItem { Content = "Terminee" });
                cmbStatut.Items.Add(new ComboBoxItem { Content = "Livree" });
            }
            cmbStatut.SelectedIndex = 0;
            carte.CmbStatutPiece = cmbStatut;
            spStatut.Children.Add(cmbStatut);
            conteneurChamps.Children.Add(spStatut);

            // ── Mesures dynamiques (panneau vide, rempli au choix du type) ──
            var panelMesures = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            carte.PanelMesures = panelMesures;
            conteneurChamps.Children.Add(panelMesures);

            // ── Photo ──
            var spPhoto = new StackPanel { Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6) };
            var imgBorder = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xEC, 0xE8)),
                Margin = new Thickness(0, 0, 10, 0), ClipToBounds = true };
            var img = new Image { Stretch = Stretch.UniformToFill };
            imgBorder.Child = img;
            var txtPhPl = new TextBlock { Text = "📷", FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center };
            imgBorder.Child = new Grid();
            ((Grid)imgBorder.Child).Children.Add(img);
            ((Grid)imgBorder.Child).Children.Add(txtPhPl);
            carte.ImgPiece = img;
            carte.TxtPhotoPlaceholderCarte = txtPhPl;
            spPhoto.Children.Add(imgBorder);

            var spBtnsPhoto = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var btnPrendre = new Button { Content = "📷 Webcam", Height = 28, Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11, Margin = new Thickness(0, 0, 0, 4),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Cursor = Cursors.Hand };
            btnPrendre.Tag   = carte;
            btnPrendre.Click += BtnPrendrePhotoCarte_Click;
            spBtnsPhoto.Children.Add(btnPrendre);

            var btnImporter = new Button { Content = "📁 Importer", Height = 28, Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11, Margin = new Thickness(0, 0, 0, 4),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Cursor = Cursors.Hand };
            btnImporter.Tag   = carte;
            btnImporter.Click += BtnImporterPhotoCarte_Click;
            spBtnsPhoto.Children.Add(btnImporter);

            var btnSupPhoto = new Button { Content = "🗑️ Supprimer", Height = 28, Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11, Visibility = Visibility.Collapsed,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            btnSupPhoto.Tag   = carte;
            btnSupPhoto.Click += BtnSupprimerPhotoCarte_Click;
            carte.BtnSupprimerPhotoCarte = btnSupPhoto;
            spBtnsPhoto.Children.Add(btnSupPhoto);

            spPhoto.Children.Add(spBtnsPhoto);
            conteneurChamps.Children.Add(spPhoto);

            // ── MATÉRIAUX / SUPPLÉMENTS ─────────────────────────────
            // Séparateur
            conteneurChamps.Children.Add(new Separator
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE0, 0xDC)),
                Margin = new Thickness(0, 6, 0, 10)
            });

            // En-tête matériaux
            var headerMat = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            headerMat.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerMat.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stackTitreMat = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(stackTitreMat, 0);
            stackTitreMat.Children.Add(new TextBlock
            {
                Text = "🧵  Matériaux / Suppléments",
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerMat.Children.Add(stackTitreMat);

            // Total matériaux (badge sombre)
            var borderTotalMat = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3)
            };
            var txtTotalMat = new TextBlock
            {
                Text = "0 FCFA", FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
            };
            borderTotalMat.Child = txtTotalMat;
            Grid.SetColumn(borderTotalMat, 1);
            headerMat.Children.Add(borderTotalMat);
            carte.TxtTotalMatCarte = txtTotalMat;

            conteneurChamps.Children.Add(headerMat);

            // Sous-titre informatif
            conteneurChamps.Children.Add(new TextBlock
            {
                Text = "Facturés au client · Exclus de la commission couturier",
                FontSize = 10, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Liste des lignes matériaux
            var panelListeMat = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            carte.PanelListeMateriaux = panelListeMat;
            conteneurChamps.Children.Add(panelListeMat);

            // Message "aucun matériau"
            var txtAucunMat = new TextBlock
            {
                Text = "Aucun matériau ajouté.",
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            panelListeMat.Children.Add(txtAucunMat);

            // ── Mini-formulaire d'ajout ─────────────────────────────
            var panelFormMat = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF5, 0xF3)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xC8, 0xBE)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = Visibility.Collapsed
            };
            var spFormMat = new StackPanel();
            panelFormMat.Child = spFormMat;

            // Désignation
            spFormMat.Children.Add(new TextBlock
            {
                Text = "Désignation *", FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 3)
            });
            var txtDesigMat = new TextBox
            {
                Height = 32, FontSize = 11, Padding = new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 6)
            };
            spFormMat.Children.Add(txtDesigMat);

            // Quantité + Prix sur même ligne
            var grilleMat = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grilleMat.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grilleMat.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grilleMat.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            var spQte = new StackPanel(); Grid.SetColumn(spQte, 0);
            spQte.Children.Add(new TextBlock { Text = "Qté", FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 3) });
            var txtQteMat = new TextBox { Height = 32, FontSize = 11, Text = "1",
                Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)), BorderThickness = new Thickness(1) };
            spQte.Children.Add(txtQteMat);
            grilleMat.Children.Add(spQte);

            var spPrixMat = new StackPanel(); Grid.SetColumn(spPrixMat, 2);
            spPrixMat.Children.Add(new TextBlock { Text = "Prix unitaire (FCFA) *", FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 3) });
            var txtPrixMat = new TextBox { Height = 32, FontSize = 11,
                Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)), BorderThickness = new Thickness(1) };
            spPrixMat.Children.Add(txtPrixMat);
            grilleMat.Children.Add(spPrixMat);
            spFormMat.Children.Add(grilleMat);

            // Boutons Valider / Annuler formulaire
            var stackBtnsFormMat = new StackPanel { Orientation = Orientation.Horizontal };
            var btnValiderMat = new Button
            {
                Content = "✔  Ajouter", Height = 30, Padding = new Thickness(12, 0, 12, 0),
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0)
            };
            var btnAnnulerFormMat = new Button
            {
                Content = "Annuler", Height = 30, Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11, Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))
            };
            stackBtnsFormMat.Children.Add(btnValiderMat);
            stackBtnsFormMat.Children.Add(btnAnnulerFormMat);
            spFormMat.Children.Add(stackBtnsFormMat);
            conteneurChamps.Children.Add(panelFormMat);

            // Bouton "+ Ajouter un matériau"
            var btnOuvrirFormMat = new Button
            {
                Height = 30, Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11, Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xC8, 0xBE)),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x5B, 0x4E)),
                Content = "+ Ajouter un matériau / supplément"
            };
            conteneurChamps.Children.Add(btnOuvrirFormMat);

            // ── Handlers matériaux inline ───────────────────────────
            // Ouvrir / fermer le formulaire
            btnOuvrirFormMat.Click += (s, ev) =>
            {
                panelFormMat.Visibility = panelFormMat.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                if (panelFormMat.Visibility == Visibility.Visible)
                {
                    txtDesigMat.Text = "";
                    txtQteMat.Text   = "1";
                    txtPrixMat.Text  = "";
                    txtDesigMat.Focus();
                }
            };

            btnAnnulerFormMat.Click += (s, ev) =>
            {
                panelFormMat.Visibility = Visibility.Collapsed;
            };

            // Valider l'ajout d'un matériau
            btnValiderMat.Click += (s, ev) =>
            {
                string desig = txtDesigMat.Text.Trim();
                if (string.IsNullOrEmpty(desig))
                {
                    MessageBox.Show("La désignation est obligatoire.", "Champ manquant",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtDesigMat.Focus(); return;
                }
                if (!int.TryParse(txtQteMat.Text.Trim(), out int qte) || qte <= 0)
                {
                    MessageBox.Show("La quantité doit être un entier positif.", "Valeur invalide",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtQteMat.Focus(); return;
                }
                if (!decimal.TryParse(txtPrixMat.Text.Trim().Replace(" ", ""),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal prixMat) || prixMat < 0)
                {
                    MessageBox.Show("Le prix unitaire doit être un nombre positif.", "Valeur invalide",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPrixMat.Focus(); return;
                }

                // Si la pièce est déjà en base → sauver directement
                if (carte.IdPieceCommande.HasValue && _commandeSelectionneeId > 0)
                {
                    try
                    {
                        var (idOp, nomOp) = OperateurConnecte();
                        var mat = new MaterielSupplement
                        {
                            IdCommande = _commandeSelectionneeId,
                            IdPieceCommande = carte.IdPieceCommande.Value,
                            Designation = desig, Quantite = qte, PrixUnitaire = prixMat
                        };
                        _materielService.Ajouter(mat, idOp, nomOp);
                        AjouterLigneMateriau(carte, mat, panelListeMat, txtTotalMat);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Erreur : " + ex.Message, "Erreur",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    // En création : stocker dans le buffer (sauvé avec la pièce)
                    var mat = new MaterielSupplement
                    {
                        Designation = desig, Quantite = qte, PrixUnitaire = prixMat
                    };
                    carte.MateriauxBuffer.Add(mat);
                    AjouterLigneMateriau(carte, mat, panelListeMat, txtTotalMat);
                }

                panelFormMat.Visibility = Visibility.Collapsed;
            };

            // ── Charger les matériaux existants (mode édition) ──────
            if (pieceExistante != null && pieceExistante.IdPieceCommande > 0)
            {
                try
                {
                    var matExistants = _materielService.ObtenirParPiece(pieceExistante.IdPieceCommande);
                    foreach (var mat in matExistants)
                        AjouterLigneMateriau(carte, mat, panelListeMat, txtTotalMat);
                }
                catch { /* silencieux si service non disponible */ }
            }
            // ── Fin section matériaux ────────────────────────────────

            // ── Pré-remplir depuis pièce existante ──
            if (pieceExistante != null)
            {
                var typeMatch = _typesVetement.FirstOrDefault(t => t.Nom == pieceExistante.TypeVetement);
                if (typeMatch != null)
                {
                    cmbType.SelectedValue = typeMatch.IdTypeVetement;
                    // Les mesures sont remplies par l'événement SelectionChanged du combo
                }
                txtPrix.Text = pieceExistante.MontantCouture.ToString();
                if (pieceExistante.IdCouturier.HasValue)
                    cmbCout.SelectedValue = pieceExistante.IdCouturier.Value;

                // Description
                if (typeMatch?.Descriptions != null)
                {
                    cmbDesc.ItemsSource = typeMatch.Descriptions.ToList();
                    var descMatch = typeMatch.Descriptions.FirstOrDefault(d => d.Texte == pieceExistante.DescriptionPrecision);
                    if (descMatch != null) cmbDesc.SelectedItem = descMatch;
                    else cmbDesc.Text = pieceExistante.DescriptionPrecision ?? "";
                }
                else cmbDesc.Text = pieceExistante.DescriptionPrecision ?? "";

                // Statut
                for (int i = 0; i < cmbStatut.Items.Count; i++)
                {
                    if ((cmbStatut.Items[i] as ComboBoxItem)?.Content?.ToString() == pieceExistante.Statut)
                    { cmbStatut.SelectedIndex = i; break; }
                }

                // Remplir les mesures après coup (elles sont créées par CmbTypeCarte_SelectionChanged)
                Dispatcher.InvokeAsync(() =>
                {
                    var mesures = _commandeService.ObtenirMesuresPiece(pieceExistante.IdPieceCommande);
                    RemplirMesuresDansCarte(carte, mesures);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // Photo
                carte.CheminPhoto = pieceExistante.CheminPhoto ?? "";
                if (!string.IsNullOrEmpty(carte.CheminPhoto) && System.IO.File.Exists(carte.CheminPhoto))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource   = new Uri(carte.CheminPhoto);
                    bmp.EndInit();
                    bmp.Freeze();
                    img.Source    = bmp;
                    txtPhPl.Visibility = Visibility.Collapsed;
                    btnSupPhoto.Visibility = Visibility.Visible;
                }
            }

            _cartesPieces.Add(carte);
            PanneauCartesPieces.Children.Add(enveloppe);
        }

        private static Button CreerBoutonIcone(string emoji, string tooltip)
        {
            return new Button
            {
                Content         = emoji, Width = 28, Height = 26, FontSize = 13,
                Background      = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor          = Cursors.Hand, Margin = new Thickness(2, 0, 0, 0),
                ToolTip         = tooltip
            };
        }

        // Version pour les boutons dans le bandeau coloré (fond blanc semi-transparent)
        private static Button CreerBoutonIconeSurBandeau(string emoji, string tooltip)
        {
            var btn = new Button
            {
                Content         = emoji, Width = 28, Height = 26, FontSize = 13,
                Background      = new SolidColorBrush(Colors.White) { Opacity = 0.18 },
                BorderThickness = new Thickness(0),
                Cursor          = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0),
                ToolTip         = tooltip,
                Foreground      = Brushes.White
            };
            return btn;
        }

        // ================================================================
        // HELPER — Ajouter une ligne de matériau dans le panneau d'une carte
        // ================================================================
        private void AjouterLigneMateriau(
            Cartepiece carte,
            MaterielSupplement mat,
            StackPanel panelListeMat,
            TextBlock txtTotalMat)
        {
            // Retirer le message "Aucun matériau" s'il est encore là
            var toClear = panelListeMat.Children
                .OfType<TextBlock>()
                .FirstOrDefault(tb => tb.Text == "Aucun matériau ajouté.");
            if (toClear != null) panelListeMat.Children.Remove(toClear);

            var ligne = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE2, 0xDC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            // Désignation
            var lblDesig = new TextBlock
            {
                Text = mat.Designation, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblDesig, 0);

            // Qté × prix
            var lblQtePrix = new TextBlock
            {
                Text = $"{mat.Quantite} × {mat.PrixUnitaire:N0} F", FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(lblQtePrix, 1);

            // Montant total ligne
            var borderMontant = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xF0)),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            borderMontant.Child = new TextBlock
            {
                Text = $"{mat.Montant:N0} FCFA", FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00))
            };
            Grid.SetColumn(borderMontant, 2);

            // Bouton supprimer
            var btnSup = new Button
            {
                Content = "✕", Width = 22, Height = 22, FontSize = 10, FontWeight = FontWeights.Bold,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(btnSup, 3);

            // Supprimer la ligne
            btnSup.Click += (s, ev) =>
            {
                var conf = MessageBox.Show("Supprimer ce matériau ?", "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (conf != MessageBoxResult.Yes) return;

                if (mat.IdMateriel > 0)
                {
                    try { _materielService.Supprimer(mat.IdMateriel); }
                    catch (InvalidOperationException ex)
                    {
                        MessageBox.Show(ex.Message, "Suppression impossible",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else
                {
                    carte.MateriauxBuffer.Remove(mat);
                }

                panelListeMat.Children.Remove(ligne);
                MettreAJourTotalMatCarte(carte, panelListeMat, txtTotalMat);
                if (panelListeMat.Children.Count == 0)
                {
                    panelListeMat.Children.Add(new TextBlock
                    {
                        Text = "Aucun matériau ajouté.", FontSize = 11, FontStyle = FontStyles.Italic,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                        Margin = new Thickness(0, 0, 0, 6)
                    });
                }
            };

            g.Children.Add(lblDesig);
            g.Children.Add(lblQtePrix);
            g.Children.Add(borderMontant);
            g.Children.Add(btnSup);
            ligne.Child = g;
            panelListeMat.Children.Add(ligne);

            MettreAJourTotalMatCarte(carte, panelListeMat, txtTotalMat);
        }

        // Recalcule et affiche le total matériaux d'une carte
        private void MettreAJourTotalMatCarte(
            Cartepiece carte,
            StackPanel panelListeMat,
            TextBlock txtTotalMat)
        {
            // Total = lignes Border dans le panel (on exclut les TextBlock "Aucun matériau")
            decimal total = 0m;
            foreach (var child in panelListeMat.Children)
            {
                if (child is Border b && b.Child is Grid g)
                {
                    // Chercher le TextBlock montant dans la colonne 2
                    foreach (UIElement el in g.Children)
                    {
                        if (el is Border bMontant && Grid.GetColumn(bMontant) == 2
                            && bMontant.Child is TextBlock tb)
                        {
                            string txt = tb.Text.Replace(" FCFA", "").Replace(" ", "").Replace("\u00A0", "");
                            if (decimal.TryParse(txt, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out decimal m))
                                total += m;
                        }
                    }
                }
            }
            txtTotalMat.Text = total > 0 ? $"{total:N0} FCFA" : "0 FCFA";
        }


        // ================================================================
        // ÉVÉNEMENTS DES CARTES DE PIÈCES
        // ================================================================

        private void CmbTypeCarte_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cmb || cmb.Tag is not Cartepiece carte) return;
            if (cmb.SelectedValue == null) return;
            if (!int.TryParse(cmb.SelectedValue.ToString(), out int idType)) return;

            var type = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idType);
            if (type == null) return;

            // Prix par défaut
            if (string.IsNullOrEmpty(carte.TxtPrix.Text) || carte.TxtPrix.Text == "0")
                carte.TxtPrix.Text = type.PrixBase.ToString();

            // Descriptions
            if (type.Descriptions != null && type.Descriptions.Any())
                carte.CmbDesc.ItemsSource = type.Descriptions.ToList();
            else
                carte.CmbDesc.ItemsSource = new List<DescriptionCourante>();

            // Mesures dynamiques
            carte.PanelMesures.Children.Clear();
            if (type.MesuresRequises != null && type.MesuresRequises.Any())
            {
                var header = new TextBlock
                {
                    Text = $"{type.MesuresRequises.Count} mesure(s) en cm :",
                    FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                carte.PanelMesures.Children.Add(header);

                foreach (var mesure in type.MesuresRequises)
                {
                    if (string.IsNullOrWhiteSpace(mesure.NomMesure)) continue;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                    row.Children.Add(new TextBlock
                    {
                        Text = mesure.NomMesure, Width = 140, FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    var combo = new ComboBox
                    {
                        Width = 90, FontSize = 11, Tag = mesure.NomMesure, IsEditable = true
                    };
                    for (int i = 20; i <= 300; i++)
                        combo.Items.Add((i * 0.5).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " cm");
                    combo.SelectedIndex = 0;
                    row.Children.Add(combo);
                    carte.PanelMesures.Children.Add(row);
                }
            }
        }

        private void RemplirMesuresDansCarte(Cartepiece carte, List<Mesure> mesures)
        {
            foreach (var child in carte.PanelMesures.Children)
            {
                if (child is not StackPanel row) continue;
                if (row.Children.Count < 2 || row.Children[1] is not ComboBox combo) continue;
                string nomMesure = combo.Tag?.ToString() ?? "";
                var mesure = mesures.FirstOrDefault(m => m.NomMesure == nomMesure);
                if (mesure == null) continue;
                bool trouve = false;
                for (int j = 0; j < combo.Items.Count; j++)
                {
                    string? item = combo.Items[j]?.ToString();
                    if (item == mesure.Valeur + " cm" || item?.StartsWith(mesure.Valeur + " ") == true)
                    { combo.SelectedIndex = j; trouve = true; break; }
                }
                if (!trouve) combo.Text = mesure.Valeur + " cm";
            }
        }

        private List<Mesure> CollecterMesuresCarte(Cartepiece carte)
        {
            var mesures = new List<Mesure>();
            foreach (var child in carte.PanelMesures.Children)
            {
                if (child is not StackPanel row) continue;
                if (row.Children.Count < 2 || row.Children[1] is not ComboBox combo) continue;
                string texte = combo.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(texte))
                {
                    string valeur = texte.Replace(" cm", "").Replace("cm", "").Trim();
                    mesures.Add(new Mesure { NomMesure = combo.Tag?.ToString() ?? "", Valeur = valeur });
                }
            }
            return mesures;
        }

        private PieceCommande? LireCartePiece(Cartepiece carte)
        {
            if (carte.CmbType.SelectedValue == null)
            {
                MessageBox.Show("Sélectionnez un type de vêtement pour chaque pièce.",
                    "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            if (!int.TryParse(carte.CmbType.SelectedValue.ToString(), out int idType)) return null;
            var typeObj = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idType);
            if (typeObj == null) return null;

            if (!decimal.TryParse(carte.TxtPrix.Text?.Trim(), out decimal prix) || prix <= 0)
            {
                MessageBox.Show("Le prix de confection doit être positif.",
                    "Prix invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            string statut = (carte.CmbStatutPiece.SelectedItem as ComboBoxItem)?.Content?.ToString()
                            ?? carte.CmbStatutPiece.Text?.Trim()
                            ?? "A faire";
            string description = carte.CmbDesc.SelectedItem is DescriptionCourante dc
                ? dc.Texte : carte.CmbDesc.Text;

            return new PieceCommande
            {
                IdPieceCommande      = carte.IdPieceCommande ?? 0,
                TypeVetement         = typeObj.Nom,
                DescriptionPrecision = description,
                IdCouturier          = carte.CmbCouturier.SelectedValue as int?,
                MontantCouture       = prix,
                Statut               = statut,
                CheminPhoto          = carte.CheminPhoto
            };
        }

        private void BtnDupliquerCartePiece_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Cartepiece carteSource) return;

            // En mode édition, on utilise le service si la pièce est en base
            if (!_modalModeCreation && carteSource.IdPieceCommande.HasValue)
            {
                try
                {
                    _commandeService.DupliquerPiece(carteSource.IdPieceCommande.Value);
                    var newPieces = _commandeService.ObtenirPiecesCommande(_commandeEnEdition!.IdCommande);
                    var nouvellePiece = newPieces.LastOrDefault();
                    if (nouvellePiece != null) AjouterCarte(nouvellePiece);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Duplication impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                // En création : dupliquer visuellement la carte
                AjouterCarte(null);
                // Copier les valeurs de la carte source dans la nouvelle
                var nouv = _cartesPieces.Last();
                nouv.CmbType.SelectedValue    = carteSource.CmbType.SelectedValue;
                nouv.TxtPrix.Text              = carteSource.TxtPrix.Text;
                nouv.CmbCouturier.SelectedValue = carteSource.CmbCouturier.SelectedValue;
                nouv.CmbDesc.Text              = carteSource.CmbDesc.Text;
                nouv.CheminPhoto               = carteSource.CheminPhoto;
            }
        }

        private void BtnSupprimerCartePiece_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Cartepiece carte) return;
            if (_cartesPieces.Count <= 1)
            {
                MessageBox.Show("La commande doit avoir au moins une pièce.",
                    "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rep = MessageBox.Show("Supprimer cette pièce ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (rep != MessageBoxResult.Yes) return;

            if (!_modalModeCreation && carte.IdPieceCommande.HasValue)
            {
                try
                {
                    var (opId, opNom) = OperateurConnecte();
                    _commandeService.SupprimerPiece(carte.IdPieceCommande.Value, opId, opNom);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            PanneauCartesPieces.Children.Remove(carte.Conteneur);
            _cartesPieces.Remove(carte);
        }


        // ================================================================
        // PHOTO DANS LES CARTES
        // ================================================================
        private static readonly string[] ExtensionsAutorisees = { ".jpg", ".jpeg", ".png", ".bmp" };
        private const long TailleMaxOctets = 5 * 1024 * 1024; // 5 Mo (était 1 Mo, trop restrictif)

        private async void BtnImporterPhotoCarte_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Cartepiece carte) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Sélectionner une photo",
                Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var info = new System.IO.FileInfo(dialog.FileName);
                string ext = info.Extension.ToLowerInvariant();
                if (!ExtensionsAutorisees.Contains(ext))
                { MessageBox.Show($"Format non autorisé : {ext}\nFormats acceptés : JPG, PNG, BMP.", "Fichier invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (info.Length > TailleMaxOctets)
                { MessageBox.Show($"Image trop volumineuse ({info.Length / 1024 / 1024.0:F1} Mo).\nTaille maximum : 5 Mo.", "Fichier trop grand", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (!EstImageValide(dialog.FileName))
                { MessageBox.Show("Le fichier sélectionné n'est pas une image valide.", "Fichier invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                string dossier = GestionCoutureApp.Helpers.AppPaths.DossierPhotos;
                string suffixe = Guid.NewGuid().ToString("N")[..8];
                string nomFich = $"photo_{DateTime.Now:yyyyMMdd_HHmmss}_{suffixe}{ext}";
                string dest    = System.IO.Path.Combine(dossier, nomFich);

                // Copie d'abord, puis compression dans un try/finally pour
                // nettoyer le fichier de destination si la compression échoue.
                System.IO.File.Copy(dialog.FileName, dest, overwrite: true);
                try
                {
                    await Task.Run(() => GestionCoutureApp.Helpers.PhotoCompressor.Compresser(dest, dest));
                }
                catch
                {
                    // Compression non critique — on affiche quand même la photo originale
                    // (certains formats BMP ne sont pas compressables par PhotoCompressor)
                }

                ChargerPhotoDansCarte(carte, dest);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de l'import de la photo :\n" + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnPrendrePhotoCarte_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Cartepiece carte) return;
            try
            {
                var win = new WebcamCaptureWindow();
                win.Owner = Window.GetWindow(this);
                if (win.ShowDialog() == true && !string.IsNullOrEmpty(win.CapturedFilePath))
                    ChargerPhotoDansCarte(carte, win.CapturedFilePath);
            }
            catch (Exception ex) { MessageBox.Show("Erreur webcam : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnSupprimerPhotoCarte_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Cartepiece carte) return;
            carte.CheminPhoto = "";
            carte.ImgPiece.Source = null;
            carte.TxtPhotoPlaceholderCarte.Visibility = Visibility.Visible;
            carte.BtnSupprimerPhotoCarte.Visibility   = Visibility.Collapsed;
        }

        private void ChargerPhotoDansCarte(Cartepiece carte, string chemin)
        {
            carte.CheminPhoto = chemin;
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource   = new Uri(chemin);
            bmp.EndInit();
            bmp.Freeze();
            carte.ImgPiece.Source = bmp;
            carte.TxtPhotoPlaceholderCarte.Visibility = Visibility.Collapsed;
            carte.BtnSupprimerPhotoCarte.Visibility   = Visibility.Visible;
        }

        private static bool EstImageValide(string chemin)
        {
            try
            {
                using var fs = new System.IO.FileStream(chemin, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                var header = new byte[8];
                int lu = fs.Read(header, 0, header.Length);
                if (lu < 3) return false;
                if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return true;
                if (lu >= 8 && header[0] == 0x89 && header[1] == 0x50 &&
                    header[2] == 0x4E && header[3] == 0x47) return true;
                if (header[0] == 0x42 && header[1] == 0x4D) return true;
                return false;
            }
            catch { return false; }
        }


        // ================================================================
        // VALIDER LA COMMANDE (création ou édition)
        // ================================================================
        private async void BtnValiderNouvelleCommande_Click(object sender, RoutedEventArgs e)
        {
            if (_modalModeCreation)
                await ValiderCreationCommande();
            else
                await ValiderEditionCommande();
        }

        // ── CRÉATION ──────────────────────────────────────────────────
        private async Task ValiderCreationCommande()
        {
            // 1. Validation en-tête
            if (CmbModalClient.SelectedValue == null)
            { MessageBox.Show("Sélectionnez un client.", "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (DpModalDateFin.SelectedDate == null)
            { MessageBox.Show("Sélectionnez une date de rendez-vous.", "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (DpModalDateFin.SelectedDate < DateTime.Today)
            { MessageBox.Show("La date de RDV ne peut pas être dans le passé.", "Date invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (_cartesPieces.Count == 0)
            { MessageBox.Show("Ajoutez au moins une pièce.", "Pièce manquante", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            // 2. Lire toutes les cartes
            var pieces = new List<(PieceCommande piece, List<Mesure> mesures)>();
            foreach (var carte in _cartesPieces)
            {
                var pieceObj = LireCartePiece(carte);
                if (pieceObj == null) return;
                pieces.Add((pieceObj, CollecterMesuresCarte(carte)));
            }

            // 3. Confirmation Secrétaire
            if (_roleUtilisateur == "Secretaire")
            {
                var rep = MessageBox.Show(
                    "Attention : l'enregistrement de cette commande est irréversible.\n\nConfirmer la création ?",
                    "Confirmation", MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (rep != MessageBoxResult.OK) return;
            }

            // 4. Construire Commande
            var commande = new Commande
            {
                IdClient  = (int)CmbModalClient.SelectedValue,
                DateDebut = DateTime.Now,
                DateFin   = DpModalDateFin.SelectedDate ?? DateTime.Now.AddDays(7),
                HeureDebut = ParseHeure(TxtHeureDebut.Text) ?? DateTime.Now.TimeOfDay,
                HeureFin   = ParseHeure(TxtModalHeure.Text),
                CheminPhoto = pieces[0].piece.CheminPhoto
            };

            var premierePiece  = pieces[0].piece;
            var premieresMesures = pieces[0].mesures;

            BtnValiderNouvelleCommande.IsEnabled = false;
            try
            {
                var (idOp, nomOp) = OperateurConnecte();
                _commandeService.Ajouter(commande, premierePiece, premieresMesures, idOp, nomOp,
                    _cartesPieces[0].MateriauxBuffer.ToList());
                _materiauxTemporaires.Clear();

                // Pièces supplémentaires (à partir de la 2ème)
                for (int i = 1; i < pieces.Count; i++)
                {
                    var (pc, mes) = pieces[i];
                    _commandeService.AjouterPiece(commande.IdCommande, pc, mes,
                        _roleUtilisateur == "Boss", null, _cartesPieces[i].MateriauxBuffer.ToList(), idOp, nomOp);
                }

                await ChargerCommandes();

                // 5. Acompte immédiat si > 0
                decimal acompte = 0;
                decimal.TryParse(TxtModalAcompte.Text?.Trim().Replace(" ", ""), out acompte);

                Paiement? paiementCree = null;
                if (acompte > 0.01m)
                {
                    string modeChoisi = (CmbModalModePaiement.SelectedItem as ComboBoxItem)?.Content?.ToString()
                                        ?? "Espèces";
                    var paiement = new Paiement
                    {
                        IdCommande   = commande.IdCommande,
                        MontantPaye  = acompte,
                        ModePaiement = modeChoisi
                    };
                    _paiementService.Ajouter(paiement, idOp, nomOp);
                    paiementCree = paiement;
                }

                FermerModal();

                // 6. Proposer reçu si acompte encaissé
                if (paiementCree != null)
                {
                    var commandeComplete = _commandeService.ObtenirParId(commande.IdCommande);
                    if (commandeComplete != null)
                    {
                        var imprimerRecu = MessageBox.Show(
                            $"Acompte de {acompte:N0} FCFA enregistré (reçu {paiementCree.RecuNumero}).\n\nImprimer le reçu ?",
                            "Reçu", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (imprimerRecu == MessageBoxResult.Yes)
                        {
                            var mesuresRecu = commandeComplete.Pieces.SelectMany(p => p.Mesures).ToList();
                            var fenRecu = new FenetreRecu(commandeComplete, paiementCree, mesuresRecu,
                                nomOp, _receiptService);
                            fenRecu.Owner = Window.GetWindow(this);
                            fenRecu.ShowDialog();
                        }
                    }
                }

                // 7. Proposer fiche atelier
                var imprimerFiche = MessageBox.Show(
                    "Commande créée avec succès !\n\nImprimer la fiche atelier ?",
                    "Fiche atelier", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (imprimerFiche == MessageBoxResult.Yes)
                {
                    var commandeComplete2 = _commandeService.ObtenirParId(commande.IdCommande);
                    if (commandeComplete2 != null)
                    {
                        var fiche = FicheAtelierWindow.Creer(commandeComplete2);
                        fiche.Owner = Window.GetWindow(this);
                        fiche.ShowDialog();
                    }
                }
            }
            catch (DoublonCommandeException ex)
            {
                MessageBox.Show(ex.Message, "Double envoi détecté", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Création impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                BtnValiderNouvelleCommande.IsEnabled = true;
            }
        }

        // ── ÉDITION ───────────────────────────────────────────────────
        private async Task ValiderEditionCommande()
        {
            if (_commandeEnEdition == null) return;

            if (CmbModalClient.SelectedValue == null)
            { MessageBox.Show("Sélectionnez un client.", "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (DpModalDateFin.SelectedDate != null && DpModalDateFin.SelectedDate < DateTime.Today)
            { MessageBox.Show("La date de RDV ne peut pas être dans le passé.", "Date invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var rep = MessageBox.Show("Enregistrer les modifications de la commande ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (rep != MessageBoxResult.Yes) return;

            BtnValiderNouvelleCommande.IsEnabled = false;
            try
            {
                var (idOp, nomOp) = OperateurConnecte();

                // Modifier les pièces existantes et créer les nouvelles
                foreach (var carte in _cartesPieces)
                {
                    var pieceObj = LireCartePiece(carte);
                    if (pieceObj == null) return;
                    var mesures = CollecterMesuresCarte(carte);

                    if (carte.IdPieceCommande.HasValue && carte.IdPieceCommande.Value > 0)
                    {
                        pieceObj.IdPieceCommande = carte.IdPieceCommande.Value;
                        if (!AvecControleLivraison(motif =>
                                _commandeService.ModifierPiece(pieceObj, mesures, idOp, nomOp, motif)))
                            return;
                    }
                    else
                    {
                        // Nouvelle pièce ajoutée en mode édition
                        _commandeService.AjouterPiece(_commandeEnEdition.IdCommande, pieceObj, mesures,
                            _roleUtilisateur == "Boss", null, new List<MaterielSupplement>(), idOp, nomOp);
                    }
                }

                // Modifier le niveau commande (client, dates)
                var commandeModif = new Commande
                {
                    IdCommande = _commandeEnEdition.IdCommande,
                    IdClient   = (int)(CmbModalClient.SelectedValue ?? _commandeEnEdition.IdClient),
                    DateFin    = DpModalDateFin.SelectedDate ?? _commandeEnEdition.DateFin,
                    HeureDebut = ParseHeure(TxtHeureDebut.Text) ?? _commandeEnEdition.HeureDebut,
                    HeureFin   = ParseHeure(TxtModalHeure.Text)
                };
                _commandeService.Modifier(commandeModif,
                    new PieceCommande { TypeVetement = string.Empty, MontantCouture = 0, Statut = string.Empty },
                    new List<Mesure>(), idOp, nomOp);

                FermerModal();
                await ChargerCommandes();
                MessageBox.Show("Commande modifiée avec succès.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Modification impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(ex.Message, "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                BtnValiderNouvelleCommande.IsEnabled = true;
            }
        }


        // ================================================================
        // FORCER STATUT (BOSS UNIQUEMENT, MODE ÉDITION)
        // ================================================================
        private void BtnForcerStatut_Click(object sender, RoutedEventArgs e)
        {
            int idCmd = _commandeEnEdition?.IdCommande ?? _commandeSelectionneeId;
            if (idCmd == 0) return;

            var dialog = new Window
            {
                Title  = "Forcer le statut de toutes les pièces",
                Width  = 350, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, Background = Brushes.White
            };
            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            panel.Children.Add(new TextBlock
            {
                Text = "Nouveau statut pour toutes les pièces :",
                FontSize = 13, Margin = new Thickness(0, 0, 0, 12)
            });
            var cmb = new ComboBox { Height = 36, FontSize = 13, Margin = new Thickness(0, 0, 0, 12) };
            cmb.Items.Add("A faire"); cmb.Items.Add("En cours");
            cmb.Items.Add("Terminee"); cmb.Items.Add("Livree");
            cmb.SelectedIndex = 2;
            panel.Children.Add(cmb);
            var msgErr = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), Height = 18, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(msgErr);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button { Content = "Appliquer", Width = 100, Height = 36, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0) };
            var btnAnn = new Button { Content = "Annuler", Width = 90, Height = 36, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Cursor = Cursors.Hand };

            btnOk.Click += (s, ev) =>
            {
                string statut = cmb.SelectedItem?.ToString() ?? "";
                try
                {
                    var (opId, opNom) = OperateurConnecte();
                    if (!AvecControleLivraison(motif =>
                            _commandeService.ForcerStatutToutesPieces(idCmd, statut, opId, opNom, motif)))
                        return;
                    dialog.DialogResult = true;
                    dialog.Close();
                    // Rafraîchir les statuts dans les cartes
                    var piecesRefresh = _commandeService.ObtenirPiecesCommande(idCmd);
                    for (int i = 0; i < _cartesPieces.Count && i < piecesRefresh.Count; i++)
                    {
                        var c = _cartesPieces[i];
                        for (int j = 0; j < c.CmbStatutPiece.Items.Count; j++)
                        {
                            if ((c.CmbStatutPiece.Items[j] as ComboBoxItem)?.Content?.ToString() == statut)
                            { c.CmbStatutPiece.SelectedIndex = j; break; }
                        }
                    }
                    _ = ChargerCommandes();
                    MessageBox.Show("Statut de toutes les pièces mis à jour.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (LivraisonNonSoldeeException ex) { MessageBox.Show(ex.Message, "Livraison impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
                catch (InvalidOperationException ex)   { msgErr.Text = ex.Message; }
                catch (UnauthorizedAccessException ex) { msgErr.Text = ex.Message; }
            };
            btnAnn.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnAnn);
            panel.Children.Add(btnRow);
            dialog.Content = panel;
            dialog.Owner   = Window.GetWindow(this);
            dialog.ShowDialog();
        }


        // ================================================================
        // SUPPRESSION D'UNE COMMANDE (factorisée)
        // ================================================================
        private async Task SupprimerCommandeAsync(int idCommande)
        {
            string? motif = DemanderMotifSuppression();
            if (string.IsNullOrWhiteSpace(motif)) return;

            var r = MessageBox.Show(
                $"Supprimer définitivement cette commande ?\n\nMotif : {motif}\n\nCette action est irréversible.",
                "Confirmation suppression", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                var op    = App.Services.GetRequiredService<IAuthService>().UtilisateurConnecte;
                int idOp  = op?.IdEmploye ?? 0;
                string nomOp = op != null ? $"{op.Prenom} {op.Nom}".Trim() : string.Empty;

                IAuditService? auditService = null;
                try { auditService = App.Services.GetService<IAuditService>(); } catch { }

                await _commandeService.SupprimerAsync(idCommande, idOp, nomOp, motif, auditService);
                await ChargerCommandes();
                if (_commandeSelectionneeId == idCommande)
                {
                    _commandeSelectionneeId = 0;
                    ViderChamps();
                }
                MessageBox.Show("Commande supprimée avec succès.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            { MessageBox.Show(ex.Message, "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (Exception ex)
            { MessageBox.Show("Erreur inattendue : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private string? DemanderMotifSuppression()
        {
            var dialog = new Window
            {
                Title = "Motif de suppression", Width = 440,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White, SizeToContent = SizeToContent.Height
            };
            var sp = new StackPanel { Margin = new Thickness(22, 20, 22, 20) };
            sp.Children.Add(new TextBlock { Text = "Motif de suppression * (10 caractères minimum)", FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
            var txMotif = new TextBox { Height = 72, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(txMotif);
            var erreur = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), FontSize = 12, Height = 18, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(erreur);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk  = new Button { Content = "Confirmer", Width = 110, Height = 36, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0) };
            var btnAnn = new Button { Content = "Annuler", Width = 90, Height = 36, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), Cursor = Cursors.Hand };
            btnOk.Click  += (s, ev) => { if (txMotif.Text.Trim().Length < 10) { erreur.Text = "Minimum 10 caractères."; return; } dialog.Tag = txMotif.Text.Trim(); dialog.DialogResult = true; dialog.Close(); };
            btnAnn.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            btns.Children.Add(btnOk); btns.Children.Add(btnAnn);
            sp.Children.Add(btns);
            dialog.Content = sp;
            return dialog.ShowDialog() == true ? (string)dialog.Tag : null;
        }


        // ================================================================
        // WHATSAPP — Anciens boutons (panneau droit legacy, conservés)
        // ================================================================
        private void MettreAJourBoutonsWhatsApp(Commande? cmd)
        {
            bool aCommande  = cmd != null;
            bool estTerminee = aCommande && cmd!.StatutGlobalAffiche == "Terminée";
            BtnWhatsAppPrete.Visibility = estTerminee ? Visibility.Visible : Visibility.Collapsed;
            BtnWhatsAppRdv.Visibility   = aCommande   ? Visibility.Visible : Visibility.Collapsed;
            BtnWhatsAppPied.Visibility  = aCommande   ? Visibility.Visible : Visibility.Collapsed;
            BtnFicheAtelier.Visibility  = aCommande   ? Visibility.Visible : Visibility.Collapsed;
            if (aCommande && TxtWhatsAppPiedLabel != null)
                TxtWhatsAppPiedLabel.Text = estTerminee ? "Prête !" : "RDV";
        }

        private Commande? ObtenirCommandeSelectionnee()
        {
            if (_commandeSelectionneeId == 0) return null;
            return _commandeService.ObtenirTous().FirstOrDefault(c => c.IdCommande == _commandeSelectionneeId);
        }

        private void BtnWhatsAppCommande_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Commande cmd) return;
            _ = EnvoyerWhatsAppApproprie(cmd);
        }

        private async void BtnWhatsAppPrete_Click(object sender, RoutedEventArgs e)
        {
            var cmd = ObtenirCommandeSelectionnee(); if (cmd == null) return;
            try { await _whatsApp.NotifierCommandePreteAsync(cmd); }
            catch (Exception ex) { MessageBox.Show("WhatsApp : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void BtnWhatsAppRdv_Click(object sender, RoutedEventArgs e)
        {
            var cmd = ObtenirCommandeSelectionnee(); if (cmd == null) return;
            try { await _whatsApp.NotifierRappelRdvAsync(cmd); }
            catch (Exception ex) { MessageBox.Show("WhatsApp : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void BtnWhatsAppPied_Click(object sender, RoutedEventArgs e)
        {
            var cmd = ObtenirCommandeSelectionnee(); if (cmd == null) return;
            try
            {
                if (cmd.StatutGlobalAffiche == "Terminée") await _whatsApp.NotifierCommandePreteAsync(cmd);
                else await _whatsApp.NotifierRappelRdvAsync(cmd);
            }
            catch (Exception ex) { MessageBox.Show("WhatsApp : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnFicheAtelier_Click(object sender, RoutedEventArgs e)
        {
            if (_commandeSelectionneeId == 0) return;
            try
            {
                var c = _commandeService.ObtenirParId(_commandeSelectionneeId);
                if (c == null) return;
                var f = FicheAtelierWindow.Creer(c);
                f.Owner = Window.GetWindow(this);
                f.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show("Erreur : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ================================================================
        // HELPERS LEGACY — conservés sans modification pour compatibilité
        // ================================================================
        private static (DateTime date, TimeSpan heure) CalculerRdvParDefaut()
        {
            var candidat = DateTime.Now.AddHours(24);
            var h        = candidat.TimeOfDay;
            if (h >= new TimeSpan(22, 0, 0)) return (candidat.Date.AddDays(1), new TimeSpan(8, 0, 0));
            if (h < new TimeSpan(8, 0, 0))  return (candidat.Date, new TimeSpan(8, 0, 0));
            int minutesArr = ((h.Minutes / 30) + 1) * 30;
            if (minutesArr >= 60) h = new TimeSpan(h.Hours + 1, 0, 0);
            else                  h = new TimeSpan(h.Hours, minutesArr, 0);
            return (candidat.Date, h);
        }

        private static TimeSpan? ParseHeure(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return null;
            if (TimeSpan.TryParse(texte, out var r)) return r;
            return null;
        }

        private string? DemanderMotif(string titre)
        {
            string? resultat = null;
            var dialog = new Window
            {
                Title = titre, Width = 420, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, Background = Brushes.White
            };
            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            panel.Children.Add(new TextBlock { Text = "Motif :", FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
            var txtMotif = new TextBox { Height = 70, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(txtMotif);
            var message = new TextBlock { FontSize = 12, Height = 18, Margin = new Thickness(0, 0, 0, 12),
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) };
            panel.Children.Add(message);
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button { Content = "Confirmer", Width = 110, Height = 36, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0) };
            var btnAnnuler = new Button { Content = "Annuler", Width = 100, Height = 36, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), Cursor = Cursors.Hand };
            btnOk.Click += (s, ev) => { if (string.IsNullOrWhiteSpace(txtMotif.Text)) { message.Text = "Motif obligatoire."; return; } resultat = txtMotif.Text.Trim(); dialog.DialogResult = true; dialog.Close(); };
            btnAnnuler.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            btnPanel.Children.Add(btnOk); btnPanel.Children.Add(btnAnnuler);
            panel.Children.Add(btnPanel);
            dialog.Content = panel;
            dialog.Owner   = Window.GetWindow(this);
            dialog.ShowDialog();
            return resultat;
        }

        private bool DemanderMotDePasse()
        {
            var dialog = new Window
            {
                Title = "Vérification d'identité", Width = 400, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, Background = Brushes.White
            };
            var mainPanel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            mainPanel.Children.Add(new TextBlock { Text = "Pour des raisons de sécurité,", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), Margin = new Thickness(0, 0, 0, 2) });
            mainPanel.Children.Add(new TextBlock { Text = "veuillez saisir votre mot de passe :", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), Margin = new Thickness(0, 0, 0, 14) });
            var passwordBox = new PasswordBox { Height = 38, FontSize = 14, Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            mainPanel.Children.Add(passwordBox);
            var message = new TextBlock { Text = "", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), Margin = new Thickness(0, 0, 0, 16), Height = 18 };
            mainPanel.Children.Add(message);
            mainPanel.Children.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), Margin = new Thickness(0, 0, 0, 14) });
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button { Content = "Confirmer", Width = 110, Height = 38, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand };
            var btnAnnuler = new Button { Content = "Annuler", Width = 100, Height = 38, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)), Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), Cursor = Cursors.Hand };
            btnOk.Click += (s, ev) =>
            {
                var authService = App.Services.GetRequiredService<IAuthService>();
                string mdp = passwordBox.Password.Trim();
                var user = authService.UtilisateurConnecte;
                bool mdpValide = user != null &&
                    (GestionCoutureApp.Helpers.PasswordHasher.EstAncienFormatSha256(user.MotDePasse)
                        ? user.MotDePasse == GestionCoutureApp.Helpers.PasswordHasher.HasherAncienSha256(mdp)
                        : GestionCoutureApp.Helpers.PasswordHasher.Verifier(mdp, user.MotDePasse));
                if (mdpValide) { dialog.DialogResult = true; dialog.Close(); }
                else { message.Text = "Mot de passe incorrect !"; passwordBox.Clear(); passwordBox.Focus(); }
            };
            btnAnnuler.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            passwordBox.KeyDown += (s, ev) => { if (ev.Key == System.Windows.Input.Key.Enter) btnOk.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            btnPanel.Children.Add(btnOk); btnPanel.Children.Add(btnAnnuler);
            mainPanel.Children.Add(btnPanel);
            dialog.Content = mainPanel;
            dialog.Owner   = Window.GetWindow(this);
            return dialog.ShowDialog() == true;
        }


        // ================================================================
        // MÉTHODES LEGACY CONSERVÉES INTACTES
        // (panneau droit masqué, mais compilées pour compatibilité tests / builds)
        // ================================================================

        private void ViderChamps()
        {
            _commandeSelectionneeId = 0;
            _pieceSelectionneeId    = null;
            _piecesCommande         = new List<PieceCommande>();
            _materiauxTemporaires.Clear();
            CmbClient.SelectedIndex      = -1;
            CmbCouturier.SelectedIndex   = -1;
            CmbTypeVetement.SelectedIndex = -1;
            CmbDescription.Text          = "";
            CmbAjustement.SelectedIndex  = 0;
            TxtPrixBase.Text  = "Prix de base : -";
            TxtPrixTotal.Text = "Prix total : -";
            TxtMontant.Text   = "";
            var (dateRdv, heureRdv) = CalculerRdvParDefaut();
            _dateRdvDefaut       = dateRdv;
            DateFin.SelectedDate = dateRdv;
            TxtHeureFin.Text     = heureRdv.ToString(@"hh\:mm");
            CmbStatut.SelectedIndex = 0;
            _prixBaseActuel = 0;
            PanelMesuresDynamiques.Children.Clear();
            TxtIndicationMesures.Text = "Sélectionnez un type de vêtement";
            GridCommandes.SelectedItem = null;
            ListePieces.ItemsSource    = null;
            TxtTotalPieces.Text        = "0 FCFA";
            _cheminPhotoTemporaire     = string.Empty;
            ImgPhoto.Source            = null;
            TxtPhotoPlaceholder.Visibility = Visibility.Visible;
            BtnSupprimerPhoto.Visibility   = Visibility.Collapsed;
            MasquerFormulairePiece();
            AfficherFormulairePiece(true);
            MettreAJourBoutonsWhatsApp(null);
            BandeauResume.Visibility = Visibility.Collapsed;
        }

        private void RafraichirListePieces()
        {
            ListePieces.ItemsSource = null;
            ListePieces.ItemsSource = _piecesCommande;
            decimal total = _piecesCommande.Sum(p => p.MontantCouture);
            TxtTotalPieces.Text = total.ToString("N0") + " FCFA";
            RafraichirBandeauResume();
        }

        private void RafraichirBandeauResume()
        {
            if (_piecesCommande == null || _piecesCommande.Count == 0)
            { BandeauResume.Visibility = Visibility.Collapsed; return; }
            BandeauResume.Visibility = Visibility.Visible;
            int nb = _piecesCommande.Count;
            TxtResumeTotalPieces.Text = nb.ToString();
            int nbT = _piecesCommande.Count(p => p.Statut == "Terminee");
            int nbE = _piecesCommande.Count(p => p.Statut == "En cours");
            int nbA = _piecesCommande.Count(p => p.Statut == "A faire");
            int nbL = _piecesCommande.Count(p => p.Statut == "Livree");
            ChipTerminee.Visibility = nbT > 0 ? Visibility.Visible : Visibility.Collapsed; if (nbT > 0) TxtResumeTerminee.Text = $"{nbT} terminée{(nbT>1?"s":"")}";
            ChipEnCours.Visibility  = nbE > 0 ? Visibility.Visible : Visibility.Collapsed; if (nbE > 0) TxtResumeEnCours.Text  = $"{nbE} en cours";
            ChipAfaire.Visibility   = nbA > 0 ? Visibility.Visible : Visibility.Collapsed; if (nbA > 0) TxtResumeAfaire.Text   = $"{nbA} à faire";
            ChipLivree.Visibility   = nbL > 0 ? Visibility.Visible : Visibility.Collapsed; if (nbL > 0) TxtResumeLivree.Text   = $"{nbL} livrée{(nbL>1?"s":"")}";
            var couturiers = _piecesCommande.Where(p => p.Couturier != null).Select(p => p.Couturier!.Prenom?.Trim()).Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderBy(n => n).ToList();
            TxtResumeCouturiers.Text = couturiers.Count == 0 ? "Aucun couturier assigné" : couturiers.Count == 1 ? $"1 couturier : {couturiers[0]}" : $"{couturiers.Count} couturiers : {string.Join(" · ", couturiers)}";
        }

        private void AfficherFormulairePiece(bool modeCreation)
        {
            LblDetailPiece.Visibility = Visibility.Visible;
            LblDetailPiece.Text = modeCreation ? "Nouvelle pièce" : "Modifier la pièce";
            SepDetailPiece.Visibility = Visibility.Visible;
            LblTypeVetement.Visibility = Visibility.Visible;
            CmbTypeVetement.Visibility = Visibility.Visible;
            LblCouturier.Visibility = Visibility.Visible;
            CmbCouturier.Visibility = Visibility.Visible;
            PanelPrix.Visibility = Visibility.Visible;
            LblDescription.Visibility = Visibility.Visible;
            CmbDescription.Visibility = Visibility.Visible;
            LblMontant.Visibility = Visibility.Visible;
            TxtMontant.Visibility = Visibility.Visible;
            LblStatut.Visibility = Visibility.Visible;
            CmbStatut.Visibility = Visibility.Visible;
            TxtIndicationMesures.Visibility = Visibility.Visible;
            SepMesures.Visibility = Visibility.Visible;
            LblPhoto.Visibility = Visibility.Visible;
            PanelBoutonsPhoto.Visibility = Visibility.Visible;
            PanelPhoto.Visibility = Visibility.Visible;
            SepPhoto.Visibility = Visibility.Visible;
            PanelMateriaux.Visibility = Visibility.Visible;
            PanelAjoutMateriau.Visibility = Visibility.Collapsed;
            SepAvantFormulaireMat.Visibility = Visibility.Collapsed;
            if (modeCreation)
            {
                BtnAjouterMateriau.Visibility = Visibility.Visible;
                _materiauxTemporaires.Clear();
                ListeMateriaux.ItemsSource = null;
                PanelListeMateriaux.Visibility = Visibility.Collapsed;
                TxtAucunMateriau.Text = "Aucun matériau ajouté — facultatif.";
                TxtAucunMateriau.Visibility = Visibility.Visible;
                TxtTotalMateriaux.Text = "0 FCFA";
            }
            else { BtnAjouterMateriau.Visibility = Visibility.Visible; }
            bool prixVerrouille = !modeCreation && _roleUtilisateur == "Secretaire";
            TxtMontant.IsReadOnly = prixVerrouille;
            CmbAjustement.IsEnabled = !prixVerrouille;
            CmbTypeVetement.IsEnabled = !prixVerrouille;
            TxtMontant.ToolTip = prixVerrouille ? "Seul le Boss peut modifier le prix d'une pièce enregistrée." : null;
            CmbClient.IsEnabled = _roleUtilisateur != "Secretaire" || modeCreation;
            BtnSauvegarderPiece.Visibility = Visibility.Visible;
            PanelActionsPiece.Visibility = modeCreation ? Visibility.Collapsed : Visibility.Visible;
            LblReutilisationMesures.Visibility = modeCreation ? Visibility.Visible : Visibility.Collapsed;
            CmbMesuresAnterieures.Visibility   = modeCreation ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MasquerFormulairePiece()
        {
            LblDetailPiece.Visibility = Visibility.Collapsed; SepDetailPiece.Visibility = Visibility.Collapsed;
            LblTypeVetement.Visibility = Visibility.Collapsed; CmbTypeVetement.Visibility = Visibility.Collapsed;
            LblCouturier.Visibility = Visibility.Collapsed; CmbCouturier.Visibility = Visibility.Collapsed;
            PanelPrix.Visibility = Visibility.Collapsed;
            LblDescription.Visibility = Visibility.Collapsed; CmbDescription.Visibility = Visibility.Collapsed;
            LblMontant.Visibility = Visibility.Collapsed; TxtMontant.Visibility = Visibility.Collapsed;
            LblStatut.Visibility = Visibility.Collapsed; CmbStatut.Visibility = Visibility.Collapsed;
            TxtIndicationMesures.Visibility = Visibility.Collapsed; SepMesures.Visibility = Visibility.Collapsed;
            LblPhoto.Visibility = Visibility.Collapsed; PanelBoutonsPhoto.Visibility = Visibility.Collapsed;
            PanelPhoto.Visibility = Visibility.Collapsed; SepPhoto.Visibility = Visibility.Collapsed;
            PanelMateriaux.Visibility = Visibility.Collapsed;
            PanelAjoutMateriau.Visibility = Visibility.Collapsed;
            ListeMateriaux.ItemsSource = null;
            PanelActionsPiece.Visibility = Visibility.Collapsed;
            LblReutilisationMesures.Visibility = Visibility.Collapsed; CmbMesuresAnterieures.Visibility = Visibility.Collapsed;
            PanelMesuresDynamiques.Children.Clear();
        }

        private void RafraichirEnTeteCommande(Commande commande)
        {
            bool ancienFlag = _chargementEnCours; _chargementEnCours = true;
            try
            {
                if (CmbClient.SelectedValue == null || (int)CmbClient.SelectedValue != commande.IdClient)
                    CmbClient.SelectedValue = commande.IdClient;
                TxtHeureDebut.Text   = commande.HeureDebut.ToString(@"hh\:mm");
                TxtHeureFin.Text     = commande.HeureFin?.ToString(@"hh\:mm") ?? string.Empty;
                DateFin.SelectedDate = commande.DateFin;
            }
            finally { _chargementEnCours = ancienFlag; }
        }

        private void RafraichirFormulairePiece(PieceCommande pieceFraiche)
        {
            bool ancienFlag = _chargementEnCours; _chargementEnCours = true;
            try
            {
                if (pieceFraiche.IdCouturier.HasValue) CmbCouturier.SelectedValue = pieceFraiche.IdCouturier.Value;
                else CmbCouturier.SelectedIndex = -1;
                bool trouve = false;
                for (int i = 0; i < CmbStatut.Items.Count; i++)
                {
                    if (CmbStatut.Items[i] is ComboBoxItem item && item.Content?.ToString() == pieceFraiche.Statut)
                    { CmbStatut.SelectedIndex = i; trouve = true; break; }
                }
                if (!trouve) CmbStatut.Text = pieceFraiche.Statut;
                TxtMontant.Text = pieceFraiche.MontantCouture.ToString();
            }
            finally { _chargementEnCours = ancienFlag; }
        }


        // ================================================================
        // MÉTHODES LEGACY — PIÈCES / MATÉRIAUX (identiques à l'original)
        // ================================================================
        private void PieceItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not PieceCommande piece) return;
            _pieceSelectionneeId = piece.IdPieceCommande;
            _chargementEnCours   = true;
            try
            {
                AfficherFormulairePiece(false);
                var typeMatch = _typesVetement.FirstOrDefault(t => t.Nom == piece.TypeVetement);
                if (typeMatch != null) CmbTypeVetement.SelectedValue = typeMatch.IdTypeVetement;
                TxtMontant.Text = piece.MontantCouture.ToString();
                if (piece.IdCouturier.HasValue) CmbCouturier.SelectedValue = piece.IdCouturier.Value;
                else CmbCouturier.SelectedIndex = -1;
                if (CmbDescription.ItemsSource != null)
                {
                    var descMatch = CmbDescription.Items.Cast<DescriptionCourante>().FirstOrDefault(d => d.Texte == piece.DescriptionPrecision);
                    if (descMatch != null) CmbDescription.SelectedItem = descMatch;
                    else CmbDescription.Text = piece.DescriptionPrecision ?? "";
                }
                else CmbDescription.Text = piece.DescriptionPrecision ?? "";
                for (int i = 0; i < CmbStatut.Items.Count; i++)
                {
                    var item = (ComboBoxItem)CmbStatut.Items[i];
                    if (item.Content.ToString() == piece.Statut) { CmbStatut.SelectedIndex = i; break; }
                }
                if (CmbStatut.SelectedItem == null && !string.IsNullOrWhiteSpace(piece.Statut))
                    CmbStatut.Text = piece.Statut;
                var mesuresExistantes = _commandeService.ObtenirMesuresPiece(piece.IdPieceCommande);
                foreach (var child in PanelMesuresDynamiques.Children)
                {
                    var row = (StackPanel)child;
                    var combo = (ComboBox)row.Children[1];
                    string nomMesure = combo.Tag?.ToString() ?? "";
                    var mesure = mesuresExistantes.FirstOrDefault(m => m.NomMesure == nomMesure);
                    if (mesure != null)
                    {
                        bool trouve = false;
                        for (int j = 0; j < combo.Items.Count; j++)
                        {
                            string? it = combo.Items[j]?.ToString();
                            if (it == mesure.Valeur + " cm" || it?.StartsWith(mesure.Valeur + " ") == true)
                            { combo.SelectedIndex = j; trouve = true; break; }
                        }
                        if (!trouve) combo.Text = mesure.Valeur + " cm";
                    }
                }
                _cheminPhotoTemporaire = piece.CheminPhoto ?? string.Empty;
                if (!string.IsNullOrEmpty(_cheminPhotoTemporaire) && System.IO.File.Exists(_cheminPhotoTemporaire))
                {
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(_cheminPhotoTemporaire); image.EndInit(); image.Freeze();
                    ImgPhoto.Source = image; TxtPhotoPlaceholder.Visibility = Visibility.Collapsed; BtnSupprimerPhoto.Visibility = Visibility.Visible;
                }
                else { ImgPhoto.Source = null; _cheminPhotoTemporaire = string.Empty; TxtPhotoPlaceholder.Visibility = Visibility.Visible; BtnSupprimerPhoto.Visibility = Visibility.Collapsed; }
                if (typeMatch != null)
                {
                    decimal ecart = piece.MontantCouture - typeMatch.PrixBase;
                    int idx = (int)Math.Round(ecart / 500m);
                    CmbAjustement.SelectedIndex = (idx >= 0 && idx < CmbAjustement.Items.Count) ? idx : 0;
                }
                BtnSupprimerPiece.Visibility  = _roleUtilisateur == "Boss" ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { _chargementEnCours = false; }
            RafraichirMateriaux();
        }

        private void CmbTypeVetement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_typesVetement == null || _typesVetement.Count == 0) return;
                if (CmbTypeVetement.SelectedValue == null) return;
                if (!int.TryParse(CmbTypeVetement.SelectedValue.ToString(), out int id)) return;
                var type = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == id);
                if (type == null) return;
                _prixBaseActuel = type.PrixBase;
                TxtPrixBase.Text = "Prix de base : " + type.PrixBase + " FCFA";
                if (!_chargementEnCours) TxtMontant.Text = type.PrixBase.ToString();
                if (type.Descriptions != null && type.Descriptions.Any()) CmbDescription.ItemsSource = type.Descriptions.ToList();
                else CmbDescription.ItemsSource = new List<DescriptionCourante>();
                if (!_chargementEnCours) CmbDescription.Text = string.Empty;
                PanelMesuresDynamiques.Children.Clear();
                if (type.MesuresRequises != null && type.MesuresRequises.Any())
                {
                    TxtIndicationMesures.Text = type.MesuresRequises.Count + " mesure(s) requise(s) :";
                    foreach (var mesure in type.MesuresRequises)
                    {
                        if (string.IsNullOrWhiteSpace(mesure.NomMesure)) continue;
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                        row.Children.Add(new TextBlock { Text = mesure.NomMesure, Width = 160, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                        var combo = new ComboBox { Width = 80, FontSize = 12, Tag = mesure.NomMesure, IsEditable = true };
                        for (int i = 20; i <= 300; i++) combo.Items.Add((i * 0.5).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " cm");
                        combo.SelectedIndex = 0;
                        row.Children.Add(combo);
                        PanelMesuresDynamiques.Children.Add(row);
                    }
                }
                else TxtIndicationMesures.Text = "Aucune mesure requise pour ce type";
                if (!_chargementEnCours) CalculerPrixTotal();
                try { ChargerMesuresAnterieures(); } catch { }
            }
            catch (Exception ex) { MessageBox.Show($"Erreur chargement type vêtement :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void CmbAjustement_SelectionChanged(object sender, SelectionChangedEventArgs e) => CalculerPrixTotal();

        private void CalculerPrixTotal()
        {
            if (CmbAjustement.SelectedItem == null) return;
            decimal ajustement = (int)CmbAjustement.SelectedItem;
            decimal total = _prixBaseActuel + ajustement;
            TxtPrixTotal.Text = "Prix total : " + total + " FCFA";
            TxtMontant.Text = total.ToString();
        }

        private void ChargerMesuresAnterieures()
        {
            try
            {
                CmbMesuresAnterieures.ItemsSource = null; CmbMesuresAnterieures.SelectedIndex = -1;
                if (CmbClient.SelectedValue == null || CmbTypeVetement.SelectedValue == null) { LblReutilisationMesures.Text = "Reprendre les mesures — choisissez d'abord un type de vêtement"; return; }
                if (!int.TryParse(CmbClient.SelectedValue.ToString(), out int idClient)) return;
                if (!int.TryParse(CmbTypeVetement.SelectedValue.ToString(), out int idType)) return;
                var type = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idType);
                if (type == null) return;
                var piecesAnt = _commandeService.ObtenirPiecesAnterieuresClient(idClient, type.Nom, null);
                LblReutilisationMesures.Text = piecesAnt.Count == 0
                    ? $"Aucune pièce {type.Nom} enregistrée pour ce client"
                    : $"Reprendre les mesures d'une pièce {type.Nom} existante ({piecesAnt.Count} trouvée(s))";
                CmbMesuresAnterieures.ItemsSource = piecesAnt;
            }
            catch { LblReutilisationMesures.Text = "Erreur lors du chargement des mesures antérieures"; }
        }

        private void CmbMesuresAnterieures_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_chargementEnCours) return;
            if (CmbMesuresAnterieures.SelectedItem is not PieceCommande pieceAnt) return;
            var mesures = _commandeService.ObtenirMesuresPiece(pieceAnt.IdPieceCommande);
            foreach (var child in PanelMesuresDynamiques.Children)
            {
                var row = (StackPanel)child; var combo = (ComboBox)row.Children[1];
                string nomMesure = combo.Tag?.ToString() ?? "";
                var mesure = mesures.FirstOrDefault(m => m.NomMesure == nomMesure);
                if (mesure != null)
                {
                    bool trouve = false;
                    for (int j = 0; j < combo.Items.Count; j++) { string? it = combo.Items[j]?.ToString(); if (it == mesure.Valeur + " cm" || it?.StartsWith(mesure.Valeur+" ") == true) { combo.SelectedIndex = j; trouve = true; break; } }
                    if (!trouve) combo.Text = mesure.Valeur + " cm";
                }
            }
        }

        private List<Mesure> CollecterMesures()
        {
            var mesures = new List<Mesure>();
            foreach (var child in PanelMesuresDynamiques.Children)
            {
                var row = (StackPanel)child; var combo = (ComboBox)row.Children[1];
                string texte = combo.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(texte))
                {
                    string valeur = texte.Replace(" cm","").Replace("cm","").Trim();
                    mesures.Add(new Mesure { NomMesure = combo.Tag?.ToString() ?? "", Valeur = valeur });
                }
            }
            return mesures;
        }

        private PieceCommande? LirePieceDuFormulaire()
        {
            if (CmbTypeVetement.SelectedValue == null || !int.TryParse(CmbTypeVetement.SelectedValue.ToString(), out int idTypeVetement))
            { MessageBox.Show("Sélectionnez un type de vêtement.", "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
            var typeVetement = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idTypeVetement);
            if (typeVetement == null) { MessageBox.Show("Type de vêtement introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); return null; }
            if (!decimal.TryParse(TxtMontant.Text, out decimal montant) || montant <= 0)
            { MessageBox.Show("Le montant doit être positif.", "Montant invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
            return new PieceCommande
            {
                TypeVetement = typeVetement.Nom,
                DescriptionPrecision = CmbDescription.SelectedItem is DescriptionCourante dc ? dc.Texte : CmbDescription.Text,
                IdCouturier = CmbCouturier.SelectedValue as int?,
                MontantCouture = montant,
                Statut = (CmbStatut.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? CmbStatut.Text?.Trim() ?? "A faire",
                CheminPhoto = _cheminPhotoTemporaire
            };
        }


        // ── Matériaux legacy ──────────────────────────────────────────────
        private void RafraichirMateriaux()
        {
            if (!_pieceSelectionneeId.HasValue) return;
            var materiaux = _materielService.ObtenirParPiece(_pieceSelectionneeId.Value);
            ListeMateriaux.ItemsSource = null; ListeMateriaux.ItemsSource = materiaux;
            bool aDesMateriaux = materiaux.Count > 0;
            PanelListeMateriaux.Visibility = aDesMateriaux ? Visibility.Visible : Visibility.Collapsed;
            TxtAucunMateriau.Text = "Aucun matériau ajouté pour cette pièce.";
            TxtAucunMateriau.Visibility = aDesMateriaux ? Visibility.Collapsed : Visibility.Visible;
            decimal totalMat = materiaux.Sum(m => m.Quantite * m.PrixUnitaire);
            TxtTotalMateriaux.Text = totalMat > 0 ? $"{totalMat:N0} FCFA" : "0 FCFA";
        }

        private void BtnAnnulerFormulaireMateriau_Click(object sender, RoutedEventArgs e)
        {
            PanelAjoutMateriau.Visibility = Visibility.Collapsed; SepAvantFormulaireMat.Visibility = Visibility.Collapsed;
            TxtMatDesignation.Text = string.Empty; TxtMatQuantite.Text = "1"; TxtMatPrix.Text = string.Empty;
        }

        private void BtnAjouterMateriau_Click(object sender, RoutedEventArgs e)
        {
            bool visible = PanelAjoutMateriau.Visibility == Visibility.Visible;
            PanelAjoutMateriau.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            SepAvantFormulaireMat.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            if (!visible) { TxtMatDesignation.Text = string.Empty; TxtMatQuantite.Text = "1"; TxtMatPrix.Text = string.Empty; TxtMatDesignation.Focus(); }
        }

        private void BtnConfirmerMateriau_Click(object sender, RoutedEventArgs e)
        {
            string designation = TxtMatDesignation.Text.Trim();
            if (string.IsNullOrEmpty(designation)) { MessageBox.Show("La désignation est obligatoire.", "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning); TxtMatDesignation.Focus(); return; }
            if (!Helpers.ValidationHelper.EstTexteSecurise(designation, out string erreur)) { MessageBox.Show($"Désignation invalide : {erreur}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!int.TryParse(TxtMatQuantite.Text.Trim(), out int quantite) || quantite <= 0) { MessageBox.Show("La quantité doit être un entier positif.", "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!decimal.TryParse(TxtMatPrix.Text.Trim().Replace(" ",""), out decimal prix) || prix < 0) { MessageBox.Show("Le prix unitaire doit être positif.", "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var materiau = new MaterielSupplement { Designation = designation, Quantite = quantite, PrixUnitaire = prix };
            TxtMatDesignation.Text = string.Empty; TxtMatQuantite.Text = "1"; TxtMatPrix.Text = string.Empty;
            PanelAjoutMateriau.Visibility = Visibility.Collapsed; SepAvantFormulaireMat.Visibility = Visibility.Collapsed;
            if (_pieceSelectionneeId.HasValue)
            {
                try { var (opId, opNom) = OperateurConnecte(); materiau.IdCommande = _commandeSelectionneeId; materiau.IdPieceCommande = _pieceSelectionneeId.Value; _materielService.Ajouter(materiau, opId, opNom); RafraichirMateriaux(); RafraichirTotalAvecMateriaux(); }
                catch (Exception ex) { MessageBox.Show("Erreur : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            else { _materiauxTemporaires.Add(materiau); RafraichirListeMatériauxTemporaires(); }
        }

        private void BtnSupprimerMateriau_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not int idMateriel) return;
            if (MessageBox.Show("Supprimer ce matériau ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (idMateriel < 0) { int idx = -(idMateriel + 1); if (idx >= 0 && idx < _materiauxTemporaires.Count) { _materiauxTemporaires.RemoveAt(idx); RafraichirListeMatériauxTemporaires(); } return; }
            try { _materielService.Supprimer(idMateriel); RafraichirMateriaux(); RafraichirTotalAvecMateriaux(); }
            catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void RafraichirListeMatériauxTemporaires()
        {
            var items = _materiauxTemporaires.Select((m, i) => new MaterielSupplement { IdMateriel = -(i+1), Designation = m.Designation, Quantite = m.Quantite, PrixUnitaire = m.PrixUnitaire }).ToList();
            ListeMateriaux.ItemsSource = null; ListeMateriaux.ItemsSource = items;
            bool a = _materiauxTemporaires.Count > 0;
            PanelListeMateriaux.Visibility = a ? Visibility.Visible : Visibility.Collapsed;
            TxtAucunMateriau.Text = "Aucun matériau ajouté — facultatif."; TxtAucunMateriau.Visibility = a ? Visibility.Collapsed : Visibility.Visible;
            decimal t = _materiauxTemporaires.Sum(m => m.Quantite * m.PrixUnitaire);
            TxtTotalMateriaux.Text = t > 0 ? $"{t:N0} FCFA" : "0 FCFA";
        }

        private void RafraichirTotalAvecMateriaux()
        {
            decimal totalCouture   = _piecesCommande.Sum(p => p.MontantCouture);
            decimal totalMateriaux = _commandeSelectionneeId > 0 ? _materielService.TotalParCommande(_commandeSelectionneeId) : 0m;
            decimal total = totalCouture + totalMateriaux;
            TxtTotalPieces.Text = totalMateriaux > 0 ? $"{total:N0} FCFA  (dont {totalMateriaux:N0} mat.)" : $"{total:N0} FCFA";
        }

        // ── Duplication / suppression de pièce (panneau droit legacy) ─────
        private void BtnDupliquerPiece_Click(object sender, RoutedEventArgs e)
        {
            if (!_pieceSelectionneeId.HasValue) return;
            if (MessageBox.Show("Dupliquer cette pièce ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                _commandeService.DupliquerPiece(_pieceSelectionneeId.Value);
                _piecesCommande = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                RafraichirListePieces(); _ = ChargerCommandes();
                MessageBox.Show("Pièce dupliquée avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "Duplication impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void BtnEditerPiece_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PieceCommande piece)
            {
                _pieceSelectionneeId = piece.IdPieceCommande;
                PieceItem_MouseLeftButtonUp(btn, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            }
        }

        private void BtnSupprimerPieceLigne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PieceCommande piece)
            { _pieceSelectionneeId = piece.IdPieceCommande; BtnSupprimerPiece_Click(sender, e); }
        }

        private void BtnSupprimerPiece_Click(object sender, RoutedEventArgs e)
        {
            if (!_pieceSelectionneeId.HasValue) return;
            if (MessageBox.Show("Supprimer cette pièce ?\n\nCette action est irréversible.", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                var (opId, opNom) = OperateurConnecte();
                _commandeService.SupprimerPiece(_pieceSelectionneeId.Value, opId, opNom);
                _piecesCommande = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                RafraichirListePieces(); _ = ChargerCommandes(); MasquerFormulairePiece(); _pieceSelectionneeId = null;
                MessageBox.Show("Pièce supprimée.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (UnauthorizedAccessException ex) { MessageBox.Show(ex.Message, "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }


        // ── Bouton sauvegarder pièce (panneau droit legacy) ───────────────
        private async void BtnSauvegarderPiece_Click(object sender, RoutedEventArgs e)
        {
            if (CmbTypeVetement.SelectedValue == null) { MessageBox.Show("Sélectionnez un type de vêtement.", "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!decimal.TryParse(TxtMontant.Text, out decimal montant) || montant <= 0) { MessageBox.Show("Le montant doit être positif.", "Montant invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (_roleUtilisateur == "Secretaire" && _pieceSelectionneeId == null)
            { var rep = MessageBox.Show("Attention : enregistrement irréversible.\n\nConfirmer ?", "Confirmation", MessageBoxButton.OKCancel, MessageBoxImage.Information); if (rep != MessageBoxResult.OK) return; }
            if (!int.TryParse(CmbTypeVetement.SelectedValue.ToString(), out int idTypeVetement)) return;
            var typeVetement = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == idTypeVetement);
            if (typeVetement == null) return;
            var piece = new PieceCommande
            {
                TypeVetement = typeVetement.Nom,
                DescriptionPrecision = CmbDescription.SelectedItem is DescriptionCourante dc ? dc.Texte : CmbDescription.Text,
                IdCouturier = CmbCouturier.SelectedValue as int?,
                MontantCouture = montant,
                Statut = (CmbStatut.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? CmbStatut.Text?.Trim() ?? "A faire",
                CheminPhoto = _cheminPhotoTemporaire
            };
            var mesures = CollecterMesures();
            try
            {
                if (_pieceSelectionneeId.HasValue)
                {
                    piece.IdPieceCommande = _pieceSelectionneeId.Value;
                    var (idOpM, nomOpM) = OperateurConnecte();
                    if (!AvecControleLivraison(motif => _commandeService.ModifierPiece(piece, mesures, idOpM, nomOpM, motif))) return;
                    var commandeAct = _commandeService.ObtenirParId(_commandeSelectionneeId);
                    if (commandeAct != null) RafraichirEnTeteCommande(commandeAct);
                    var pieceFr = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId).FirstOrDefault(p => p.IdPieceCommande == _pieceSelectionneeId.Value);
                    if (pieceFr != null) RafraichirFormulairePiece(pieceFr);
                    MessageBox.Show("Pièce modifiée avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (_commandeSelectionneeId > 0)
                {
                    var (opId, opNom) = OperateurConnecte();
                    _commandeService.AjouterPiece(_commandeSelectionneeId, piece, mesures, _roleUtilisateur == "Boss", _motifExceptionAjoutPiece, _materiauxTemporaires.ToList(), opId, opNom);
                    _motifExceptionAjoutPiece = null; _materiauxTemporaires.Clear();
                    MessageBox.Show("Pièce ajoutée avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                _piecesCommande = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                RafraichirListePieces();
                await ChargerCommandes();
                if (!_pieceSelectionneeId.HasValue) MasquerFormulairePiece();
            }
            catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "Opération impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (UnauthorizedAccessException ex) { MessageBox.Show(ex.Message, "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void BtnAjouterPiece_Click(object sender, RoutedEventArgs e)
        {
            _motifExceptionAjoutPiece = null;
            if (_commandeSelectionneeId == 0) { MessageBox.Show("Sélectionnez d'abord une commande.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!_commandeService.PeutAjouterPiece(_commandeSelectionneeId))
            {
                if (_roleUtilisateur == "Boss")
                {
                    if (MessageBox.Show("Un acompte a déjà été encaissé.\n\nEn tant que Boss, vous pouvez ajouter une pièce avec motif.\n\nContinuer ?", "Ajout avec exception", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    string? motifSaisi = DemanderMotif("Motif de l'ajout après encaissement");
                    if (string.IsNullOrWhiteSpace(motifSaisi)) return;
                    _motifExceptionAjoutPiece = motifSaisi;
                }
                else { MessageBox.Show("Impossible d'ajouter une pièce : un acompte a déjà été encaissé.\nSeul le Boss peut ajouter dans ce cas.", "Ajout impossible", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            }
            _pieceSelectionneeId = null; _materiauxTemporaires.Clear();
            CmbTypeVetement.SelectedIndex = -1; CmbCouturier.SelectedIndex = -1;
            CmbDescription.Text = ""; CmbAjustement.SelectedIndex = 0;
            TxtPrixBase.Text = "Prix de base : -"; TxtPrixTotal.Text = "Prix total : -";
            TxtMontant.Text = ""; CmbStatut.SelectedIndex = 0; _prixBaseActuel = 0;
            PanelMesuresDynamiques.Children.Clear(); TxtIndicationMesures.Text = "Sélectionnez un type de vêtement";
            _cheminPhotoTemporaire = string.Empty; ImgPhoto.Source = null;
            TxtPhotoPlaceholder.Visibility = Visibility.Visible; BtnSupprimerPhoto.Visibility = Visibility.Collapsed;
            CmbMesuresAnterieures.ItemsSource = null;
            AfficherFormulairePiece(true);
        }

        // ── Boutons CRUD commande legacy ──────────────────────────────────
        private void BtnCreer_Click(object sender, RoutedEventArgs e)
        {
            // Redirige vers la modale pour la création
            OuvrirModalCreation();
        }

        private async void BtnModifier_Click(object sender, RoutedEventArgs e)
        {
            if (_commandeSelectionneeId == 0) { MessageBox.Show("Sélectionnez une commande.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (CmbClient.SelectedValue == null) { MessageBox.Show("Sélectionnez un client.", "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (DateFin.SelectedDate != null && DateFin.SelectedDate < DateTime.Today) { MessageBox.Show("Date de RDV invalide.", "Date invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            PieceCommande? pieceModifiee = null;
            if (_pieceSelectionneeId.HasValue && CmbTypeVetement.Visibility == Visibility.Visible)
            {
                pieceModifiee = LirePieceDuFormulaire();
                if (pieceModifiee == null) return;
                pieceModifiee.IdPieceCommande = _pieceSelectionneeId.Value;
            }
            if (MessageBox.Show("Modifier la commande ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                if (pieceModifiee != null)
                {
                    var (idOp, nomOp) = OperateurConnecte();
                    if (!AvecControleLivraison(motif => _commandeService.ModifierPiece(pieceModifiee, CollecterMesures(), idOp, nomOp, motif))) return;
                }
                var commandePourModif = new Commande { IdCommande = _commandeSelectionneeId, IdClient = (int)(CmbClient.SelectedValue ?? 0), DateFin = DateFin.SelectedDate ?? DateTime.Today, HeureDebut = ParseHeure(TxtHeureDebut.Text) ?? TimeSpan.Zero, HeureFin = ParseHeure(TxtHeureFin.Text) };
                var piecePourModif = pieceModifiee ?? new PieceCommande { TypeVetement = string.Empty, MontantCouture = 0, Statut = string.Empty };
                var (idOpM, nomOpM) = OperateurConnecte();
                _commandeService.Modifier(commandePourModif, piecePourModif, pieceModifiee != null ? CollecterMesures() : new List<Mesure>(), idOpM, nomOpM);
                if (pieceModifiee != null)
                {
                    _piecesCommande = _commandeService.ObtenirPiecesCommande(_commandeSelectionneeId);
                    RafraichirListePieces();
                    var pieceFr = _piecesCommande.FirstOrDefault(p => p.IdPieceCommande == pieceModifiee.IdPieceCommande);
                    if (pieceFr != null) RafraichirFormulairePiece(pieceFr);
                }
                await ChargerCommandes();
                MessageBox.Show("Commande modifiée avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "Modification impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (UnauthorizedAccessException ex) { MessageBox.Show(ex.Message, "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private async void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (_commandeSelectionneeId == 0) { MessageBox.Show("Sélectionnez une commande.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            await SupprimerCommandeAsync(_commandeSelectionneeId);
        }

        private void BtnVider_Click(object sender, RoutedEventArgs e) => ViderChamps();


        // ── Photo legacy ──────────────────────────────────────────────────
        private async void BtnImporterPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Sélectionner une photo", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var info = new System.IO.FileInfo(dialog.FileName);
                string ext = info.Extension.ToLowerInvariant();
                if (!ExtensionsAutorisees.Contains(ext)) { MessageBox.Show($"Format non autorisé : {ext}", "Fichier invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (info.Length > TailleMaxOctets) { MessageBox.Show("Image trop volumineuse (max 1 Mo).", "Fichier trop grand", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (!EstImageValide(dialog.FileName)) { MessageBox.Show("Le fichier n'est pas une image valide.", "Invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                string dossier = GestionCoutureApp.Helpers.AppPaths.DossierPhotos;
                string suffixe = Guid.NewGuid().ToString("N")[..8];
                string nomFich = $"photo_{DateTime.Now:yyyyMMdd_HHmmss}_{suffixe}{ext}";
                string dest    = System.IO.Path.Combine(dossier, nomFich);
                System.IO.File.Copy(dialog.FileName, dest, overwrite: true);
                LoadingIndicator.Visibility = Visibility.Visible;
                await Task.Run(() => GestionCoutureApp.Helpers.PhotoCompressor.Compresser(dest, dest));
                LoadingIndicator.Visibility = Visibility.Collapsed;
                _cheminPhotoTemporaire = dest;
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(dest); image.EndInit(); image.Freeze();
                ImgPhoto.Source = image; TxtPhotoPlaceholder.Visibility = Visibility.Collapsed; BtnSupprimerPhoto.Visibility = Visibility.Visible;
            }
            catch (OutOfMemoryException) { MessageBox.Show("Mémoire insuffisante pour traiter cette image.", "Erreur mémoire", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch (Exception ex) { MessageBox.Show("Erreur import : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnPrendrePhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var webcamWindow = new WebcamCaptureWindow();
                webcamWindow.Owner = Window.GetWindow(this);
                if (webcamWindow.ShowDialog() == true && !string.IsNullOrEmpty(webcamWindow.CapturedFilePath))
                {
                    _cheminPhotoTemporaire = webcamWindow.CapturedFilePath;
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(_cheminPhotoTemporaire); image.EndInit(); image.Freeze();
                    ImgPhoto.Source = image; TxtPhotoPlaceholder.Visibility = Visibility.Collapsed; BtnSupprimerPhoto.Visibility = Visibility.Visible;
                }
            }
            catch (OutOfMemoryException) { MessageBox.Show("Mémoire insuffisante.", "Erreur mémoire", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch (Exception ex) { MessageBox.Show("Erreur webcam : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnSupprimerPhoto_Click(object sender, RoutedEventArgs e)
        {
            _cheminPhotoTemporaire = string.Empty;
            ImgPhoto.Source = null;
            TxtPhotoPlaceholder.Visibility = Visibility.Visible;
            BtnSupprimerPhoto.Visibility   = Visibility.Collapsed;
        }

        // ── Récapitulatif legacy ──────────────────────────────────────────
        private bool AfficherRecapitulatif(string client, string typeVetement, string description, string couturier, decimal montant, DateTime dateRdv, List<Mesure> mesures, List<MaterielSupplement> materiaux, List<PieceCommande>? piecesExistantes = null)
        {
            var dialog = new Window { Title = "Récapitulatif — Confirmer la commande", Width = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Brushes.White, SizeToContent = SizeToContent.Height };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 620 };
            var root = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };
            root.Children.Add(new TextBlock { Text = "Récapitulatif de la commande", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)), Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new Border { Height = 3, Width = 48, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) });
            var blcCmd = CreerBlocRecap("📌  Commande"); AjouterLigneRecap(blcCmd, "Client", client); AjouterLigneRecap(blcCmd, "Date RDV", dateRdv.ToString("dd/MM/yyyy")); root.Children.Add(blcCmd);
            var blcCouture = CreerBlocRecap("🧵  Détail couture"); decimal totalCouture = 0;
            if (piecesExistantes != null) { foreach (var pe in piecesExistantes) { AjouterLigneRecap(blcCouture, pe.TypeVetement, $"{pe.MontantCouture:N0} FCFA"); totalCouture += pe.MontantCouture; } }
            AjouterLigneRecap(blcCouture, typeVetement, $"{montant:N0} FCFA"); totalCouture += montant;
            AjouterLigneRecap(blcCouture, "S/T Couture", $"{totalCouture:N0} FCFA", gras: true); root.Children.Add(blcCouture);
            if (mesures.Count > 0) { var blcMes = CreerBlocRecap("📐  Mesures"); foreach (var m in mesures) AjouterLigneRecap(blcMes, m.NomMesure, m.Valeur + " cm"); root.Children.Add(blcMes); }
            decimal totalMat = 0; var blcMat = CreerBlocRecap("📦  Matériaux"); bool aDesMat = false;
            if (piecesExistantes != null) { foreach (var pe in piecesExistantes) foreach (var mat in pe.MaterielSupplements) { AjouterLigneRecap(blcMat, mat.Designation, $"{mat.Quantite} × {mat.PrixUnitaire:N0} = {mat.Montant:N0} FCFA"); totalMat += mat.Montant; aDesMat = true; } }
            foreach (var mat in materiaux) { AjouterLigneRecap(blcMat, mat.Designation, $"{mat.Quantite} × {mat.PrixUnitaire:N0} = {mat.Montant:N0} FCFA"); totalMat += mat.Montant; aDesMat = true; }
            if (!aDesMat) ((StackPanel)blcMat.Child).Children.Add(new TextBlock { Text = "Aucun matériau — 0 FCFA", FontSize = 12, FontStyle = FontStyles.Italic, Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)) });
            root.Children.Add(blcMat);
            decimal totalGen = totalCouture + totalMat;
            var blcTotal = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 8, 0, 20) };
            var spTotal = new StackPanel(); var rowT = new Grid(); rowT.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); rowT.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lblT = new TextBlock { Text = "TOTAL GÉNÉRAL", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xA8, 0xA4)), VerticalAlignment = VerticalAlignment.Center };
            var valT = new TextBlock { Text = $"{totalGen:N0} FCFA", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lblT, 0); Grid.SetColumn(valT, 1); rowT.Children.Add(lblT); rowT.Children.Add(valT); spTotal.Children.Add(rowT); blcTotal.Child = spTotal; root.Children.Add(blcTotal);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnAnn  = new Button { Content = "✎  Corriger", Width = 110, Height = 38, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)), Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0) };
            var btnConf = new Button { Content = "✔  Confirmer", Width = 130, Height = 38, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            btnAnn.Click  += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            btnConf.Click += (s, ev) => { dialog.DialogResult = true;  dialog.Close(); };
            btnRow.Children.Add(btnAnn); btnRow.Children.Add(btnConf); root.Children.Add(btnRow);
            scroll.Content = root; dialog.Content = scroll; dialog.Owner = Window.GetWindow(this);
            return dialog.ShowDialog() == true;
        }

        private static Border CreerBlocRecap(string titre)
        {
            var border = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF5, 0xF3)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE0, 0xDC)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = titre, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x73, 0x55)), Margin = new Thickness(0, 0, 0, 8) });
            border.Child = stack; return border;
        }

        private static void AjouterLigneRecap(Border bloc, string label, string valeur, bool gras = false)
        {
            var stack = (StackPanel)bloc.Child;
            var row   = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)), VerticalAlignment = VerticalAlignment.Top };
            var val = new TextBlock { Text = valeur, FontSize = 12, FontWeight = gras ? FontWeights.Bold : FontWeights.Normal, Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(lbl, 0); Grid.SetColumn(val, 1); row.Children.Add(lbl); row.Children.Add(val); stack.Children.Add(row);
        }


        // ================================================================
        // VALIDATION INPUT (acompte, matériaux)
        // ================================================================
        private void TxtModalAcompte_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            Helpers.ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtModalAcompte_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            Helpers.ValidationHelper.TextBox_Pasting(sender, e);
        }

        private void TxtPrixCarte_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            Helpers.ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtMontant_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            Helpers.ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtMontant_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            Helpers.ValidationHelper.TextBox_Pasting(sender, e);
        }

        private void TxtMatDesignation_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            try { Helpers.ValidationHelper.TextBox_PreviewTextInputTexteSecurise(sender, e); }
            catch { e.Handled = true; }
        }
    }
}
