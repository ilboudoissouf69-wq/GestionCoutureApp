using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Écran de suivi des statuts — une ligne par pièce de commande.
    /// Accessible à Boss et Secrétaire (consultation + contact client WhatsApp).
    /// </summary>
    public partial class StatutView : Page
    {
        private readonly ICommandeService _commandeService;
        private readonly IWhatsAppService _whatsApp;

        // ── Pagination ────────────────────────────────────────────────────
        private const int PAGE_SIZE = 20;
        private int _currentPage = 1;
        private string? _filtreStatut = null;   // null = tous
        private string _recherche = string.Empty;

        // Référence au bouton de filtre actif (pour l'indicateur visuel)
        private Button? _btnFiltreActif;

        public StatutView()
        {
            InitializeComponent();

            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _whatsApp        = App.Services.GetRequiredService<IWhatsAppService>();

            Loaded += async (s, e) =>
            {
                // Activer le bouton "Tous" par défaut
                SetFiltreActif(BtnFiltreAll);
                await ChargerPieces();
            };
        }

        // ==================================================================
        // Chargement / rechargement de la liste
        // ==================================================================
        private async Task ChargerPieces()
        {
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                GridPieces.IsEnabled = false;

                var result = await _commandeService.ObtenirPagePiecesAsync(
                    _filtreStatut, _currentPage, PAGE_SIZE,
                    string.IsNullOrWhiteSpace(_recherche) ? null : _recherche);

                GridPieces.ItemsSource = result.Items;

                // Pagination
                BtnPagePrecedente.IsEnabled = result.HasPrevious;
                BtnPageSuivante.IsEnabled   = result.HasNext;

                int start = result.TotalCount == 0 ? 0 : (result.Page - 1) * result.PageSize + 1;
                int end   = Math.Min(result.Page * result.PageSize, result.TotalCount);
                string filtreLabel = _filtreStatut switch
                {
                    "Retard"   => "retard",
                    "A faire"  => "à faire",
                    "En cours" => "en cours",
                    "Terminee" => "terminée",
                    "Livree"   => "livrée",
                    _          => "total"
                };
                TxtPaginationInfo.Text = result.TotalCount == 0
                    ? "Aucune pièce"
                    : $"{start}-{end} / {result.TotalCount} pièce(s) {filtreLabel}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du chargement : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                GridPieces.IsEnabled = true;
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
            await ChargerPieces();
        }

        /// <summary>
        /// Marque visuellement le bouton de filtre actif avec un contour rouge/foncé
        /// et remet les autres à leur apparence normale.
        /// </summary>
        private void SetFiltreActif(Button bouton)
        {
            // Retirer le marqueur de l'ancien bouton actif
            if (_btnFiltreActif != null)
                _btnFiltreActif.BorderThickness = new Thickness(1.5);

            // Marquer le nouveau bouton actif avec un contour épais
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
            await ChargerPieces();
        }

        // ==================================================================
        // Pagination
        // ==================================================================
        private async void BtnPagePrecedente_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await ChargerPieces();
            }
        }

        private async void BtnPageSuivante_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await ChargerPieces();
        }

        // ==================================================================
        // Bouton WhatsApp par ligne
        // Comportement contextuel selon statut et date :
        //   - Terminée ou Livrée            → NotifierCommandePreteAsync
        //   - À faire / En cours en retard  → NotifierRappelRdvAsync
        //   - Autres cas                    → ContacterClientAsync
        // ==================================================================
        private async void BtnWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not PieceCommande piece) return;

            var commande = piece.Commande;
            if (commande == null)
            {
                MessageBox.Show("Impossible de récupérer les informations de la commande.",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                bool estEnRetard = commande.DateFin < DateTime.Now
                    && (piece.Statut == "A faire" || piece.Statut == "En cours");

                bool estPrete = piece.Statut == "Terminee" || piece.Statut == "Livree";

                bool rdvProche = !estEnRetard
                    && (piece.Statut == "A faire" || piece.Statut == "En cours")
                    && commande.DateFin <= DateTime.Now.AddDays(2);

                if (estPrete)
                {
                    await _whatsApp.NotifierCommandePreteAsync(commande);
                }
                else if (estEnRetard || rdvProche)
                {
                    await _whatsApp.NotifierRappelRdvAsync(commande);
                }
                else
                {
                    await _whatsApp.ContacterClientAsync(commande.Client);
                }
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
