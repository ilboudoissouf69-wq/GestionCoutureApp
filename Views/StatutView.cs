using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Écran Kanban de suivi des pièces — 4 colonnes (A faire / En cours / Terminee / Livree).
    /// Boss/Secrétaire : lecture + modification de statut.
    /// Couturier : consultation uniquement.
    ///
    /// NOTE : ChargerCommandes() charge maintenant des PIÈCES (PieceCommande),
    /// non plus des Commande. Le nom est conservé pour la cohérence avec l'abonnement
    /// CommandeChanged existant.
    /// </summary>
    public partial class StatutView : Page
    {
        // ── Services ────────────────────────────────────────────────────
        private readonly ICommandeService _commandeService;
        private readonly IWhatsAppService _whatsApp;
        private readonly IAuthService     _authService;

        // ── Pagination ────────────────────────────────────────────────
        private const int PAGE_SIZE = 20;
        private int _currentPage = 1;
        private string? _filtreStatut = null;   // null = tous
        private string _recherche = string.Empty;

        // Rôle de l'utilisateur connecté
        private readonly string _role;

        // Référence au bouton de filtre actif (indicateur visuel)
        private Button? _btnFiltreActif;

        // ── Filtre couturier (côté client) ─────────────────────────────
        private int? _filtreCouturierId = null;   // null = tous
        private bool _suppressCmbCouturier = false;

        // ── Modal livraison non soldée ─────────────────────────────────
        // Action en attente d'un motif (branche PeutForcer == true)
        private Func<string?, Task>? _actionEnAttente = null;

        // ── Données Kanban ─────────────────────────────────────────────
        // Les colonnes ; affectées à ItemsSource des ItemsControl dans le XAML
        private List<PieceCommande> _itemsAfaire   = new();
        private List<PieceCommande> _itemsEnCours  = new();
        private List<PieceCommande> _itemsTerminee = new();
        private List<PieceCommande> _itemsLivree   = new();

        // ── Constructeur ───────────────────────────────────────────────
        public StatutView()
        {
            InitializeComponent();

            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _whatsApp        = App.Services.GetRequiredService<IWhatsAppService>();
            _authService     = App.Services.GetRequiredService<IAuthService>();

            _role = _authService.UtilisateurConnecte?.Role ?? "";

            Loaded += async (s, e) =>
            {
                // Masquer le filtre couturier pour le Couturier lui-même
                if (_role == "Couturier")
                    CmbFiltreCouturier.Visibility = Visibility.Collapsed;

                // Remplir le ComboBox couturiers (sentinelle + couturiers actifs)
                RemplirFiltreCouturier();

                SetFiltreActif(BtnFiltreAll);
                await ChargerCommandes();

                // Abonnement CommandeChanged pour rafraîchissement immédiat
                // et proposition WhatsApp au passage à Terminee
                _commandeService.CommandeChanged += OnCommandeChanged;
            };

            Unloaded += (s, e) =>
            {
                _commandeService.CommandeChanged -= OnCommandeChanged;
            };
        }

        // ── Remplissage filtre couturier ──────────────────────────────
        /// <summary>
        /// Construit la liste couturiers pour CmbFiltreCouturier en réutilisant
        /// la logique de CommandesView : couturiers actifs (Role Couturier ou Boss)
        /// précédés d'une sentinelle "Tous les couturiers" (IdEmploye = 0).
        /// Si aucun service n'est disponible, déduit la liste des pièces chargées.
        /// </summary>
        private void RemplirFiltreCouturier()
        {
            _suppressCmbCouturier = true;
            try
            {
                var sentinelle = new Employe
                {
                    IdEmploye   = 0,
                    Prenom      = "Tous les couturiers",
                    Nom         = "",
                    Identifiant = "",
                    MotDePasse  = ""
                };

                // Tente de résoudre un DbContextFactory pour lire les employés actifs
                // (même source que CommandesView, sans toucher aux services)
                List<Employe> couturiers = new();
                try
                {
                    var ctxFactory = App.Services
                        .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Data.ApplicationDbContext>>();
                    using var ctx = ctxFactory.CreateDbContext();
                    couturiers = ctx.Employes
                        .Where(e => e.Statut == "Actif" && (e.Role == "Couturier" || e.Role == "Boss"))
                        .OrderBy(e => e.Prenom)
                        .Select(e => new Employe
                        {
                            IdEmploye   = e.IdEmploye,
                            Nom         = e.Nom,
                            Prenom      = e.Role == "Boss" ? e.Prenom + " (Boss)" : e.Prenom,
                            Role        = e.Role,
                            Statut      = e.Statut,
                            Identifiant = e.Identifiant,
                            MotDePasse  = e.MotDePasse
                        })
                        .ToList();
                }
                catch
                {
                    // Si DB inaccessible, laisse liste vide — on déduit depuis les pièces après chargement
                }

                var liste = new List<Employe> { sentinelle };
                liste.AddRange(couturiers);

                CmbFiltreCouturier.ItemsSource   = liste;
                CmbFiltreCouturier.SelectedIndex = 0;
            }
            finally
            {
                _suppressCmbCouturier = false;
            }
        }

        // ── Abonnement CommandeChanged ────────────────────────────────
        private async void OnCommandeChanged(object? sender, CommandeChangedEventArgs e)
        {
            // Rafraîchir le Kanban
            await Dispatcher.InvokeAsync(async () => await ChargerCommandes());

            // Proposition WhatsApp si une pièce vient de passer à Terminee
            if (e.TypeChangement == "PieceTerminee")
            {
                var commande = _commandeService.ObtenirParId(e.IdCommande);
                if (commande?.Client != null && !string.IsNullOrWhiteSpace(commande.Client.Telephone))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var rep = MessageBox.Show(
                            $"La pièce est terminée !\n\n" +
                            $"Client : {commande.Client.Prenom} {commande.Client.Nom}\n" +
                            "Envoyer un message WhatsApp pour prévenir ?",
                            "Pièce terminée — notification client",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (rep == MessageBoxResult.Yes)
                            _ = _whatsApp.NotifierCommandePreteAsync(commande);
                    });
                }
            }
        }

        // ── Helper opérateur connecté ─────────────────────────────────
        private (int id, string nom) OperateurConnecte()
        {
            var op = _authService.UtilisateurConnecte;
            return op != null
                ? (op.IdEmploye, $"{op.Prenom} {op.Nom}".Trim())
                : (0, string.Empty);
        }

        // =================================================================
        // CHARGEMENT KANBAN
        // =================================================================
        /// <summary>
        /// Charge les pièces dans les 4 colonnes du Kanban.
        /// Nom conservé "ChargerCommandes" pour rester cohérent avec l'abonnement
        /// CommandeChanged, mais charge bien des PieceCommande depuis ObtenirPagePiecesAsync.
        /// </summary>
        private async Task ChargerCommandes()
        {
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                GrilleKanban.IsEnabled      = false;

                // Décide quels appels effectuer selon le filtre actif
                PagedResult<PieceCommande>? rAfaire   = null;
                PagedResult<PieceCommande>? rEnCours  = null;
                PagedResult<PieceCommande>? rTerminee = null;
                PagedResult<PieceCommande>? rLivree   = null;

                string? filtreEffectif = _filtreStatut;

                if (filtreEffectif == null)
                {
                    // Tous : 4 colonnes, chacune filtrée par son statut propre
                    var t1 = _commandeService.ObtenirPagePiecesAsync("A faire",  _currentPage, PAGE_SIZE, NullRecherche());
                    var t2 = _commandeService.ObtenirPagePiecesAsync("En cours", _currentPage, PAGE_SIZE, NullRecherche());
                    var t3 = _commandeService.ObtenirPagePiecesAsync("Terminee", _currentPage, PAGE_SIZE, NullRecherche());
                    var t4 = _commandeService.ObtenirPagePiecesAsync("Livree",   _currentPage, PAGE_SIZE, NullRecherche());
                    await Task.WhenAll(t1, t2, t3, t4);
                    rAfaire   = t1.Result;
                    rEnCours  = t2.Result;
                    rTerminee = t3.Result;
                    rLivree   = t4.Result;
                }
                else if (filtreEffectif == "Retard")
                {
                    // Retard : seules les colonnes A faire et En cours (filtre virtuel)
                    var t1 = _commandeService.ObtenirPagePiecesAsync("Retard", _currentPage, PAGE_SIZE, NullRecherche());
                    var t2 = _commandeService.ObtenirPagePiecesAsync("Retard", _currentPage, PAGE_SIZE, NullRecherche());
                    await Task.WhenAll(t1, t2);
                    // Le service retourne toutes les pièces en retard quel que soit le statut ;
                    // on les sépare côté client entre A faire et En cours.
                    var toutesRetard = t1.Result.Items
                        .Concat(t2.Result.Items)
                        .Distinct()
                        .ToList();
                    rAfaire   = FakeResult(toutesRetard.Where(p => p.Statut == "A faire").ToList(),  t1.Result);
                    rEnCours  = FakeResult(toutesRetard.Where(p => p.Statut == "En cours").ToList(), t2.Result);
                    rTerminee = FakeResult(new List<PieceCommande>(), new PagedResult<PieceCommande> { TotalCount = 0, Page = _currentPage, PageSize = PAGE_SIZE });
                    rLivree   = FakeResult(new List<PieceCommande>(), new PagedResult<PieceCommande> { TotalCount = 0, Page = _currentPage, PageSize = PAGE_SIZE });
                }
                else
                {
                    // Filtre précis : seule la colonne correspondante est remplie
                    var tFiltre = await _commandeService.ObtenirPagePiecesAsync(filtreEffectif, _currentPage, PAGE_SIZE, NullRecherche());
                    var vide    = new PagedResult<PieceCommande> { Items = new(), TotalCount = 0, Page = _currentPage, PageSize = PAGE_SIZE };

                    rAfaire   = filtreEffectif == "A faire"  ? tFiltre : vide;
                    rEnCours  = filtreEffectif == "En cours" ? tFiltre : vide;
                    rTerminee = filtreEffectif == "Terminee" ? tFiltre : vide;
                    rLivree   = filtreEffectif == "Livree"   ? tFiltre : vide;
                }

                // Appliquer filtre couturier côté client
                _itemsAfaire   = AppliquerFiltreCouturier(rAfaire.Items);
                _itemsEnCours  = AppliquerFiltreCouturier(rEnCours.Items);
                _itemsTerminee = AppliquerFiltreCouturier(rTerminee.Items);
                _itemsLivree   = AppliquerFiltreCouturier(rLivree.Items);

                // Alimenter les ItemsControl
                ListeAfaire.ItemsSource   = _itemsAfaire;
                ListeEnCours.ItemsSource  = _itemsEnCours;
                ListeTerminee.ItemsSource = _itemsTerminee;
                ListeLivree.ItemsSource   = _itemsLivree;

                // Compteurs (TotalCount de la colonne, pas Items.Count)
                TxtCompteurAfaire.Text   = rAfaire.TotalCount.ToString();
                TxtCompteurEnCours.Text  = rEnCours.TotalCount.ToString();
                TxtCompteurTerminee.Text = rTerminee.TotalCount.ToString();
                TxtCompteurLivree.Text   = rLivree.TotalCount.ToString();

                // États vides
                TxtVideAfaire.Visibility   = _itemsAfaire.Count   == 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtVideEnCours.Visibility  = _itemsEnCours.Count  == 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtVideTerminee.Visibility = _itemsTerminee.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtVideLivree.Visibility   = _itemsLivree.Count   == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Pagination : activée si au moins une colonne a une page supplémentaire
                BtnPagePrecedente.IsEnabled = rAfaire.HasPrevious || rEnCours.HasPrevious
                                           || rTerminee.HasPrevious || rLivree.HasPrevious;
                BtnPageSuivante.IsEnabled   = rAfaire.HasNext || rEnCours.HasNext
                                           || rTerminee.HasNext || rLivree.HasNext;

                // Info pagination
                int totalPieces = rAfaire.TotalCount + rEnCours.TotalCount
                                + rTerminee.TotalCount + rLivree.TotalCount;
                TxtPaginationInfo.Text = totalPieces == 0
                    ? "Aucune pièce"
                    : $"Page {_currentPage} — {totalPieces} pièce(s)";

                // Déduire couturiers manquants si la liste du ComboBox était vide
                CompleterFiltreCouturiersDepuisPieces();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du chargement : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                GrilleKanban.IsEnabled      = true;
            }
        }

        // Retourne null si la recherche est vide (convention du service)
        private string? NullRecherche() =>
            string.IsNullOrWhiteSpace(_recherche) ? null : _recherche;

        // Applique le filtre couturier côté client
        private List<PieceCommande> AppliquerFiltreCouturier(List<PieceCommande> items)
        {
            if (_filtreCouturierId == null || _filtreCouturierId == 0)
                return items;
            return items.Where(p => p.IdCouturier == _filtreCouturierId).ToList();
        }

        // Construit un PagedResult à partir d'une liste filtrée et d'un résultat parent
        private static PagedResult<PieceCommande> FakeResult(
            List<PieceCommande> items, PagedResult<PieceCommande> parent) =>
            new()
            {
                Items      = items,
                TotalCount = parent.TotalCount,
                Page       = parent.Page,
                PageSize   = parent.PageSize
            };

        /// <summary>
        /// Si le ComboBox couturier n'a que la sentinelle (DB inaccessible au Loaded),
        /// complète la liste depuis les pièces maintenant disponibles en mémoire.
        /// Garde anti-boucle via _suppressCmbCouturier.
        /// </summary>
        private void CompleterFiltreCouturiersDepuisPieces()
        {
            // Déjà plus d'un item (sentinelle + au moins un couturier) → rien à faire
            if (CmbFiltreCouturier.Items.Count > 1) return;

            _suppressCmbCouturier = true;
            try
            {
                var toutes = _itemsAfaire.Concat(_itemsEnCours)
                                         .Concat(_itemsTerminee)
                                         .Concat(_itemsLivree);

                var couturiersDistincts = toutes
                    .Where(p => p.Couturier != null)
                    .Select(p => p.Couturier!)
                    .GroupBy(c => c.IdEmploye)
                    .Select(g => g.First())
                    .OrderBy(c => c.Prenom)
                    .ToList();

                if (couturiersDistincts.Count == 0) return;

                var sentinelle = new Employe
                {
                    IdEmploye   = 0,
                    Prenom      = "Tous les couturiers",
                    Nom         = "",
                    Identifiant = "",
                    MotDePasse  = ""
                };
                var liste = new List<Employe> { sentinelle };
                liste.AddRange(couturiersDistincts);
                CmbFiltreCouturier.ItemsSource   = liste;
                CmbFiltreCouturier.SelectedIndex = 0;
            }
            finally
            {
                _suppressCmbCouturier = false;
            }
        }

        // =================================================================
        // FILTRES RAPIDES
        // =================================================================
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

        // =================================================================
        // FILTRE COUTURIER
        // =================================================================
        private async void CmbFiltreCouturier_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCmbCouturier) return;
            if (CmbFiltreCouturier.SelectedItem is Employe emp)
                _filtreCouturierId = emp.IdEmploye == 0 ? null : emp.IdEmploye;
            else
                _filtreCouturierId = null;
            _currentPage = 1;
            await ChargerCommandes();
        }

        // =================================================================
        // RECHERCHE
        // =================================================================
        private async void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            _recherche   = TxtRecherche.Text.Trim();
            _currentPage = 1;
            await ChargerCommandes();
        }

        // =================================================================
        // PAGINATION
        // =================================================================
        private async void BtnPagePrecedente_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1) { _currentPage--; await ChargerCommandes(); }
        }

        private async void BtnPageSuivante_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await ChargerCommandes();
        }

        // =================================================================
        // HELPER PARTAGÉ — Changer statut d'une pièce
        // =================================================================
        /// <summary>
        /// Point d'entrée unique pour tout changement de statut d'une pièce.
        /// Applique la garde de rôle, AvecControleLivraison, ChangerStatutPiece,
        /// puis recharge.
        /// </summary>
        private async Task ChangerStatutPieceAsync(int idPiece, string nouveauStatut)
        {
            if (_role == "Couturier")
            {
                MessageBox.Show("La modification de statut est réservée au Boss et à la Secrétaire.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (idOp, nomOp) = OperateurConnecte();
            bool ok = await AvecControleLivraison(async motif =>
            {
                await Task.Run(() =>
                    _commandeService.ChangerStatutPiece(idPiece, nouveauStatut, idOp, nomOp, motif));
            });

            if (ok) await ChargerCommandes();
        }

        // =================================================================
        // BOUTON ▶ — Avancer au statut suivant
        // =================================================================
        private async void BtnAvancerStatut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            PieceCommande? piece = btn.Tag as PieceCommande;
            if (piece == null) return;

            string? suivant = piece.Statut switch
            {
                "A faire"  => "En cours",
                "En cours" => "Terminee",
                "Terminee" => "Livree",
                _          => null
            };
            if (suivant == null) return;

            await ChangerStatutPieceAsync(piece.IdPieceCommande, suivant);
        }

        // =================================================================
        // BOUTON ⋯ — Menu contextuel (cette pièce + toute la commande)
        // =================================================================
        private void BtnPasserA_Click(object sender, RoutedEventArgs e)
        {
            if (_role == "Couturier")
            {
                MessageBox.Show("La modification de statut est réservée au Boss et à la Secrétaire.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (sender is not Button btn) return;
            PieceCommande? piece = btn.Tag as PieceCommande;
            if (piece == null) return;

            var menu = new ContextMenu();

            // ── Groupe 1 : Cette pièce ──
            menu.Items.Add(new MenuItem
            {
                Header    = $"Cette pièce (P#{piece.IdPieceCommande})",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            });
            menu.Items.Add(new Separator());

            void AjouterItemPiece(string libelle, string statut)
            {
                var item = new MenuItem { Header = libelle };
                item.Click += async (s, ev) =>
                    await ChangerStatutPieceAsync(piece.IdPieceCommande, statut);
                menu.Items.Add(item);
            }
            AjouterItemPiece("⬜  À faire",  "A faire");
            AjouterItemPiece("🟡  En cours",  "En cours");
            AjouterItemPiece("🟢  Terminée",  "Terminee");
            AjouterItemPiece("🔵  Livrée",    "Livree");

            // ── Groupe 2 : Toute la commande ──
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header    = $"Toute la commande #{piece.IdCommande}",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            });
            menu.Items.Add(new Separator());

            void AjouterItemCommande(string libelle, string statut)
            {
                var item = new MenuItem { Header = libelle };
                item.Click += async (s, ev) =>
                    await ForcerStatutCommande(piece.IdCommande, statut);
                menu.Items.Add(item);
            }
            AjouterItemCommande("⬜  À faire (tout)",  "A faire");
            AjouterItemCommande("🟡  En cours (tout)",  "En cours");
            AjouterItemCommande("🟢  Terminée (tout)",  "Terminee");
            AjouterItemCommande("🔵  Livrée (tout)",    "Livree");

            btn.ContextMenu = menu;
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }

        private async Task ForcerStatutCommande(int idCommande, string statut)
        {
            if (_role == "Couturier")
            {
                MessageBox.Show("La modification de statut est réservée au Boss et à la Secrétaire.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (idOp, nomOp) = OperateurConnecte();
            bool ok = await AvecControleLivraison(async motif =>
            {
                await Task.Run(() =>
                    _commandeService.ForcerStatutToutesPieces(idCommande, statut, idOp, nomOp, motif));
            });

            if (ok) await ChargerCommandes();
        }

        // =================================================================
        // BOUTON 🗂 — Fiche atelier
        // Pattern identique à CommandesView.BtnActionFicheAtelier_Click
        // =================================================================
        private void BtnFicheAtelier_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            PieceCommande? piece = btn.Tag as PieceCommande;
            if (piece == null) return;

            try
            {
                var commandeComplete = _commandeService.ObtenirParId(piece.IdCommande);
                if (commandeComplete == null)
                {
                    MessageBox.Show("Commande introuvable.",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var fiche = FicheAtelierWindow.Creer(commandeComplete);
                fiche.Owner = Window.GetWindow(this);
                fiche.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur fiche atelier : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =================================================================
        // BOUTON 💬 — WhatsApp
        // DataContext = PieceCommande ; recharge la commande complète pour
        // avoir StatutGlobal et Client corrects.
        // =================================================================
        private async void BtnWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            PieceCommande? piece = btn.Tag as PieceCommande;
            if (piece == null) return;

            // Recharger la commande complète (StatutGlobal, Client.Telephone)
            var commande = _commandeService.ObtenirParId(piece.IdCommande);
            if (commande == null)
            {
                MessageBox.Show("Commande introuvable.",
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

                bool estTerminee  = commande.StatutGlobal is "Terminee" or "Terminee partiellement";
                bool estLivree    = commande.StatutGlobal is "Livree"   or "Livree partiellement";
                bool estEnRetard  = commande.EstEnRetard;
                bool rdvProche    = !estEnRetard
                    && commande.DateFin <= DateTime.Now.AddDays(2)
                    && commande.StatutGlobal is "A faire" or "En cours";

                if (estTerminee || estLivree)
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

        // =================================================================
        // GESTION LIVRAISON NON SOLDÉE
        // =================================================================

        /// <summary>
        /// Enveloppe une action asynchrone dans la gestion de LivraisonNonSoldeeException.
        /// - PeutForcer == false : MessageBox bloquant, return false.
        /// - PeutForcer == true  : ouvre le modal pour saisir un motif.
        ///   Le modal appellera _actionEnAttente(motif) puis rechargera.
        ///   Return false ici (l'action sera complétée depuis le modal).
        /// Retourne true si l'action s'est exécutée sans exception de livraison.
        /// </summary>
        private async Task<bool> AvecControleLivraison(Func<string?, Task> action)
        {
            try
            {
                await action(null);
                return true;
            }
            catch (LivraisonNonSoldeeException ex) when (ex.PeutForcer)
            {
                // Ouvrir le modal au lieu de la boîte de dialogue bloquante
                _actionEnAttente = action;
                TxtInfoSoldeRestant.Text = $"Cette commande présente un solde restant de {ex.ResteAPayer:N0} FCFA.";
                TxtMotifLivraison.Text   = string.Empty;
                ModalLivraisonNonSoldee.Visibility = Visibility.Visible;
                return false;   // L'action sera complétée depuis BtnConfirmerLivraisonNonSoldee_Click
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

        // ── Confirmer la livraison non soldée ──────────────────────────
        private async void BtnConfirmerLivraisonNonSoldee_Click(object sender, RoutedEventArgs e)
        {
            string motif = TxtMotifLivraison.Text.Trim();
            if (string.IsNullOrWhiteSpace(motif))
            {
                MessageBox.Show("Le motif est obligatoire pour confirmer la livraison non soldée.",
                    "Motif manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // Rester dans le modal
            }

            if (_actionEnAttente == null) { FermerModal(); return; }

            var action = _actionEnAttente;
            FermerModal();

            try
            {
                await action(motif);
                await ChargerCommandes();
            }
            catch (LivraisonNonSoldeeException ex) when (!ex.PeutForcer)
            {
                MessageBox.Show(ex.Message,
                    "Livraison impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Opération impossible",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(ex.Message, "Accès refusé",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Annuler le modal ──────────────────────────────────────────
        private void BtnAnnulerLivraisonNonSoldee_Click(object sender, RoutedEventArgs e)
        {
            FermerModal();
        }

        private void FermerModal()
        {
            ModalLivraisonNonSoldee.Visibility = Visibility.Collapsed;
            TxtMotifLivraison.Text   = string.Empty;
            _actionEnAttente         = null;
        }
    }
}
