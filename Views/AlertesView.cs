using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class AlertesView : Page
    {
        private readonly IAlerteService      _alerteService;
        private readonly IWhatsAppService    _whatsAppService;
        private readonly IParametresService  _parametresService;
        private readonly ICommandeService    _commandeService;

        private List<AlerteRendezVous> _alertesProduction = new();
        private List<AlerteRendezVous> _alertesRetrait    = new();

        // TÂCHE 7 : Timer de rafraîchissement automatique toutes les 60 secondes
        private readonly DispatcherTimer _timer;

        // TÂCHE 7 : Anti-répétition — clé = IdPieceCommande, valeur = heure de la dernière notif
        // Une pièce ne sera re-notifiée qu'après le délai configuré (par défaut 15 min).
        private readonly Dictionary<int, DateTime> _dernieresNotifications = new();
        private static readonly TimeSpan DelaiRepetitionNotif = TimeSpan.FromMinutes(15);

        public AlertesView()
        {
            InitializeComponent();

            _alerteService     = App.Services.GetRequiredService<IAlerteService>();
            _whatsAppService   = App.Services.GetRequiredService<IWhatsAppService>();
            _parametresService = App.Services.GetRequiredService<IParametresService>();
            _commandeService   = App.Services.GetRequiredService<ICommandeService>();

            // TÂCHE 7 : DispatcherTimer 60 secondes
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _timer.Tick += async (s, e) => await ChargerDonnees();

            Loaded += async (s, e) =>
            {
                await ChargerDonnees();
                _timer.Start();

                // Abonnement CommandeChanged pour rafraîchissement immédiat
                _commandeService.CommandeChanged += OnCommandeChanged;
            };

            Unloaded += (s, e) =>
            {
                _timer.Stop();
                _commandeService.CommandeChanged -= OnCommandeChanged;
            };
        }

        // ==================================================================
        // Rafraîchissement immédiat sur changement de commande
        // ==================================================================
        private async void OnCommandeChanged(object? sender, CommandeChangedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () => await ChargerDonnees());
        }

        // ==================================================================
        // Chargement
        // ==================================================================
        private async Task ChargerDonnees()
        {
            try
            {
                _alertesProduction = await _alerteService.ObtenirAlertesActuelles();
                _alertesRetrait    = await _alerteService.ObtenirRendezVousSemaine();

                MettreAJourBadges();
                AfficherProduction();
                AfficherRetrait();

                // TÂCHE 7 : notifications sonores et popups pour les alertes urgentes
                await VerifierNotificationsUrgentes();
            }
            catch (Exception ex)
            {
                // Ne pas bloquer le timer sur erreur — log silencieux
                System.Diagnostics.Debug.WriteLine($"[AlertesView] Erreur chargement : {ex.Message}");
            }
        }

        // ==================================================================
        // TÂCHE 7 : Notifications urgentes (son + popup) — async, non bloquant
        // ==================================================================
        private async Task VerifierNotificationsUrgentes()
        {
            var urgentes = _alertesProduction
                .Concat(_alertesRetrait)
                .Where(a => a.NecessiteNotificationUrgente)
                .ToList();

            if (!urgentes.Any()) return;

            // Filtrer celles déjà notifiées récemment
            var maintenant = DateTime.Now;
            var nouvelles = urgentes.Where(a =>
            {
                if (_dernieresNotifications.TryGetValue(a.IdPieceCommande, out DateTime derniere))
                    return (maintenant - derniere) >= DelaiRepetitionNotif;
                return true;
            }).ToList();

            if (!nouvelles.Any()) return;

            // Son Windows (SystemSounds.Exclamation — non bloquant)
            await Task.Run(() =>
            {
                try { SystemSounds.Exclamation.Play(); }
                catch { /* son indisponible → silencieux */ }
            });

            // Popup (une seule popup groupée, non bloquante)
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
                // Marquer comme notifiées
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
                // Remonter la fenêtre principale au premier plan si possible
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

            // Marquer les alertes notifiées AVANT d'ouvrir la popup
            foreach (var a in nouvelles)
                _dernieresNotifications[a.IdPieceCommande] = DateTime.Now;

            // Non bloquant — ShowDialog() bloquerait le Dispatcher
            popup.Show();
        }

        // ==================================================================
        // Badges KPI
        // ==================================================================
        private void MettreAJourBadges()
        {
            TxtNbProdUrgent.Text    = _alertesProduction.Count(a => a.EstUrgent).ToString();
            TxtNbProdSurveiller.Text = _alertesProduction.Count(a => !a.EstUrgent).ToString();
            TxtNbPrets.Text          = _alertesRetrait.Count(a => a.PiecePrete).ToString();
            TxtNbRdvAVenir.Text      = _alertesRetrait.Count(a => !a.PiecePrete).ToString();

            // TÂCHE 7 : Badge total dans le bouton menu (si MainWindow l'expose)
            int totalUrgent = _alertesProduction.Count(a => a.NiveauAlerte is "retard" or "jourbj")
                            + _alertesRetrait.Count(a => a.NiveauAlerte is "retard" or "jourbj");

            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.MettreAJourBadgeAlertes(totalUrgent);
            }
        }

        // ==================================================================
        // Section 1 — Production (ItemsControl)
        // ==================================================================
        private void AfficherProduction()
        {
            if (_alertesProduction.Count == 0)
            {
                ListeProduction.Visibility   = Visibility.Collapsed;
                TxtVideProduction.Visibility = Visibility.Visible;
            }
            else
            {
                ListeProduction.Visibility   = Visibility.Visible;
                TxtVideProduction.Visibility = Visibility.Collapsed;
                ListeProduction.ItemsSource  = _alertesProduction
                    .OrderByDescending(a => a.NiveauAlerte == "retard")
                    .ThenByDescending(a => a.NiveauAlerte == "jourbj")
                    .ThenByDescending(a => a.NiveauAlerte == "demain")
                    .ThenBy(a => a.DateRendezVous)
                    .ToList();
            }
        }

        // ==================================================================
        // Section 2 — Retrait (ItemsControl)
        // ==================================================================
        private void AfficherRetrait()
        {
            if (_alertesRetrait.Count == 0)
            {
                ListeRetrait.Visibility   = Visibility.Collapsed;
                TxtVideRetrait.Visibility = Visibility.Visible;
            }
            else
            {
                ListeRetrait.Visibility   = Visibility.Visible;
                TxtVideRetrait.Visibility = Visibility.Collapsed;
                ListeRetrait.ItemsSource  = _alertesRetrait
                    .OrderByDescending(a => a.NiveauAlerte == "retard")
                    .ThenBy(a => a.DateRendezVous)
                    .ToList();
            }
        }

        // ==================================================================
        // Actualiser
        // ==================================================================
        private async void BtnActualiser_Click(object sender, RoutedEventArgs e)
        {
            try { await ChargerDonnees(); }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================================================================
        // WhatsApp — bouton rappel depuis la liste (retard / RDV proche)
        // ==================================================================
        private async void BtnContacterWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not AlerteRendezVous alerte) return;

            if (string.IsNullOrWhiteSpace(alerte.Telephone))
            {
                MessageBox.Show("Ce client n'a pas de numéro de téléphone enregistré.",
                    "Numéro manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string modele    = await _parametresService.ObtenirMsgCommandePrete();
                string nomAtelier = await _parametresService.ObtenirNomAtelier();

                string message = modele
                    .Replace("{Nom}",      $"*{alerte.NomClient}*")
                    .Replace("{Commande}", alerte.IdCommande.ToString())
                    .Replace("{Pieces}",   alerte.TypeVetement)
                    .Replace("{Reste}",    "—")
                    .Replace("{Atelier}",  nomAtelier)
                    .Replace("{Date}",     alerte.DateRendezVous.ToString("dd/MM/yyyy"))
                    .Replace("{Heure}",    alerte.HeureRendezVous);

                _whatsAppService.OuvrirConversation(alerte.Telephone, message);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur WhatsApp : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
