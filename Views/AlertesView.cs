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
        // Étape 6 : retours actifs avec RDV
        private List<AlerteRendezVous> _alertesRetours    = new();

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
                // Étape 6 : retours actifs
                try { _alertesRetours = await _alerteService.ObtenirAlertesRetours(); }
                catch { _alertesRetours = new(); }

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
    }
}
