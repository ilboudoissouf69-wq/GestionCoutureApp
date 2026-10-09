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
        private readonly IAlerteService   _alerteService;
        private readonly IWhatsAppService _whatsAppService;
        private readonly ICommandeService _commandeService;

        // ── Données brutes chargées depuis les services ───────────────────
        private List<AlerteRendezVous> _alertesProduction = new();
        private List<AlerteRendezVous> _alertesRetrait    = new();
        private List<AlerteRendezVous> _alertesRetard     = new();

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

            _alerteService   = App.Services.GetRequiredService<IAlerteService>();
            _whatsAppService = App.Services.GetRequiredService<IWhatsAppService>();
            _commandeService = App.Services.GetRequiredService<ICommandeService>();

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

            // 1. Union dédoublonnée (IdPieceCommande) — priorité : production
            //    d'abord, retrait ensuite, retards en dernier pour les pièces
            //    qui seraient dans plusieurs listes.
            var toutes = _alertesProduction
                .Concat(_alertesRetrait)
                .Concat(_alertesRetard)
                .GroupBy(a => a.IdPieceCommande)
                .Select(g => g.First())
                .ToList();

            // 2. Affectation à une section par priorité
            var prets       = new List<AlerteRendezVous>();
            var retards     = new List<AlerteRendezVous>();
            var auJourdhui  = new List<AlerteRendezVous>();
            var imminentes  = new List<AlerteRendezVous>();

            foreach (var a in toutes)
            {
                // Priorité 1 — Prêtes : pièce Terminee (peu importe le RDV)
                if (a.PiecePrete)
                {
                    prets.Add(a);
                    continue;
                }

                // Priorité 2 — En retard : RDV dépassé et non terminée
                if (a.NiveauAlerte == "retard")
                {
                    retards.Add(a);
                    continue;
                }

                // Priorité 3 — Aujourd'hui : RDV date == aujourd'hui, non terminée
                if (a.DateRendezVous.Date == maintenant.Date)
                {
                    auJourdhui.Add(a);
                    continue;
                }

                // Priorité 4 — À venir 7 jours : RDV dans la fenêtre > aujourd'hui
                //   et <= +7 jours. Exception : colis stagnant (TypeAlerte ==
                //   "PasEncorePriseEnCharge") dont le RDV dépasse la fenêtre →
                //   affiché quand même pour ne pas être perdu.
                bool rdvSemaine = a.DateRendezVous.Date > maintenant.Date
                               && a.DateRendezVous.Date <= maintenant.AddDays(7).Date;
                bool stagnantHorsFenetre = a.TypeAlerte == "PasEncorePriseEnCharge"
                                        && a.DateRendezVous.Date > maintenant.AddDays(7).Date;

                if (rdvSemaine || stagnantHorsFenetre)
                {
                    imminentes.Add(a);
                }
            }

            // 3. Tris par section
            prets      = prets
                .OrderByDescending(a => a.ProposerContactWhatsApp)
                .ThenBy(a => a.DateRendezVous)
                .ToList();
            retards    = retards.OrderBy(a => a.DateRendezVous).ToList();
            auJourdhui = auJourdhui.OrderBy(a => a.DateRendezVous).ToList();
            imminentes = imminentes.OrderBy(a => a.DateRendezVous).ToList();

            // 4. Groupement en cartes
            var cartesPrets      = Grouper(prets,      "💬 Prévenir : commande prête");
            var cartesRetards    = Grouper(retards,    "💬 Informer Client");
            var cartesAujourdhui = Grouper(auJourdhui, "💬 Rappel RDV");
            var cartesImminentes = Grouper(imminentes, ""); // pas de bouton WhatsApp

            // 5. Mise à jour badges KPI
            TxtBadgeNbPrets.Text       = cartesPrets.Count.ToString();
            TxtBadgeNbRetards.Text     = cartesRetards.Count.ToString();
            TxtBadgeNbAujourdhui.Text  = cartesAujourdhui.Count.ToString();
            TxtBadgeNbImminentes.Text  = cartesImminentes.Count.ToString();

            // 6. Alimenter les sections
            AppliquerSection(ListePrets,      TxtZeroPrets,      cartesPrets);
            AppliquerSection(ListeRetards,    TxtZeroRetard,     cartesRetards);
            AppliquerSection(ListeAujourdhui, TxtZeroAujourdhui, cartesAujourdhui);
            AppliquerSection(ListeImminentes, TxtZeroImminentes, cartesImminentes);

            // 7. Badge menu principal (en pièces, retards + aujourd'hui)
            int totalBadge = toutes.Count(a => a.NiveauAlerte is "retard" or "jourbj");
            if (Window.GetWindow(this) is MainWindow mw)
                mw.MettreAJourBadgeAlertes(totalBadge);
        }

        // ── Groupement des AlerteRendezVous en AlerteCommandeVm ──────────
        private static List<AlerteCommandeVm> Grouper(
            List<AlerteRendezVous> pieces,
            string libelleBoutonWhatsApp)
        {
            return pieces
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
                        IdCommande             = g.Key,
                        NomClient              = plusUrgente.NomClient,
                        Telephone              = plusUrgente.Telephone,
                        DateEcheance           = plusUrgente.DateRendezVous,
                        HeureRdv               = plusUrgente.HeureRendezVous,
                        DetailPieces           = detail,
                        LibelleBoutonWhatsApp  = libelleBoutonWhatsApp,
                        EstStagnant            = stagnant,
                        IdsPiecesCommande      = ordered.Select(a => a.IdPieceCommande).ToList()
                    };
                })
                .ToList();
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

            var urgentes = toutesDedup
                .Where(a => a.NecessiteNotificationUrgente)
                .ToList();

            if (!urgentes.Any()) return;

            var maintenant = DateTime.Now;
            var nouvelles  = urgentes.Where(a =>
            {
                if (_dernieresNotifications.TryGetValue(a.IdPieceCommande, out DateTime derniere))
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
                $"• {a.NomClient} — {a.TypeVetement} " +
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
                    _dernieresNotifications[a.IdPieceCommande] = DateTime.Now;
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
                    _dernieresNotifications[a.IdPieceCommande] = DateTime.Now;
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
                _dernieresNotifications[a.IdPieceCommande] = DateTime.Now;

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
    }
}
