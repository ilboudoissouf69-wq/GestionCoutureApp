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
    public partial class AlertesView : Page
    {
        // ── Services ─────────────────────────────────────────────────────
        private readonly IAlerteService        _alerteService;
        private readonly IWhatsAppService      _whatsAppService;
        private readonly ICommandeService      _commandeService;
        private readonly IRetardAlerteService  _retardAlerteService;
        private readonly IAuthService          _authService;

        // ── Données brutes chargées depuis les services ───────────────────
        private List<AlerteRendezVous> _alertesProduction = new();
        private List<AlerteRendezVous> _alertesRetrait    = new();
        private List<AlerteRendezVous> _alertesRetard     = new();
        // Étape 6 : retours actifs avec RDV
        private List<AlerteRendezVous> _alertesRetours    = new();
        // Étape 7 : alertes risque de retard
        private List<AlerteRendezVous> _alertesRisqueRetard = new();

        // ── État modal message d'excuse ───────────────────────────────────
        private BrouillonMessageExcuse? _brouillonExcuseCourant;

        // ── Timer 60 s + anti-répétition notifications ────────────────────
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<int, DateTime> _dernieresNotifications = new();
        private static readonly TimeSpan DelaiRepetitionNotif = TimeSpan.FromMinutes(15);

        // ══════════════════════════════════════════════════════════════════
        // CONSTRUCTEUR
        // ══════════════════════════════════════════════════════════════════
        public AlertesView()
        {
            InitializeComponent();

            _alerteService       = App.Services.GetRequiredService<IAlerteService>();
            _whatsAppService     = App.Services.GetRequiredService<IWhatsAppService>();
            _commandeService     = App.Services.GetRequiredService<ICommandeService>();
            _retardAlerteService = App.Services.GetRequiredService<IRetardAlerteService>();
            _authService         = App.Services.GetRequiredService<IAuthService>();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _timer.Tick += async (s, e) => await ChargerDonnees();

            Loaded += async (s, e) =>
            {
                await ChargerDonnees();
                _timer.Start();
                _commandeService.CommandeChanged += OnCommandeChanged;
            };

            Unloaded += (s, e) =>
            {
                _timer.Stop();
                _commandeService.CommandeChanged -= OnCommandeChanged;
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // RAFRAÎCHISSEMENT SUR CHANGEMENT DE COMMANDE
        // ══════════════════════════════════════════════════════════════════
        private async void OnCommandeChanged(object? sender, CommandeChangedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () => await ChargerDonnees());
        }

        // ══════════════════════════════════════════════════════════════════
        // CHARGEMENT PRINCIPAL
        // ══════════════════════════════════════════════════════════════════
        private async Task ChargerDonnees()
        {
            try
            {
                _alertesProduction = await _alerteService.ObtenirAlertesActuelles();
                _alertesRetrait    = await _alerteService.ObtenirRendezVousSemaine();
                _alertesRetard     = await _alerteService.ObtenirRetards();
                // Étape 6 : retours actifs
                try { _alertesRetours = await _alerteService.ObtenirAlertesRetours(); }
                catch { _alertesRetours = new(); }
                // Étape 7 : risque de retard
                try { _alertesRisqueRetard = await _retardAlerteService.ObtenirAlertesRisqueRetardAsync(); }
                catch { _alertesRisqueRetard = new(); }

                RepartirEtAfficher();
                await VerifierNotificationsUrgentes();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlertesView] Erreur chargement : {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // RÉPARTITION EN 4 SECTIONS (§3 du cahier des charges)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fusionne les trois listes, dédoublonne par IdPieceCommande,
        /// affecte chaque pièce à UNE SEULE section par ordre de priorité,
        /// regroupe par commande en AlerteCommandeVm, puis alimente les
        /// quatre ItemsControls.
        /// </summary>
        private void RepartirEtAfficher()
        {
            var maintenant = DateTime.Now;

            // 1. Union dédoublonnée (IdPieceCommande) pour les commandes.
            // Priorité d'ordre : retards d'abord (RDV dépassé = plus urgent),
            // puis retrait, puis production — pour que la déduplification
            // garde la version "retard" quand une pièce est dans plusieurs listes.
            var toutesCommandes = _alertesRetard
                .Concat(_alertesRetrait)
                .Concat(_alertesProduction)
                .GroupBy(a => a.IdPieceCommande)
                .Select(g => g.First())
                .ToList();

            // 2. Retours — dédoublonnés par IdRetour (distinct des pièces commande)
            var retoursDedoub = _alertesRetours
                .GroupBy(r => r.IdRetour)
                .Select(g => g.First())
                .ToList();

            // 3. Affectation à une section par priorité
            var prets       = new List<AlerteRendezVous>();
            var retards     = new List<AlerteRendezVous>();
            var auJourdhui  = new List<AlerteRendezVous>();
            var imminentes  = new List<AlerteRendezVous>();

            // ── Commandes ────────────────────────────────────────────────
            foreach (var a in toutesCommandes)
            {
                bool rdvDepasse = a.DateRendezVous < maintenant;
                if (rdvDepasse)                          { retards.Add(a);    continue; }
                if (a.PiecePrete)                        { prets.Add(a);      continue; }
                if (a.DateRendezVous.Date == maintenant.Date) { auJourdhui.Add(a); continue; }
                bool rdvSemaine = a.DateRendezVous.Date > maintenant.Date
                               && a.DateRendezVous.Date <= maintenant.AddDays(7).Date;
                bool stagnant = a.TypeAlerte == "PasEncorePriseEnCharge"
                             && a.DateRendezVous.Date > maintenant.AddDays(7).Date;
                if (rdvSemaine || stagnant) imminentes.Add(a);
            }

            // ── Retours (Étape 6) : même logique de priorité ─────────────
            foreach (var r in retoursDedoub)
            {
                bool rdvDepasse = r.DateRendezVous < maintenant;
                bool estTermine = r.Statut is "Termine" or "Terminé";

                if (rdvDepasse && !estTermine)           { retards.Add(r);    continue; }
                if (estTermine)                          { prets.Add(r);      continue; }
                if (r.DateRendezVous.Date == maintenant.Date) { auJourdhui.Add(r); continue; }
                bool rdvSemaine = r.DateRendezVous.Date > maintenant.Date
                               && r.DateRendezVous.Date <= maintenant.AddDays(7).Date;
                if (rdvSemaine) imminentes.Add(r);
            }

            // 4. Tris par section
            retards    = retards
                .OrderByDescending(a => a.PiecePrete || a.Statut is "Termine" or "Terminé")
                .ThenBy(a => a.DateRendezVous)
                .ToList();
            prets      = prets.OrderByDescending(a => a.ProposerContactWhatsApp).ThenBy(a => a.DateRendezVous).ToList();
            auJourdhui = auJourdhui.OrderBy(a => a.DateRendezVous).ToList();
            imminentes = imminentes.OrderBy(a => a.DateRendezVous).ToList();

            // 5. Groupement en cartes
            var cartesPrets      = Grouper(prets,      "💬 Prévenir : commande prête");
            var cartesRetards    = Grouper(retards,    "💬 Relancer le client");
            var cartesAujourdhui = Grouper(auJourdhui, "💬 Rappel RDV");
            var cartesImminentes = Grouper(imminentes, "");

            // 6. Mise à jour badges KPI
            TxtBadgeNbPrets.Text       = cartesPrets.Count.ToString();
            TxtBadgeNbRetards.Text     = cartesRetards.Count.ToString();
            TxtBadgeNbAujourdhui.Text  = cartesAujourdhui.Count.ToString();
            TxtBadgeNbImminentes.Text  = cartesImminentes.Count.ToString();

            // 7. Alimenter les sections
            AppliquerSection(ListePrets,      TxtZeroPrets,      cartesPrets);
            AppliquerSection(ListeRetards,    TxtZeroRetard,     cartesRetards);
            AppliquerSection(ListeAujourdhui, TxtZeroAujourdhui, cartesAujourdhui);
            AppliquerSection(ListeImminentes, TxtZeroImminentes, cartesImminentes);

            // 8. Badge menu principal : retards commandes + retards retours + livraisons du jour
            var toutesDedup = toutesCommandes.Concat(retoursDedoub).ToList();
            int totalBadge = toutesDedup.Count(a => a.DateRendezVous < maintenant || a.NiveauAlerte == "jourbj");
            if (Window.GetWindow(this) is MainWindow mw)
                mw.MettreAJourBadgeAlertes(totalBadge);

            // 9. Étape 7 — Section risque de retard
            AfficherSectionRisqueRetard();
        }

        // ── Groupement des AlerteRendezVous en AlerteCommandeVm ──────────
        private static List<AlerteCommandeVm> Grouper(
            List<AlerteRendezVous> pieces,
            string libelleBoutonWhatsApp)
        {
            var result = new List<AlerteCommandeVm>();

            // Séparer retours et commandes
            var retours   = pieces.Where(a => a.EstAlertRetour).ToList();
            var commandes = pieces.Where(a => !a.EstAlertRetour).ToList();

            // Regrouper les commandes par IdCommande (logique existante)
            var cartesCommandes = commandes
                .GroupBy(a => a.IdCommande)
                .Select(g =>
                {
                    var ordered     = g.OrderBy(a => a.DateRendezVous).ToList();
                    var plusUrgente = ordered.First();

                    string detail = string.Join(" • ", ordered.Select(a =>
                        $"{a.TypeVetement} ({a.Statut})"));

                    bool stagnant = g.Any(a => a.TypeAlerte == "PasEncorePriseEnCharge");

                    return new AlerteCommandeVm
                    {
                        IdCommande            = g.Key,
                        NomClient             = plusUrgente.NomClient,
                        Telephone             = plusUrgente.Telephone,
                        DateEcheance          = plusUrgente.DateRendezVous,
                        HeureRdv              = plusUrgente.HeureRendezVous,
                        DetailPieces          = detail,
                        LibelleBoutonWhatsApp = libelleBoutonWhatsApp,
                        EstStagnant           = stagnant,
                        IdsPiecesCommande     = ordered.Select(a => a.IdPieceCommande).ToList(),
                        EstAlertRetour        = false
                    };
                });
            result.AddRange(cartesCommandes);

            // Un retour = une carte distincte (pas de regroupement inter-retours)
            foreach (var r in retours)
            {
                result.Add(new AlerteCommandeVm
                {
                    IdCommande            = r.IdCommande,
                    NomClient             = r.NomClient,
                    Telephone             = r.Telephone,
                    DateEcheance          = r.DateRendezVous,
                    HeureRdv              = r.HeureRendezVous,
                    DetailPieces          = $"{r.TypeVetement} — {r.NomCouturier} ({r.Statut})",
                    LibelleBoutonWhatsApp = "", // pas de WhatsApp auto sur retours
                    EstStagnant           = false,
                    IdsPiecesCommande     = new List<int> { r.IdPieceCommande },
                    EstAlertRetour        = true,
                    IdRetour              = r.IdRetour,
                    NumeroRetour          = r.NumeroRetour
                });
            }

            return result.OrderBy(c => c.DateEcheance).ToList();
        }

        // ── Afficher / masquer une section ────────────────────────────────
        private static void AppliquerSection(
            ItemsControl liste,
            TextBlock msgVide,
            List<AlerteCommandeVm> cartes)
        {
            if (cartes.Count == 0)
            {
                liste.Visibility   = Visibility.Collapsed;
                msgVide.Visibility = Visibility.Visible;
            }
            else
            {
                liste.ItemsSource  = cartes;
                liste.Visibility   = Visibility.Visible;
                msgVide.Visibility = Visibility.Collapsed;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // NOTIFICATIONS URGENTES (son + popup groupée)
        // ══════════════════════════════════════════════════════════════════
        private async Task VerifierNotificationsUrgentes()
        {
            // Dédoublonnage global avant le filtre NecessiteNotificationUrgente
            var toutesDedup = _alertesProduction
                .Concat(_alertesRetrait)
                .Concat(_alertesRetard)
                .GroupBy(a => a.IdPieceCommande)
                .Select(g => g.First())
                .ToList();

            // Ajouter les retours (clé = IdRetour, distinct)
            var retoursDedup = _alertesRetours
                .GroupBy(r => r.IdRetour)
                .Select(g => g.First())
                .ToList();

            var urgentes = toutesDedup.Concat(retoursDedup)
                .Where(a => a.NecessiteNotificationUrgente)
                .ToList();

            if (!urgentes.Any()) return;

            var maintenant = DateTime.Now;
            var nouvelles  = urgentes.Where(a =>
            {
                // Clé d'anti-répétition : pour les retours, on utilise un ID négatif pour éviter
                // les collisions avec les IdPieceCommande (qui sont toujours positifs).
                int cle = a.EstAlertRetour ? -(a.IdRetour) : a.IdPieceCommande;
                if (_dernieresNotifications.TryGetValue(cle, out DateTime derniere))
                    return (maintenant - derniere) >= DelaiRepetitionNotif;
                return true;
            }).ToList();

            if (!nouvelles.Any()) return;

            await Task.Run(() =>
            {
                try { SystemSounds.Exclamation.Play(); }
                catch { /* son indisponible */ }
            });

            string corps = string.Join("\n", nouvelles.Take(5).Select(a =>
                $"• {a.NomClient} — " +
                (a.EstAlertRetour ? $"[RETOUR {a.NumeroRetour}] " : "") +
                $"{a.TypeVetement} " +
                $"({(a.NiveauAlerte == "retard" ? "EN RETARD" : "Auj. " + a.HeureRendezVous)})"));
            if (nouvelles.Count > 5)
                corps += $"\n… et {nouvelles.Count - 5} autre(s)";

            var popup = new Window
            {
                Title = "⚠️ Alertes RDV urgentes",
                Width = 460, Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var sp  = new StackPanel { Margin = new Thickness(20) };
            var hdr = new TextBlock
            {
                Text = $"{nouvelles.Count} alerte(s) urgente(s) :",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var txt = new TextBlock
            {
                Text = corps, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var btnGrid = new Grid();
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition());
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var btnIgnorer = new Button
            {
                Content = "Ignorer (15 min)", Height = 36,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnIgnorer.Click += (s, e) =>
            {
                foreach (var a in nouvelles)
                {
                    int cle = a.EstAlertRetour ? -(a.IdRetour) : a.IdPieceCommande;
                    _dernieresNotifications[cle] = DateTime.Now;
                }
                popup.Close();
            };

            var btnVoir = new Button
            {
                Content = "Voir les alertes", Height = 36,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnVoir.Click += (s, e) =>
            {
                foreach (var a in nouvelles)
                {
                    int cle = a.EstAlertRetour ? -(a.IdRetour) : a.IdPieceCommande;
                    _dernieresNotifications[cle] = DateTime.Now;
                }
                popup.Close();
                if (Window.GetWindow(this) is Window main)
                {
                    main.Activate();
                    main.Focus();
                }
            };

            Grid.SetColumn(btnIgnorer, 0);
            Grid.SetColumn(btnVoir,    1);
            btnGrid.Children.Add(btnIgnorer);
            btnGrid.Children.Add(btnVoir);

            sp.Children.Add(hdr);
            sp.Children.Add(txt);
            sp.Children.Add(btnGrid);
            popup.Content = sp;

            foreach (var a in nouvelles)
            {
                int cle = a.EstAlertRetour ? -(a.IdRetour) : a.IdPieceCommande;
                _dernieresNotifications[cle] = DateTime.Now;
            }
            popup.Show();
        }

        // ══════════════════════════════════════════════════════════════════
        // BOUTON ACTUALISER
        // ══════════════════════════════════════════════════════════════════
        private async void BtnActualiser_Click(object sender, RoutedEventArgs e)
        {
            try { await ChargerDonnees(); }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // WHATSAPP — CONTEXTUEL PAR SECTION
        // ══════════════════════════════════════════════════════════════════
        private async void BtnWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not AlerteCommandeVm vm) return;

            if (string.IsNullOrWhiteSpace(vm.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone enregistré.",
                    "Numéro manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Charger la commande complète pour IWhatsAppService
            var commande = _commandeService.ObtenirParId(vm.IdCommande);
            if (commande == null)
            {
                MessageBox.Show("La commande est introuvable.",
                    "Commande manquante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (commande.Client == null)
            {
                MessageBox.Show("Le client de cette commande est introuvable.",
                    "Client manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                btn.IsEnabled = false;

                // Le libellé sur le bouton détermine le contexte d'envoi
                if (vm.LibelleBoutonWhatsApp.Contains("commande prête", StringComparison.OrdinalIgnoreCase))
                    await _whatsAppService.NotifierCommandePreteAsync(commande);
                else
                    await _whatsAppService.NotifierRappelRdvAsync(commande);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur WhatsApp : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // MODAL DÉTAILS COMMANDE
        // ══════════════════════════════════════════════════════════════════
        private void BtnVoirDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not AlerteCommandeVm vm) return;

            OuvrirModal(vm);
        }

        private void OuvrirModal(AlerteCommandeVm vm)
        {
            // Charger la commande complète (avec pièces et paiements)
            var commande = _commandeService.ObtenirParId(vm.IdCommande);
            if (commande == null)
            {
                MessageBox.Show("La commande est introuvable.",
                    "Commande manquante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // En-tête
            TxtModalSoustitre.Text  = $"Commande #{commande.IdCommande} · Échéance : {vm.DateEcheanceAffichee}";
            TxtModalNomClient.Text  = vm.NomClient;
            TxtModalTelephone.Text  = string.IsNullOrWhiteSpace(vm.Telephone)
                ? "(pas de numéro)"
                : vm.Telephone;

            // Infos financières
            TxtModalEcheance.Text    = vm.DateEcheanceAffichee;
            TxtModalResteAPayer.Text = commande.ResteAPayer > 0
                ? $"{commande.ResteAPayer:N0} FCFA"
                : "Soldée ✅";

            // Pièces avec StatutAffiche (lisible) pour les badges XAML
            var piecesPourModal = (commande.Pieces ?? new List<PieceCommande>())
                .Select(p => new PieceModalItem
                {
                    TypeVetement = p.TypeVetement,
                    Statut       = p.StatutAffiche   // "Terminée", "En cours", etc.
                })
                .ToList();

            ListeModalPieces.ItemsSource = piecesPourModal;

            ModalDetailsCommande.Visibility = Visibility.Visible;
        }

        private void BtnFermerModal_Click(object sender, RoutedEventArgs e)
        {
            ModalDetailsCommande.Visibility = Visibility.Collapsed;
        }

        // ── Petit DTO interne pour la liste de pièces dans la modal ───────
        private sealed class PieceModalItem
        {
            public string TypeVetement { get; init; } = string.Empty;
            public string Statut       { get; init; } = string.Empty;
        }

        // ══════════════════════════════════════════════════════════════════
        // ÉTAPE 7 — SECTION RISQUE DE RETARD
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Construit la section "Risque de retard" et l'insère dynamiquement
        /// dans le StackPanel du ScrollViewer principal.
        /// Appelée depuis RepartirEtAfficher().
        /// </summary>
        private void AfficherSectionRisqueRetard()
        {
            // Le StackPanel racine est le Content du ScrollViewer (Grid.Row=1)
            var scrollViewer = FindVisualChildByName<ScrollViewer>(this, null);
            StackPanel? root = null;
            if (scrollViewer?.Content is StackPanel sp)
                root = sp;

            // Mise à jour badge KPI
            if (TxtBadgeNbRisqueRetard != null)
                TxtBadgeNbRisqueRetard.Text = _alertesRisqueRetard.Count.ToString();

            if (root == null) return;

            // Supprimer l'ancienne section si elle existe
            var ancienne = root.Children.OfType<Border>()
                .FirstOrDefault(b => b.Tag?.ToString() == "SectionRisqueRetard");
            if (ancienne != null) root.Children.Remove(ancienne);

            var utilisateur = _authService.UtilisateurConnecte;
            bool peutPrevenir = utilisateur?.Role is "Secretaire" or "Boss";

            // Construire les VMs
            var vmList = new List<AlerteCommandeVm>();
            foreach (var a in _alertesRisqueRetard)
            {
                vmList.Add(new AlerteCommandeVm
                {
                    IdCommande            = a.IdCommande,
                    NomClient             = a.NomClient,
                    Telephone             = a.Telephone,
                    DateEcheance          = a.DateRendezVous,
                    HeureRdv              = a.HeureRendezVous,
                    DetailPieces          = a.EstAlertRetour
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
                });
            }

            var section = ConstruireSectionRisqueRetard(vmList);
            section.Tag = "SectionRisqueRetard";

            // Insérer en première position (plus urgent en premier)
            root.Children.Insert(0, section);
        }

        private Border ConstruireSectionRisqueRetard(List<AlerteCommandeVm> items)
        {
            var section = new Border
            {
                Background      = System.Windows.Media.Brushes.White,
                CornerRadius    = new CornerRadius(12),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xFD, 0xBA, 0x74)),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(0, 0, 0, 20)
            };

            var sp = new StackPanel();

            // En-tête orange foncé
            var header = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Padding      = new Thickness(20, 14, 20, 14),
                Background   = new SolidColorBrush(Color.FromRgb(0xC2, 0x41, 0x0C))
            };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerText = new StackPanel();
            Grid.SetColumn(headerText, 0);
            headerText.Children.Add(new TextBlock
            {
                Text       = "🟠  RDV en Risque de Retard",
                FontSize   = 14, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            });
            headerText.Children.Add(new TextBlock
            {
                Text       = "Pièces non terminées dont le RDV est imminent ou dépassé · Prévenir le client",
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFE, 0xD7, 0xAA)),
                Margin     = new Thickness(0, 3, 0, 0)
            });

            var headerBadge = new Border
            {
                Background    = new SolidColorBrush(Color.FromRgb(0x9A, 0x35, 0x12)),
                CornerRadius  = new CornerRadius(6),
                Padding       = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerBadge, 1);
            headerBadge.Child = new TextBlock
            {
                Text       = "📱 Prévenir le client",
                FontSize   = 10, FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White
            };

            headerGrid.Children.Add(headerText);
            headerGrid.Children.Add(headerBadge);
            header.Child = headerGrid;
            sp.Children.Add(header);

            if (items.Count == 0)
            {
                sp.Children.Add(new TextBlock
                {
                    Text       = "✅  Aucun RDV en risque de retard pour le moment.",
                    FontSize   = 12, FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                    Margin     = new Thickness(20, 12, 20, 16)
                });
            }
            else
            {
                foreach (var vm in items)
                {
                    var row = ConstruireLigneRisqueRetard(vm);
                    sp.Children.Add(row);
                }
            }

            section.Child = sp;
            return section;
        }

        private Border ConstruireLigneRisqueRetard(AlerteCommandeVm vm)
        {
            var row = new Border
            {
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xFF, 0xED, 0xD5)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background      = new SolidColorBrush(Color.FromRgb(0xFF, 0xF7, 0xED)),
                Padding         = new Thickness(16, 10, 16, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Colonne info
            var info = new StackPanel();
            Grid.SetColumn(info, 0);
            var ligne1 = new StackPanel { Orientation = Orientation.Horizontal };

            ligne1.Children.Add(new TextBlock { Text = "🔴 ", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            ligne1.Children.Add(new TextBlock
            {
                Text       = vm.NomClient,
                FontSize   = 13, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                VerticalAlignment = VerticalAlignment.Center
            });
            ligne1.Children.Add(new TextBlock
            {
                Text       = " · " + vm.Telephone,
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Badge "Client prévenu N fois" si applicable
            if (vm.AfficherCompteurPrevenu)
            {
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding      = new Thickness(6, 2, 6, 2),
                    Margin       = new Thickness(8, 0, 0, 0),
                    Background   = new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7))
                };
                badge.Child = new TextBlock
                {
                    Text       = vm.CompteurPrevenu,
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46))
                };
                ligne1.Children.Add(badge);
            }

            info.Children.Add(ligne1);
            info.Children.Add(new TextBlock
            {
                Text       = vm.DetailPieces,
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                Margin     = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            // Colonne date
            var datePanel = new StackPanel
            {
                VerticalAlignment     = VerticalAlignment.Center,
                Margin                = new Thickness(16, 0, 16, 0)
            };
            Grid.SetColumn(datePanel, 1);
            datePanel.Children.Add(new TextBlock
            {
                Text       = vm.DateCourte,
                FontSize   = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            datePanel.Children.Add(new TextBlock
            {
                Text       = vm.HeureRdv,
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // Colonne boutons
            var btns = new StackPanel
            {
                Orientation           = Orientation.Horizontal,
                VerticalAlignment     = VerticalAlignment.Center
            };
            Grid.SetColumn(btns, 2);

            if (vm.AfficherPrevenirClient)
            {
                var btnPrevenir = ConstruireBouton(
                    "📱 Prévenir",
                    Color.FromRgb(0xEA, 0x58, 0x0C),
                    vm,
                    BtnPrevenirClient_Click);
                btnPrevenir.Margin = new Thickness(0, 0, 8, 0);
                btns.Children.Add(btnPrevenir);
            }

            var btnDetails = ConstruireBouton(
                "Voir",
                Color.FromRgb(0x1E, 0x40, 0xAF),
                vm,
                BtnVoirDetailsRetard_Click);
            btns.Children.Add(btnDetails);

            grid.Children.Add(info);
            grid.Children.Add(datePanel);
            grid.Children.Add(btns);
            row.Child = grid;
            return row;
        }

        private static Button ConstruireBouton(string texte, Color couleur, object tag, RoutedEventHandler click)
        {
            var btn = new Button
            {
                Content         = texte,
                Height          = 32,
                Padding         = new Thickness(12, 0, 12, 0),
                FontSize        = 11,
                FontWeight      = FontWeights.SemiBold,
                Foreground      = System.Windows.Media.Brushes.White,
                Background      = new SolidColorBrush(couleur),
                BorderThickness = new Thickness(0),
                Cursor          = System.Windows.Input.Cursors.Hand,
                Tag             = tag
            };
            btn.Click += click;
            var template = new ControlTemplate(typeof(Button));
            var border   = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty,   new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty,      new TemplateBindingExtension(Button.PaddingProperty));
            border.SetValue(Border.HeightProperty,       new TemplateBindingExtension(Button.HeightProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
            border.AppendChild(cp);
            template.VisualTree = border;
            btn.Template = template;
            return btn;
        }

        /// <summary>Recherche récursive d'un enfant visuel par type et nom optionnel.</summary>
        private static T? FindVisualChildByName<T>(DependencyObject parent, string? name) where T : FrameworkElement
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t && (name == null || t.Name == name)) return t;
                var found = FindVisualChildByName<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ──────────────────────────────────────────────────────────────────
        // Bouton "Prévenir le client"
        // ──────────────────────────────────────────────────────────────────
        private async void BtnPrevenirClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AlerteCommandeVm vm) return;

            btn.IsEnabled = false;
            try
            {
                var brouillon = await _retardAlerteService.PreparerMessageExcuseAsync(
                    vm.TypeCibleRetard,
                    vm.IdCibleRetard,
                    vm.DateEcheance);

                _brouillonExcuseCourant = brouillon;

                // Alimenter la modal
                TxtModalExcuseSousTitre.Text   = $"{vm.NomClient} — RDV le {vm.DateCourte} à {vm.HeureRdv}";
                TxtModalExcuseClient.Text      = vm.NomClient;
                TxtModalExcuseTel.Text         = string.IsNullOrWhiteSpace(brouillon.Telephone)
                    ? "(pas de numéro enregistré)"
                    : brouillon.Telephone;
                TxtModalExcuseNouvelleDate.Text = brouillon.NouvelleDateProposee.ToString("dddd dd MMMM yyyy",
                    new System.Globalization.CultureInfo("fr-FR"));

                // Compteur
                TxtModalExcuseCompteur.Visibility = brouillon.NbFoisPrevenu > 0
                    ? Visibility.Visible : Visibility.Collapsed;
                TxtModalExcuseCompteur.Text = $"⚠️  Client déjà prévenu {brouillon.NbFoisPrevenu} fois";

                // Avertissement numéro
                bool numOk = _retardAlerteService.NumeroClientValide(brouillon.Telephone);
                PanelAvertissementNumero.Visibility = numOk ? Visibility.Collapsed : Visibility.Visible;
                BtnEnvoyerWhatsApp.IsEnabled        = numOk;

                TxtModalExcuseMessage.Text = brouillon.Message;
                TxtModalExcuseMsg.Text     = "";
                ModalMessageExcuse.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la préparation du message : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { btn.IsEnabled = true; }
        }

        // ──────────────────────────────────────────────────────────────────
        // Bouton "Voir" dans la section risque de retard
        // ──────────────────────────────────────────────────────────────────
        private void BtnVoirDetailsRetard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AlerteCommandeVm vm) return;
            OuvrirModal(vm);
        }

        // ──────────────────────────────────────────────────────────────────
        // Fermer la modal message d'excuse
        // ──────────────────────────────────────────────────────────────────
        private void BtnFermerModalExcuse_Click(object sender, RoutedEventArgs e)
        {
            ModalMessageExcuse.Visibility = Visibility.Collapsed;
            _brouillonExcuseCourant = null;
        }

        // ──────────────────────────────────────────────────────────────────
        // Envoi WhatsApp + proposition de report
        // ──────────────────────────────────────────────────────────────────
        private async void BtnEnvoyerWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (_brouillonExcuseCourant == null) return;
            var utilisateur = _authService.UtilisateurConnecte;
            if (utilisateur == null)
            {
                MessageBox.Show("Vous devez être connecté pour effectuer cette action.",
                    "Non connecté", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnEnvoyerWhatsApp.IsEnabled = false;
            try
            {
                string message = TxtModalExcuseMessage.Text.Trim();
                if (string.IsNullOrWhiteSpace(message))
                { TxtModalExcuseMsg.Text = "Le message ne peut pas être vide.";
                  TxtModalExcuseMsg.Foreground = System.Windows.Media.Brushes.Red; return; }

                // 1. Ouvrir WhatsApp
                _whatsAppService.OuvrirConversation(_brouillonExcuseCourant.Telephone, message);

                // 2. Enregistrer la prévention (anti-doublon)
                await _retardAlerteService.EnregistrerPreventionAsync(
                    _brouillonExcuseCourant.TypeCible,
                    _brouillonExcuseCourant.IdCible,
                    _brouillonExcuseCourant.DateRdvActuelle,
                    _brouillonExcuseCourant.IdClient,
                    utilisateur.IdEmploye,
                    $"{utilisateur.Prenom} {utilisateur.Nom}");

                TxtModalExcuseMsg.Text       = "✅  Message ouvert dans WhatsApp. Envoyez-le depuis l'application.";
                TxtModalExcuseMsg.Foreground = System.Windows.Media.Brushes.Green;

                // 3. Proposer le report (confirmation obligatoire)
                var nouvelleDate = _brouillonExcuseCourant.NouvelleDateProposee.ToString("dd/MM/yyyy");
                var rep = MessageBox.Show(
                    $"Souhaitez-vous REPORTER le RDV au {nouvelleDate} ?\n\n" +
                    "⚠️  Cette action déplace la date de rendez-vous.\n" +
                    "Elle ne modifie ni le statut, ni le paiement, ni la commission.",
                    "Reporter le RDV ?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (rep == MessageBoxResult.Yes)
                {
                    await _retardAlerteService.ReporterRendezVousAsync(
                        _brouillonExcuseCourant.TypeCible,
                        _brouillonExcuseCourant.IdCible,
                        _brouillonExcuseCourant.DateRdvActuelle,
                        utilisateur.IdEmploye,
                        $"{utilisateur.Prenom} {utilisateur.Nom}");

                    TxtModalExcuseMsg.Text = $"✅  RDV reporté au {nouvelleDate}. Message envoyé.";
                    TxtModalExcuseMsg.Foreground = System.Windows.Media.Brushes.Green;
                }

                // 4. Rafraîchir les alertes (l'alerte traitée disparaît)
                await ChargerDonnees();
            }
            catch (Exception ex)
            {
                TxtModalExcuseMsg.Text = "Erreur : " + ex.Message;
                TxtModalExcuseMsg.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally { BtnEnvoyerWhatsApp.IsEnabled = true; }
        }
    }
}
