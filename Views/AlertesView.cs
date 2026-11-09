using System.Media;
using System.Collections.ObjectModel;
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
    /// • Sections : ObtenirSectionAsync (ORDER BY + pagination de 5 cartes)
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
        private static readonly object VerrouNotifications = new();
        private static readonly HashSet<string> AlertesUrgentesDejaSignalees = new(StringComparer.Ordinal);
        private static bool _notificationsInitialisees;

        // ── Pages courantes (pagination) ──────────────────────────────────
        private int _pageRetards    = 1;
        private int _pagePrets      = 1;
        private int _pageAujourdhui = 1;
        private int _pageImminentes = 1;
        private int _pageRisque     = 1;
        private const int TAILLE_PAGE = 5;
        private readonly Dictionary<SectionAlerte, int> _totauxSections = new();

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
                if (!IsLoaded || !IsVisible || _chargementEnCours) return;
                await ChargerDonnees();
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
                // Les requêtes partent ensemble et s'exécutent hors du fil UI.
                // L'écran reste utilisable pendant les lectures de SQLite.
                await Task.WhenAll(
                    ChargerBadgesAsync(token),
                    ChargerToutesSectionsAsync(token),
                    VerifierNotificationsUrgentesAsync(token));
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlertesView] Erreur : {ex.Message}");
            }
            finally { _chargementEnCours = false; }
        }

        private async Task VerifierNotificationsUrgentesAsync(CancellationToken ct = default)
        {
            var urgentes = await Task.Run(
                () => _alerteService.ObtenirAlertesUrgentesAsync(ct), ct);
            List<AlerteCommandeVm> nouvelles = new();
            lock (VerrouNotifications)
            {
                // Au premier passage, mémoriser les alertes déjà présentes sans
                // afficher une boîte de dialogue pour tout l'historique en retard.
                if (!_notificationsInitialisees)
                {
                    foreach (var alerte in urgentes)
                        AlertesUrgentesDejaSignalees.Add(CleAlerteUrgente(alerte));
                    _notificationsInitialisees = true;
                    return;
                }

                foreach (var alerte in urgentes)
                    if (AlertesUrgentesDejaSignalees.Add(CleAlerteUrgente(alerte)))
                        nouvelles.Add(alerte);
            }
            if (nouvelles.Count == 0) return;

            SystemSounds.Exclamation.Play();
            MessageBox.Show(
                string.Join(Environment.NewLine, nouvelles.Take(5).Select(a =>
                    $"{a.NomClient} — {a.DetailPieces} — {a.DateEcheance:dd/MM HH:mm}")),
                "⚠️ Alertes RDV urgentes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static string CleAlerteUrgente(AlerteCommandeVm alerte) =>
            $"{(alerte.EstAlertRetour ? "R" : "C")}:{(alerte.EstAlertRetour ? alerte.IdRetour : alerte.IdCommande)}:{alerte.DateEcheance:O}";

        // ── Badges (COUNT SQL) ────────────────────────────────────────────
        private async Task ChargerBadgesAsync(CancellationToken ct = default)
        {
            try
            {
                var resume = await Task.Run(
                    () => _alerteService.ObtenirResumeAsync(ct), ct);
                if (ct.IsCancellationRequested) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (TxtBadgeNbPrets      != null) TxtBadgeNbPrets.Text      = FormaterBadge(resume.NbPrets);
                    if (TxtBadgeNbRetards    != null) TxtBadgeNbRetards.Text    = FormaterBadge(resume.NbRetards);
                    if (TxtBadgeNbAujourdhui != null) TxtBadgeNbAujourdhui.Text = FormaterBadge(resume.NbAujourdhui);
                    if (TxtBadgeNbImminentes != null) TxtBadgeNbImminentes.Text = FormaterBadge(resume.NbImminentes);
                    if (TxtBadgeNbRisqueRetard != null) TxtBadgeNbRisqueRetard.Text = FormaterBadge(resume.NbRisqueRetard);

                    if (Window.GetWindow(this) is MainWindow mw)
                        mw.AcquitterBadgeAlertes(resume.NbBadgeMenu);
                });
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                System.Diagnostics.Debug.WriteLine($"[AlertesView] Erreur badges : {ex.Message}");
            }
        }

        private static string FormaterBadge(int nombre) => nombre >= 1000 ? "999+" : nombre.ToString();

        // ── Sections (ORDER BY + LIMIT SQL) ──────────────────────────────
        private async Task ChargerToutesSectionsAsync(CancellationToken ct = default)
        {
            _pageRetards = _pagePrets = _pageAujourdhui = _pageImminentes = _pageRisque = 1;
            // Lancer toutes les sections en parallèle
            var tasks = new[]
            {
                ChargerSectionAsync(SectionAlerte.Retards,    ListeRetards,    TxtZeroRetard,    TxtTotalRetards, _pageRetards, ct),
                ChargerSectionAsync(SectionAlerte.Prets,      ListePrets,      TxtZeroPrets,     TxtTotalPrets, _pagePrets, ct),
                ChargerSectionAsync(SectionAlerte.Aujourdhui, ListeAujourdhui, TxtZeroAujourdhui,TxtTotalAujourdhui, _pageAujourdhui, ct),
                ChargerSectionAsync(SectionAlerte.Imminentes, ListeImminentes, TxtZeroImminentes,TxtTotalImminentes, _pageImminentes, ct),
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
                var result = await Task.Run(
                    () => _alerteService.ObtenirSectionAsync(section, page, TAILLE_PAGE, ct), ct);
                if (ct.IsCancellationRequested) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    ApliquerSection(liste, msgVide, result.Items, txtTotal, result.TotalCount);
                    _totauxSections[section] = result.TotalCount;
                    MettreAJourPagination(section, page, result.TotalCount);
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
                var result = await Task.Run(
                    () => _retardAlerteService.ObtenirAlertesRisqueRetardPageAsync(
                        _pageRisque, TAILLE_PAGE, cancellationToken: ct), ct);
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
                    if (TxtBadgeNbRisqueRetardSection != null)
                        TxtBadgeNbRisqueRetardSection.Text = result.TotalCount.ToString();
                    if (TxtTotalRisqueRetard != null)
                        TxtTotalRisqueRetard.Text = $"{cartes.Count} affichés sur {result.TotalCount}";
                    _totauxSections[SectionAlerte.RisqueRetard] = result.TotalCount;
                    MettreAJourPagination(SectionAlerte.RisqueRetard, _pageRisque, result.TotalCount);
                });
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                System.Diagnostics.Debug.WriteLine($"[AlertesView] RisqueRetard : {ex.Message}");
            }
        }

        private static void ApliquerSection(
            ItemsControl liste, TextBlock msgVide, List<AlerteCommandeVm> cartes,
            TextBlock? txtTotal, int total)
        {
            var collection = new ObservableCollection<AlerteCommandeVm>(cartes);
            liste.ItemsSource = collection;
            msgVide.Visibility = collection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (txtTotal != null) txtTotal.Text = $"{collection.Count} affichés sur {total}";
        }

        private async void BtnPaginationAlerte_Click(object sender, RoutedEventArgs e)
        {
            if (_chargementEnCours || sender is not Button btn || btn.Tag is not string tag) return;
            var morceaux = tag.Split(':');
            if (morceaux.Length != 2 || !Enum.TryParse(morceaux[0], out SectionAlerte section)
                || !int.TryParse(morceaux[1], out int delta)) return;

            int page = PageCourante(section);
            int total = _totauxSections.GetValueOrDefault(section);
            int maxPage = Math.Max(1, (total + TAILLE_PAGE - 1) / TAILLE_PAGE);
            int nouvellePage = Math.Clamp(page + delta, 1, maxPage);
            if (nouvellePage == page) return;

            DefinirPage(section, nouvellePage);
            btn.IsEnabled = false;
            ItemsControl liste;
            TextBlock vide;
            TextBlock? totalText;
            switch (section)
            {
                case SectionAlerte.Retards: liste = ListeRetards; vide = TxtZeroRetard; totalText = TxtTotalRetards; break;
                case SectionAlerte.Prets: liste = ListePrets; vide = TxtZeroPrets; totalText = TxtTotalPrets; break;
                case SectionAlerte.Aujourdhui: liste = ListeAujourdhui; vide = TxtZeroAujourdhui; totalText = TxtTotalAujourdhui; break;
                case SectionAlerte.Imminentes: liste = ListeImminentes; vide = TxtZeroImminentes; totalText = TxtTotalImminentes; break;
                default: await ChargerRisqueRetardAsync(_cts.Token); return;
            }
            await ChargerSectionAsync(section, liste, vide, totalText, nouvellePage, _cts.Token);
        }

        private (Button Precedent, Button Suivant, TextBlock Page) ControlePagination(SectionAlerte section) => section switch
        {
            SectionAlerte.Retards => (BtnPrecedentRetards, BtnSuivantRetards, TxtPageRetards),
            SectionAlerte.Prets => (BtnPrecedentPrets, BtnSuivantPrets, TxtPagePrets),
            SectionAlerte.Aujourdhui => (BtnPrecedentAujourdhui, BtnSuivantAujourdhui, TxtPageAujourdhui),
            SectionAlerte.Imminentes => (BtnPrecedentImminentes, BtnSuivantImminentes, TxtPageImminentes),
            _ => (BtnPrecedentRisque, BtnSuivantRisque, TxtPageRisque)
        };

        private void MettreAJourPagination(SectionAlerte section, int page, int total)
        {
            var (precedent, suivant, textePage) = ControlePagination(section);
            int maxPage = Math.Max(1, (total + TAILLE_PAGE - 1) / TAILLE_PAGE);
            textePage.Text = $"Page {page} / {maxPage}";
            precedent.IsEnabled = page > 1;
            suivant.IsEnabled = page < maxPage;
        }

        private int PageCourante(SectionAlerte section) => section switch
        {
            SectionAlerte.Retards => _pageRetards,
            SectionAlerte.Prets => _pagePrets,
            SectionAlerte.Aujourdhui => _pageAujourdhui,
            SectionAlerte.Imminentes => _pageImminentes,
            _ => _pageRisque
        };

        private void DefinirPage(SectionAlerte section, int page)
        {
            switch (section)
            {
                case SectionAlerte.Retards: _pageRetards = page; break;
                case SectionAlerte.Prets: _pagePrets = page; break;
                case SectionAlerte.Aujourdhui: _pageAujourdhui = page; break;
                case SectionAlerte.Imminentes: _pageImminentes = page; break;
                default: _pageRisque = page; break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // BOUTONS DE SECTION (WhatsApp, Voir, Voir plus)
        // ══════════════════════════════════════════════════════════════════

        private async void BtnActualiser_Click(object sender, RoutedEventArgs e)
            => await ChargerDonnees();

        private async void BtnWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AlerteCommandeVm vm) return;
            if (string.IsNullOrWhiteSpace(vm.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro enregistré.",
                    "Numéro manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (vm.EstAlertRetour) return;
            var commande = await Task.Run(() => _commandeService.ObtenirParId(vm.IdCommande));
            if (commande == null) return;
            try
            {
                if (vm.LibelleBoutonWhatsApp.Contains("prête", StringComparison.OrdinalIgnoreCase))
                    await _whatsAppService.NotifierCommandePreteAsync(commande);
                else
                    await _whatsAppService.NotifierRappelRdvAsync(commande);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible de préparer le message WhatsApp : " + ex.Message,
                    "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnVoirDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AlerteCommandeVm vm) return;
            await OuvrirModalAsync(vm);
        }

        private async void BtnVoirDetailsRetard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AlerteCommandeVm vm) return;
            await OuvrirModalAsync(vm);
        }

        private async Task OuvrirModalAsync(AlerteCommandeVm vm)
        {
            if (ModalDetailsCommande == null) return;
            if (vm.IdCommande > 0)
            {
                var commande = await Task.Run(() => _commandeService.ObtenirParId(vm.IdCommande));
                ListeModalPieces.ItemsSource = commande?.Pieces.Select(p => new
                {
                    TypeVetement = $"{p.TypeVetement} — {(p.Couturier == null ? "(non assigné)" : $"{p.Couturier.Prenom} {p.Couturier.Nom}".Trim())} — RDV {p.DateRdvEffective:dd/MM/yyyy HH:mm}",
                    Statut = p.Statut switch { "A faire" => "À faire", "Terminee" => "Terminée", "Livree" => "Livrée", _ => p.Statut },
                    Couturier = p.Couturier == null ? "(non assigné)" : $"{p.Couturier.Prenom} {p.Couturier.Nom}".Trim(),
                    RendezVous = p.DateRdvEffective.ToString("dd/MM/yyyy HH:mm")
                }).ToList();
            }
            else ListeModalPieces.ItemsSource = null;
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
