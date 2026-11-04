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
        private readonly Dictionary<int, DateTime> _dernieresNotifications = new();
        private static readonly TimeSpan DelaiRepetitionNotification = TimeSpan.FromMinutes(15);

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
                if (!IsLoaded || !IsVisible || _chargementEnCours) return;
                // Badges d'abord (COUNT SQL pur, <10ms)
                await ChargerBadgesAsync();
                await ChargerToutesSectionsAsync();
                await VerifierNotificationsUrgentesAsync();
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
                await VerifierNotificationsUrgentesAsync(token);
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
            var urgentes = await _alerteService.ObtenirAlertesUrgentesAsync(ct);
            var maintenant = DateTime.Now;
            var nouvelles = urgentes.Where(a =>
            {
                int cle = a.EstAlertRetour ? -a.IdRetour : a.IdCommande;
                return !_dernieresNotifications.TryGetValue(cle, out var derniere)
                    || maintenant - derniere >= DelaiRepetitionNotification;
            }).ToList();
            if (nouvelles.Count == 0) return;

            SystemSounds.Exclamation.Play();
            MessageBox.Show(
                string.Join(Environment.NewLine, nouvelles.Take(5).Select(a =>
                    $"{a.NomClient} — {a.DetailPieces} — {a.DateEcheance:dd/MM HH:mm}")),
                "⚠️ Alertes RDV urgentes", MessageBoxButton.OK, MessageBoxImage.Warning);
            foreach (var alerte in nouvelles)
                _dernieresNotifications[alerte.EstAlertRetour ? -alerte.IdRetour : alerte.IdCommande] = DateTime.Now;
        }

        // ── Badges (COUNT SQL) ────────────────────────────────────────────
        private async Task ChargerBadgesAsync(CancellationToken ct = default)
        {
            try
            {
                var resume = await _alerteService.ObtenirResumeAsync(ct);
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
            _pageRetards = _pagePrets = _pageAujourdhui = _pageImminentes = 1;
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
                var result = await _alerteService.ObtenirSectionAsync(section, page, TAILLE_PAGE, ct);
                if (ct.IsCancellationRequested) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    ApliquerSection(liste, msgVide, result.Items, page, txtTotal, result.TotalCount);
                    var plus = BoutonVoirPlus(section);
                    if (plus != null) plus.Visibility = liste.Items.Count < result.TotalCount
                        ? Visibility.Visible : Visibility.Collapsed;
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
                    _pageRisque, TAILLE_PAGE, cancellationToken: ct);
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
            ItemsControl liste, TextBlock msgVide, List<AlerteCommandeVm> cartes,
            int page, TextBlock? txtTotal, int total)
        {
            ObservableCollection<AlerteCommandeVm> collection;
            if (page == 1 || liste.ItemsSource is not ObservableCollection<AlerteCommandeVm> existing)
            {
                collection = new ObservableCollection<AlerteCommandeVm>(cartes);
                liste.ItemsSource = collection;
            }
            else
            {
                collection = existing;
                foreach (var carte in cartes) collection.Add(carte);
            }
            msgVide.Visibility = collection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (txtTotal != null) txtTotal.Text = $"{collection.Count} affichés sur {total}";
        }

        private async void BtnVoirPlus_Click(object sender, RoutedEventArgs e)
        {
            if (_chargementEnCours || sender is not Button btn || btn.Tag is not string tag) return;
            var section = Enum.Parse<SectionAlerte>(tag);
            ItemsControl liste; TextBlock vide, total; int page;
            switch (section)
            {
                case SectionAlerte.Retards: page = ++_pageRetards; liste = ListeRetards; vide = TxtZeroRetard; total = TxtTotalRetards; break;
                case SectionAlerte.Prets: page = ++_pagePrets; liste = ListePrets; vide = TxtZeroPrets; total = TxtTotalPrets; break;
                case SectionAlerte.Aujourdhui: page = ++_pageAujourdhui; liste = ListeAujourdhui; vide = TxtZeroAujourdhui; total = TxtTotalAujourdhui; break;
                default: page = ++_pageImminentes; liste = ListeImminentes; vide = TxtZeroImminentes; total = TxtTotalImminentes; break;
            }
            btn.IsEnabled = false;
            await ChargerSectionAsync(section, liste, vide, total, page, _cts.Token);
            btn.IsEnabled = liste.Items.Count >= page * TAILLE_PAGE;
            btn.Visibility = liste.Items.Count < (int.TryParse(total.Text.Split("sur ").LastOrDefault(), out var n) ? n : 0)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private Button? BoutonVoirPlus(SectionAlerte section) => section switch
        {
            SectionAlerte.Retards => BtnPlusRetards,
            SectionAlerte.Prets => BtnPlusPrets,
            SectionAlerte.Aujourdhui => BtnPlusAujourdhui,
            SectionAlerte.Imminentes => BtnPlusImminentes,
            _ => null
        };

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
