using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Écran de suivi des statuts — une ligne par COMMANDE, dépliable par pièce.
    /// Boss/Secrétaire : lecture + modification de statut.
    /// Couturier : consultation uniquement.
    /// </summary>
    public partial class StatutView : Page
    {
        private readonly ICommandeService _commandeService;
        private readonly IWhatsAppService _whatsApp;
        private readonly IAuthService     _authService;

        // ── Pagination ────────────────────────────────────────────────────
        private const int PAGE_SIZE = 20;
        private int _currentPage = 1;
        private string? _filtreStatut = null;   // null = tous
        private string _recherche = string.Empty;

        // Rôle de l'utilisateur connecté
        private readonly string _role;

        // Référence au bouton de filtre actif (pour l'indicateur visuel)
        private Button? _btnFiltreActif;

        // Verrou pour éviter les boucles dans CmbStatutPiece_SelectionChanged
        private bool _suppressionChangementStatut = false;

        public StatutView()
        {
            InitializeComponent();

            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _whatsApp        = App.Services.GetRequiredService<IWhatsAppService>();
            _authService     = App.Services.GetRequiredService<IAuthService>();

            _role = _authService.UtilisateurConnecte?.Role ?? "";

            Loaded += async (s, e) =>
            {
                SetFiltreActif(BtnFiltreAll);
                await ChargerCommandes();
            };
        }

        // ── Helper opérateur connecté ──────────────────────────────────────
        private (int id, string nom) OperateurConnecte()
        {
            var op = _authService.UtilisateurConnecte;
            return op != null
                ? (op.IdEmploye, $"{op.Prenom} {op.Nom}".Trim())
                : (0, string.Empty);
        }

        // ==================================================================
        // Chargement / rechargement de la liste par commande
        // ==================================================================
        private async Task ChargerCommandes()
        {
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                GridCommandes.IsEnabled = false;

                var result = await _commandeService.ObtenirPageCommandesStatutAsync(
                    _filtreStatut, _currentPage, PAGE_SIZE,
                    string.IsNullOrWhiteSpace(_recherche) ? null : _recherche);

                // Désactiver les boutons "Passer à…" pour Couturier (lecture seule)
                _suppressionChangementStatut = true;
                GridCommandes.ItemsSource = result.Items;
                _suppressionChangementStatut = false;

                // Pagination
                BtnPagePrecedente.IsEnabled = result.HasPrevious;
                BtnPageSuivante.IsEnabled   = result.HasNext;

                int start = result.TotalCount == 0 ? 0 : (result.Page - 1) * result.PageSize + 1;
                int end   = Math.Min(result.Page * result.PageSize, result.TotalCount);
                string filtreLabel = _filtreStatut switch
                {
                    "Retard"   => "en retard",
                    "A faire"  => "à faire",
                    "En cours" => "en cours",
                    "Terminee" => "terminées",
                    "Livree"   => "livrées",
                    _          => "total"
                };
                TxtPaginationInfo.Text = result.TotalCount == 0
                    ? "Aucune commande"
                    : $"{start}-{end} / {result.TotalCount} commande(s) {filtreLabel}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du chargement : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                GridCommandes.IsEnabled = true;
            }
        }

        // ==================================================================
        // Filtres rapides
        // ==================================================================
        private async void BtnFiltre_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            string tag = btn.Tag?.ToString() ?? "";
            _filtreStatut = string.IsNullOrEmpty(tag) ? null : tag;
            _currentPage  = 1;

            SetFiltreActif(btn);
            await ChargerCommandes();
        }

        private void SetFiltreActif(Button bouton)
        {
            if (_btnFiltreActif != null)
                _btnFiltreActif.BorderThickness = new Thickness(1.5);
            bouton.BorderThickness = new Thickness(2.5);
            _btnFiltreActif = bouton;
        }

        // ==================================================================
        // Recherche
        // ==================================================================
        private async void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            _recherche   = TxtRecherche.Text.Trim();
            _currentPage = 1;
            await ChargerCommandes();
        }

        // ==================================================================
        // Pagination
        // ==================================================================
        private async void BtnPagePrecedente_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1) { _currentPage--; await ChargerCommandes(); }
        }

        private async void BtnPageSuivante_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await ChargerCommandes();
        }

        // ==================================================================
        // "Passer à…" — menu contextuel pour forcer le statut d'une commande
        // Accessible Boss et Secrétaire uniquement.
        // ==================================================================
        private void BtnPasserA_Click(object sender, RoutedEventArgs e)
        {
            // Couturier : pas d'action
            if (_role == "Couturier")
            {
                MessageBox.Show("La modification de statut est réservée au Boss et à la Secrétaire.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (sender is not Button btn) return;
            if (!int.TryParse(btn.Tag?.ToString(), out int idCommande)) return;

            // Construire le menu contextuel dynamiquement
            var menu = new ContextMenu();

            void AjouterItem(string libelle, string statut)
            {
                var item = new MenuItem { Header = libelle };
                item.Click += async (s, ev) => await ForcerStatutCommande(idCommande, statut);
                menu.Items.Add(item);
            }

            AjouterItem("⬜  À faire",  "A faire");
            AjouterItem("🟡  En cours",  "En cours");
            AjouterItem("🟢  Terminée",  "Terminee");
            AjouterItem("🔵  Livrée",    "Livree");

            btn.ContextMenu = menu;
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }

        private async Task ForcerStatutCommande(int idCommande, string statut)
        {
            var (idOp, nomOp) = OperateurConnecte();

            bool ok = await AvecControleLivraison(async motif =>
            {
                await Task.Run(() =>
                    _commandeService.ForcerStatutToutesPieces(idCommande, statut, idOp, nomOp, motif));
            });

            if (ok) await ChargerCommandes();
        }

        // ==================================================================
        // Changement de statut pièce par pièce (ComboBox dans le détail)
        // Accessible Boss et Secrétaire uniquement.
        // ==================================================================
        private async void CmbStatutPiece_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressionChangementStatut) return;
            if (sender is not ComboBox cmb) return;
            if (cmb.SelectedItem is not ComboBoxItem item) return;

            // Couturier : lecture seule
            if (_role == "Couturier")
            {
                MessageBox.Show("La modification de statut est réservée au Boss et à la Secrétaire.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(cmb.Tag?.ToString(), out int idPiece)) return;
            string nouveauStatut = item.Tag?.ToString() ?? "";
            if (string.IsNullOrEmpty(nouveauStatut)) return;

            var (idOp, nomOp) = OperateurConnecte();

            bool ok = await AvecControleLivraison(async motif =>
            {
                await Task.Run(() =>
                    _commandeService.ChangerStatutPiece(idPiece, nouveauStatut, idOp, nomOp, motif));
            });

            if (ok) await ChargerCommandes();
        }

        // ==================================================================
        // Gestion de la livraison non soldée (même logique que CommandesView)
        // ==================================================================
        private async Task<bool> AvecControleLivraison(Func<string?, Task> action)
        {
            try
            {
                await action(null);
                return true;
            }
            catch (LivraisonNonSoldeeException ex) when (ex.PeutForcer)
            {
                var rep = MessageBox.Show(
                    ex.Message + "\n\nAutoriser quand même la livraison ?",
                    "Livraison non soldée",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (rep != MessageBoxResult.Yes) return false;

                string? motif = DemanderMotif("Motif de la livraison non soldée");
                if (string.IsNullOrWhiteSpace(motif)) return false;

                await action(motif);
                return true;
            }
            catch (LivraisonNonSoldeeException ex) when (!ex.PeutForcer)
            {
                MessageBox.Show(ex.Message,
                    "Livraison impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Opération impossible",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(ex.Message, "Accès refusé",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private string? DemanderMotif(string titre)
        {
            string? resultat = null;
            var dialog = new Window
            {
                Title = titre,
                Width = 420,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var sp = new StackPanel { Margin = new Thickness(20) };
            var lbl = new TextBlock { Text = "Motif (obligatoire) :", Margin = new Thickness(0, 0, 0, 8) };
            var txt = new TextBox { Height = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
            var btnOk = new Button
            {
                Content = "Confirmer", Width = 110, Height = 32,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnOk.Click += (s, e) => { resultat = txt.Text.Trim(); dialog.Close(); };

            sp.Children.Add(lbl);
            sp.Children.Add(txt);
            sp.Children.Add(btnOk);
            dialog.Content = sp;
            dialog.ShowDialog();
            return resultat;
        }

        // ==================================================================
        // Bouton WhatsApp — même logique contextuelle qu'avant
        // ==================================================================
        private async void BtnWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not Commande commande) return;

            if (commande.Client == null)
            {
                MessageBox.Show("Le client de cette commande est introuvable.",
                    "Client manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                btn.IsEnabled = false;

                bool estTermineee  = commande.StatutGlobal is "Terminee" or "Terminee partiellement";
                bool estLivree     = commande.StatutGlobal is "Livree"   or "Livree partiellement";
                bool estEnRetard   = commande.EstEnRetard;
                bool rdvProche     = !estEnRetard
                    && commande.DateFin <= DateTime.Now.AddDays(2)
                    && commande.StatutGlobal is "A faire" or "En cours";

                if (estTermineee || estLivree)
                    await _whatsApp.NotifierCommandePreteAsync(commande);
                else if (estEnRetard || rdvProche)
                    await _whatsApp.NotifierRappelRdvAsync(commande);
                else
                    await _whatsApp.ContacterClientAsync(commande.Client);
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
    }
}
