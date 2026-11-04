using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// ÉTAPE 2 PERF — AlertesView refondée :
    /// • Badges : ResumeAlertesDto (COUNT SQL, aucune entité chargée)
    /// • Sections : ObtenirSectionAsync (ORDER BY + LIMIT 50 en SQL)
    /// • Risque de retard : ListBox virtualisée avec DataTemplate XAML
    /// • Timer 60s : rafraîchit uniquement les badges si vue non visible
    /// • CancellationToken : annule le chargement précédent si la vue est quittée
    /// </summary>
    public partial class AlertesView : Page
    {
        // ── Services ─────────────────────────────────────────────────────
        private readonly IAlerteService       _alerteService;
        private readonly IWhatsAppService     _whatsAppService;
        private readonly ICommandeService     _commandeService;
        private readonly IRetardAlerteService _retardAlerteService;
        private readonly IAuthService         _authService;

        // ── État modal message d'excuse ───────────────────────────────────
        private BrouillonMessageExcuse? _brouillonExcuseCourant;

        // ── Timer + CancellationToken ─────────────────────────────────────
        private readonly DispatcherTimer  _timer;
        private DispatcherTimer?          _debounceTimer;
        private CancellationTokenSource   _cts = new();
        private bool                      _chargementEnCours;

        // ── Pages courantes (pagination) ──────────────────────────────────
        private int _pageRetards    = 1;
        private int _pagePrets      = 1;
        private int _pageAujourdhui = 1;
        private int _pageImminentes = 1;
        private int _pageRisque     = 1;
        private const int TAILLE_PAGE = 50;

        // ══════════════════════════════════════════════════════════════════
        public AlertesView()
        {
            InitializeComponent();

            _alerteService       = App.Services.GetRequiredService<IAlerteService>();
            _whatsAppService     = App.Services.GetRequiredService<IWhatsAppService>();
            _commandeService     = App.Services.GetRequiredService<ICommandeService>();
            _retardAlerteService = App.Services.GetRequiredService<IRetardAlerteService>();
            _authService         = App.Services.GetRequiredService<IAuthService>();

            // Timer 60s : recharge badges + section visible
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _timer.Tick += async (_, _) =>
            {
                if (!IsLoaded) return;
                // Badges d'abord (COUNT SQL pur, <10ms)
                await ChargerBadgesAsync();
                // Section complète uniquement si la vue est visible
                if (IsVisible) await ChargerToutesSectionsAsync();
            };

            // Debounce 500ms sur CommandeChanged
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _debounceTimer.Tick += async (_, _) =>
            {
                _debounceTimer!.Stop();
                await ChargerDonnees();
            };

            Loaded += async (_, _) =>
            {
                await ChargerDonnees();
                _timer.Start();
                _commandeService.CommandeChanged += OnCommandeChanged;
            };

            Unloaded += (_, _) =>
            {
                _timer.Stop();
                _debounceTimer?.Stop();
                _cts.Cancel();
                _commandeService.CommandeChanged -= OnCommandeChanged;
            };
        }

        private async void OnCommandeChanged(object? sender, CommandeChangedEventArgs e)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _debounceTimer?.Stop();
                _debounceTimer?.Start();
            });
        }

        // ══════════════════════════════════════════════════════════════════
        // CHARGEMENT PRINCIPAL
        // ══════════════════════════════════════════════════════════════════
        private async Task ChargerDonnees()
        {
            if (_chargementEnCours) return;
            if (!IsLoaded) return;
            _chargementEnCours = true;

            // Annuler le chargement précédent
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            using var t = GestionCoutureApp.Helpers.PerfLogger.Start("AlertesView.ChargerDonnees");
            try
            {
                // 1. Badges immédiats (COUNT SQL pur, ~5ms)
                await ChargerBadgesAsync(token);
                if (token.IsCancellationRequested) return;

                // 2. Toutes les sections en parallèle
                await ChargerToutesSectionsAsync(token);
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlertesView] Erreur : {ex.Message}");
            }
            finally { _chargementEnCours = false; }
        }

        // ── Badges (COUNT SQL) ────────────────────────────────────────────
        private async Task ChargerBadgesAsync(CancellationToken ct = default)
        {
            try
            {
                var resume = await _alerteService.ObtenirResumeAsync();
                if (ct.IsCancellationRequested) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (TxtBadgeNbPrets      != null) TxtBadgeNbPrets.Text      = resume.NbPrets.ToString();
                    if (TxtBadgeNbRetards    != null) TxtBadgeNbRetards.Text    = resume.NbRetards.ToString();
                    if (TxtBadgeNbAujourdhui != null) TxtBadgeNbAujourdhui.Text = resume.NbAujourdhui.ToString();
                    if (TxtBadgeNbImminentes != null) TxtBadgeNbImminentes.Text = resume.NbImminentes.ToString();
                    if (TxtBadgeNbRisqueRetard != null) TxtBadgeNbRisqueRetard.Text = resume.NbRisqueRetard.ToString();

                    // Badge menu principal
                    if (Window.GetWindow(this) is MainWindow mw)
                        mw.MettreAJourBadgeAlertes(resume.NbBadgeMenu);
                });
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                System.Diagnostics.Debug.WriteLine($"[AlertesView] Erreur badges : {ex.Message}");
            }
        }

        // ── Sections (ORDER BY + LIMIT SQL) ──────────────────────────────
        private async Task ChargerToutesSectionsAsync(CancellationToken ct = default)
        {
            // Lancer toutes les sections en parallèle
            var tasks = new[]
            {
                ChargerSectionAsync(SectionAlerte.Retards,    ListeRetards,    TxtZeroRetard,    null, _pageRetards, ct),
                ChargerSectionAsync(SectionAlerte.Prets,      ListePrets,      TxtZeroPrets,     null, _pagePrets, ct),
                ChargerSectionAsync(SectionAlerte.Aujourdhui, ListeAujourdhui, TxtZeroAujourdhui,null, _pageAujourdhui, ct),
                ChargerSectionAsync(SectionAlerte.Imminentes, ListeImminentes, TxtZeroImminentes,null, _pageImminentes, ct),
                ChargerRisqueRetardAsync(ct)
            };
            await Task.WhenAll(tasks);
        }

        private async Task ChargerSectionAsync(
            SectionAlerte section,
            ItemsControl liste,
            TextBlock msgVide,
            TextBlock? txtTotal,
            int page,
            CancellationToken ct)
        {
            try
            {
                var result = await _alerteService.ObtenirSectionAsync(section, page, TAILLE_PAGE);
                if (ct.IsCancellationRequested) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    ApliquerSection(liste, msgVide, result.Items);
                    if (txtTotal != null)
                        txtTotal.Text = $"{result.TotalCount} au total";
                });
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                System.Diagnostics.Debug.WriteLine($"[AlertesView] Section {section} : {ex.Message}");
            }
        }

        private async Task ChargerRisqueRetardAsync(CancellationToken ct = default)
        {
            try
            {
                int minutes = await App.Services.GetRequiredService<IParametresService>()
                    .ObtenirRetardAlerteAvantMinutes();
                var result = await _retardAlerteService.ObtenirAlertesRisqueRetardPageAsync(
                    _pageRisque, TAILLE_PAGE);
                if (ct.IsCancellationRequested) return;

                bool peutPrevenir = _authService.UtilisateurConnecte?.Role is "Secretaire" or "Boss";

                var cartes = result.Items.Select(a => new AlerteCommandeVm
                {
                    IdCommande           = a.IdCommande,
                    NomClient            = a.NomClient,
                    Telephone            = a.Telephone,
                    DateEcheance         = a.DateRendezVous,
                    HeureRdv             = a.HeureRendezVous,
                    DetailPieces         = a.EstAlertRetour
                        ? $"🔄 RETOUR {a.NumeroRetour} — {a.TypeVetement} ({a.Statut})"
                        : $"{a.TypeVetement} ({a.Statut})",
                    LibelleBoutonWhatsApp = "",
                    EstAlertRetour        = a.EstAlertRetour,
                    IdRetour              = a.IdRetour,
                    NumeroRetour          = a.NumeroRetour,
                    TypeCibleRetard       = a.TypeCibleRetard,
                    IdCibleRetard         = a.IdCibleRetard,
                    IdClientRetard        = a.IdClientRetard,
                    NbFoisPrevenu         = a.NbFoisPrevenu,
                    AfficherPrevenirClient = peutPrevenir,
                    IdsPiecesCommande     = new List<int> { a.IdPieceCommande }
                }).ToList();

                await Dispatcher.InvokeAsync(() =>
                {
                    if (ListeRisqueRetard != null)
                    {
                        ListeRisqueRetard.ItemsSource = cartes;
                        if (TxtZeroRisqueRetard != null)
                            TxtZeroRisqueRetard.Visibility = cartes.Count == 0
                                ? Visibility.Visible : Visibility.Collapsed;
                    }
                    if (TxtBadgeNbRisqueRetard != null)
                        TxtBadgeNbRisqueRetard.Text = result.TotalCount.ToString();
                });
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                System.Diagnostics.Debug.WriteLine($"[AlertesView] RisqueRetard : {ex.Message}");
            }
        }

        private static void ApliquerSection(
            ItemsControl liste, TextBlock msgVide, List<AlerteCommandeVm> cartes)
        {
            liste.ItemsSource = cartes;
            msgVide.Visibility = cartes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══════════════════════════════════════════════════════════════════
        // BOUTONS DE SECTION (WhatsApp, Voir, Voir plus)
        // ══════════════════════════════════════════════════════════════════

        private async void BtnActualiser_Click(object sender, RoutedEventArgs e)
            => await ChargerDonnees();

        private void BtnWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AlerteCommandeVm vm) return;
            if (string.IsNullOrWhiteSpace(vm.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro enregistré.",
                    "Numéro manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var commande = _commandeService.ObtenirParId(vm.IdCommande);
            if (commande == null) return;
            string msg = $"Bonjour {vm.NomClient}, votre commande est prête. Merci de venir la retirer.";
            _whatsAppService.OuvrirConversation(vm.Telephone, msg);
        }

        private void BtnVoirDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AlerteCommandeVm vm) return;
            OuvrirModal(vm);
        }

        private void BtnVoirDetailsRetard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AlerteCommandeVm vm) return;
            OuvrirModal(vm);
        }

        private void OuvrirModal(AlerteCommandeVm vm)
        {
            if (ModalDetailsCommande == null) return;
            if (TxtModalNomClient  != null) TxtModalNomClient.Text  = vm.NomClient;
            if (TxtModalTelephone  != null) TxtModalTelephone.Text  = vm.Telephone;
            if (TxtModalEcheance   != null) TxtModalEcheance.Text   = vm.DateEcheanceAffichee;
            if (TxtModalSoustitre  != null) TxtModalSoustitre.Text  = vm.DetailPieces;
            ModalDetailsCommande.Visibility = Visibility.Visible;
        }

        private void BtnFermerModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalDetailsCommande != null)
                ModalDetailsCommande.Visibility = Visibility.Collapsed;
        }

        // ── Bouton "Prévenir le client" (section Risque de retard) ───────
        private async void BtnPrevenirClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AlerteCommandeVm vm) return;

            btn.IsEnabled = false;
            try
            {
                var brouillon = await _retardAlerteService.PreparerMessageExcuseAsync(
                    vm.TypeCibleRetard, vm.IdCibleRetard, vm.DateEcheance);

                _brouillonExcuseCourant = brouillon;

                if (TxtModalExcuseSousTitre  != null) TxtModalExcuseSousTitre.Text  = $"{vm.NomClient} — RDV le {vm.DateCourte} à {vm.HeureRdv}";
                if (TxtModalExcuseClient     != null) TxtModalExcuseClient.Text     = vm.NomClient;
                if (TxtModalExcuseTel        != null) TxtModalExcuseTel.Text        = string.IsNullOrWhiteSpace(brouillon.Telephone) ? "(pas de numéro)" : brouillon.Telephone;
                if (TxtModalExcuseNouvelleDate != null) TxtModalExcuseNouvelleDate.Text = brouillon.NouvelleDateProposee.ToString("dddd dd MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));

                if (TxtModalExcuseCompteur != null)
                {
                    TxtModalExcuseCompteur.Visibility = brouillon.NbFoisPrevenu > 0 ? Visibility.Visible : Visibility.Collapsed;
                    TxtModalExcuseCompteur.Text = $"⚠️  Client déjà prévenu {brouillon.NbFoisPrevenu} fois";
                }

                bool numOk = _retardAlerteService.NumeroClientValide(brouillon.Telephone);
                if (PanelAvertissementNumero != null) PanelAvertissementNumero.Visibility = numOk ? Visibility.Collapsed : Visibility.Visible;
                if (BtnEnvoyerWhatsApp       != null) BtnEnvoyerWhatsApp.IsEnabled        = numOk;
                if (TxtModalExcuseMessage    != null) TxtModalExcuseMessage.Text          = brouillon.Message;
                if (TxtModalExcuseMsg        != null) TxtModalExcuseMsg.Text             = "";
                if (ModalMessageExcuse       != null) ModalMessageExcuse.Visibility       = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { btn.IsEnabled = true; }
        }

        private void BtnFermerModalExcuse_Click(object sender, RoutedEventArgs e)
        {
            if (ModalMessageExcuse != null) ModalMessageExcuse.Visibility = Visibility.Collapsed;
            _brouillonExcuseCourant = null;
        }

        private async void BtnEnvoyerWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (_brouillonExcuseCourant == null) return;
            var utilisateur = _authService.UtilisateurConnecte;
            if (utilisateur == null) return;

            if (BtnEnvoyerWhatsApp != null) BtnEnvoyerWhatsApp.IsEnabled = false;
            try
            {
                string message = TxtModalExcuseMessage?.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(message))
                {
                    if (TxtModalExcuseMsg != null) { TxtModalExcuseMsg.Text = "Le message ne peut pas être vide."; TxtModalExcuseMsg.Foreground = Brushes.Red; }
                    return;
                }

                _whatsAppService.OuvrirConversation(_brouillonExcuseCourant.Telephone, message);

                await _retardAlerteService.EnregistrerPreventionAsync(
                    _brouillonExcuseCourant.TypeCible, _brouillonExcuseCourant.IdCible,
                    _brouillonExcuseCourant.DateRdvActuelle, _brouillonExcuseCourant.IdClient,
                    utilisateur.IdEmploye, $"{utilisateur.Prenom} {utilisateur.Nom}");

                if (TxtModalExcuseMsg != null)
                {
                    TxtModalExcuseMsg.Text = "✅  Message ouvert dans WhatsApp.";
                    TxtModalExcuseMsg.Foreground = Brushes.Green;
                }

                var nouvelleDate = _brouillonExcuseCourant.NouvelleDateProposee.ToString("dd/MM/yyyy");
                var rep = MessageBox.Show(
                    $"Souhaitez-vous REPORTER le RDV au {nouvelleDate} ?",
                    "Reporter le RDV ?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (rep == MessageBoxResult.Yes)
                {
                    await _retardAlerteService.ReporterRendezVousAsync(
                        _brouillonExcuseCourant.TypeCible, _brouillonExcuseCourant.IdCible,
                        _brouillonExcuseCourant.DateRdvActuelle,
                        utilisateur.IdEmploye, $"{utilisateur.Prenom} {utilisateur.Nom}");
                    if (TxtModalExcuseMsg != null)
                    {
                        TxtModalExcuseMsg.Text = $"✅  RDV reporté au {nouvelleDate}.";
                        TxtModalExcuseMsg.Foreground = Brushes.Green;
                    }
                }
                await ChargerDonnees();
            }
            catch (Exception ex)
            {
                if (TxtModalExcuseMsg != null) { TxtModalExcuseMsg.Text = "Erreur : " + ex.Message; TxtModalExcuseMsg.Foreground = Brushes.Red; }
            }
            finally { if (BtnEnvoyerWhatsApp != null) BtnEnvoyerWhatsApp.IsEnabled = true; }
        }
    }
}
