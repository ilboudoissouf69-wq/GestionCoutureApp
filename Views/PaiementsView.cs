using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using GestionCoutureApp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class PaiementsView : Page
    {
        private readonly IPaiementService _paiementService;
        private readonly ICommandeService _commandeService;
        private readonly IAuthService _authService;
        private readonly IReceiptService _receiptService;
        private readonly ILanguageService _languageService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ApplicationDbContext _context;
        private Commande? _commandeSelectionnee;
        private Employe? _operateurConnecte;

        public PaiementsView()
        {
            InitializeComponent();

            _paiementService = App.Services.GetRequiredService<IPaiementService>();
            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _authService = App.Services.GetRequiredService<IAuthService>();
            _receiptService = App.Services.GetRequiredService<IReceiptService>();
            _languageService = App.Services.GetRequiredService<ILanguageService>();
            _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();

            // ✅ CORRECTIF AUDIT #1 : S'abonner aux changements de commande
            _commandeService.CommandeChanged += OnCommandeChanged;
            
            // ✅ S'abonner aux changements de langue et de thème
            _eventAggregator.Subscribe(SettingsChangedType.Language, OnLanguageChanged);
            _eventAggregator.Subscribe(SettingsChangedType.AccentColor, OnThemeChanged);

            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();
            Unloaded += (s, e) =>
            {
                _context.Dispose();
                // ✅ Se désabonner pour éviter les fuites mémoire
                _commandeService.CommandeChanged -= OnCommandeChanged;
                _eventAggregator.Unsubscribe(SettingsChangedType.Language, OnLanguageChanged);
                _eventAggregator.Unsubscribe(SettingsChangedType.AccentColor, OnThemeChanged);
            };

            _operateurConnecte = _authService.UtilisateurConnecte;

            // Affiche le nom de l'operateur connecte dans le bandeau
            if (_operateurConnecte != null)
                TxtOperateurConnecte.Text =
                    "Operateur : " + _operateurConnecte.Prenom + " " + _operateurConnecte.Nom;

            ChargerCommandes();
            ChargerPaiements();
        }
        
        // ------------------------------------------------------------------
        // Gestionnaire de changement de langue
        // ------------------------------------------------------------------
        private void OnLanguageChanged(SettingsChangedEvent evt)
        {
            Dispatcher.Invoke(() => UpdateTranslations());
        }
        
        // ------------------------------------------------------------------
        // Gestionnaire de changement de thème
        // ------------------------------------------------------------------
        private void OnThemeChanged(SettingsChangedEvent evt)
        {
            // Les couleurs utilisent DynamicResource, donc elles se mettent à jour automatiquement
        }
        
        // ------------------------------------------------------------------
        // Mettre à jour les traductions de PaiementsView
        // ------------------------------------------------------------------
        private void UpdateTranslations()
        {
            // Pour l'instant, PaiementsView n'a pas beaucoup de textes traduisibles
            // Les messages MessageBox restent en français pour l'instant
        }

        // ----------------------------------------------------------------
        // Chargement
        // ----------------------------------------------------------------

        private void ChargerCommandes()
        {
            var commandes = _commandeService.ObtenirTous();
            CmbCommande.ItemsSource = commandes.Select(c => new
            {
                c.IdCommande,
                // MontantTotalAvecMateriaux = couture + matériaux (Point 2) :
                // c'est le montant réel que le client doit payer sur sa facture.
                DisplayText = (c.Client?.Nom ?? "") + " " + (c.Client?.Prenom ?? "")
                              + " — " + c.TypeVetementAffiche
                              + "  (" + c.MontantTotalAvecMateriaux.ToString("N0") + " FCFA)"
            }).ToList();
            CmbCommande.SelectedValuePath = "IdCommande";
        }

        private void ChargerPaiements()
        {
            GridPaiements.ItemsSource = null;
            GridPaiements.ItemsSource = _paiementService.ObtenirTous();
        }

        // ----------------------------------------------------------------
        // Selection d'une commande dans la ComboBox
        // ----------------------------------------------------------------

        private void CmbCommande_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCommande.SelectedValue == null) return;
            int idCmd = (int)CmbCommande.SelectedValue;

            _commandeSelectionnee = _commandeService.ObtenirTous()
                .FirstOrDefault(c => c.IdCommande == idCmd);
            if (_commandeSelectionnee == null) return;

            TxtInfoClient.Text = "Client : "
                + (_commandeSelectionnee.Client?.Nom ?? "") + " "
                + (_commandeSelectionnee.Client?.Prenom ?? "");

            decimal totalValide = _paiementService.TotalValideParCommande(idCmd);

            // MontantTotalAvecMateriaux = couture + matériaux (Point 2).
            // C'est le montant total de la FACTURE que le client doit régler.
            // La commission du couturier sera calculée séparément sur MontantTotalCalcule
            // (couture seule) — les matériaux n'y entrent jamais.
            decimal montantTotal = _commandeSelectionnee.MontantTotalAvecMateriaux;
            decimal montantCouture = _commandeSelectionnee.MontantTotalCalcule;
            decimal montantMateriaux = _commandeSelectionnee.TotalMateriaux;
            decimal reste = montantTotal - totalValide;

            TxtInfoMontant.Text = montantMateriaux > 0
                ? $"Total facture : {montantTotal:N0} FCFA  (couture {montantCouture:N0} + materiaux {montantMateriaux:N0})"
                : $"Total facture : {montantTotal:N0} FCFA";
            TxtInfoDejaPaye.Text = "Deja paye : " + totalValide.ToString("N0") + " FCFA";
            TxtInfoReste.Text = "Reste : " + Math.Max(0m, reste).ToString("N0") + " FCFA";

            // Historique detaille
            var historique = _paiementService.ObtenirParCommande(idCmd);
            ListeHistorique.ItemsSource = historique
                .Select(p => p.AffichageHistorique)
                .ToList();

            // Desactive le champ montant si tout est paye
            TxtMontant.IsEnabled    = reste > 0.01m;
            BtnEnregistrer.IsEnabled = reste > 0.01m;
        }

        // ----------------------------------------------------------------
        // Clic sur une ligne du tableau -> selectionne la commande
        // ----------------------------------------------------------------

        private void GridPaiements_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridPaiements.SelectedItem is Paiement p)
                CmbCommande.SelectedValue = p.IdCommande;
        }

        // ----------------------------------------------------------------
        // Enregistrer un paiement
        // ----------------------------------------------------------------

        private void BtnEnregistrer_Click(object sender, RoutedEventArgs e)
        {
            // Validations de base
            if (CmbCommande.SelectedValue == null)
            { Alerte("Selectionnez une commande."); return; }

            if (!decimal.TryParse(TxtMontant.Text.Replace(" ", ""), out decimal montant) || montant <= 0)
            { Alerte("Le montant doit etre un nombre positif."); return; }

            if (_commandeSelectionnee == null)
            { Alerte("Commande introuvable."); return; }

            if (_operateurConnecte == null)
            { Alerte("Aucun operateur connecte."); return; }

            // Verification solde en temps reel sur le montant total facture
            // (couture + materiaux) — c'est ce que le client doit rembourser.
            decimal totalValide = _paiementService.TotalValideParCommande(_commandeSelectionnee.IdCommande);
            decimal reste = _commandeSelectionnee.MontantTotalAvecMateriaux - totalValide;

            if (reste <= 0.01m)
            { Alerte("Cette commande est deja entierement payee."); return; }

            if (montant > reste + 0.01m)
            {
                Alerte($"Le montant saisi ({montant:N0} FCFA) depasse\nle reste a payer ({reste:N0} FCFA).");
                return;
            }

            // Confirmation avant enregistrement
            string modeChoisi = ((ComboBoxItem)CmbModePaiement.SelectedItem).Content?.ToString() ?? "Especes";
            decimal totalFacture = _commandeSelectionnee.MontantTotalAvecMateriaux;
            decimal totalMateriaux = _commandeSelectionnee.TotalMateriaux;
            string ligneTotal = totalMateriaux > 0
                ? $"Total facture : {totalFacture:N0} FCFA (dont {totalMateriaux:N0} materiaux)\n"
                : $"Total facture : {totalFacture:N0} FCFA\n";

            var confirmation = MessageBox.Show(
                $"Confirmer l'enregistrement du paiement ?\n\n" +
                $"Client  : {_commandeSelectionnee.Client?.Nom} {_commandeSelectionnee.Client?.Prenom}\n" +
                ligneTotal +
                $"Montant : {montant:N0} FCFA\n" +
                $"Mode    : {modeChoisi}\n" +
                $"Reste apres : {(reste - montant):N0} FCFA\n\n" +
                $"Operateur : {_operateurConnecte.Prenom} {_operateurConnecte.Nom}",
                "Confirmation du paiement",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes) return;

            try
            {
                var paiement = new Paiement
                {
                    IdCommande   = _commandeSelectionnee.IdCommande,
                    MontantPaye  = montant,
                    ModePaiement = modeChoisi
                };

                _paiementService.Ajouter(
                    paiement,
                    _operateurConnecte.IdEmploye,
                    _operateurConnecte.Prenom + " " + _operateurConnecte.Nom);

                TxtMontant.Text = "";
                ChargerPaiements();
                CmbCommande_SelectionChanged(null!, null!);

                MessageBox.Show(
                    $"Paiement enregistre avec succes !\n\n" +
                    $"Numero de recu : {paiement.RecuNumero}\n" +
                    $"Montant        : {paiement.MontantPaye:N0} FCFA\n" +
                    $"Operateur      : {paiement.NomOperateur}\n" +
                    $"Date           : {paiement.DatePaiement:dd/MM/yyyy HH:mm}",
                    "Paiement enregistre",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                Alerte("Erreur : " + ex.Message);
            }
        }

        // ----------------------------------------------------------------
        // Annuler un paiement (Boss uniquement, motif obligatoire)
        // ----------------------------------------------------------------

        private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            if (GridPaiements.SelectedItem is not Paiement paiement)
            {
                Alerte("Selectionnez un paiement dans le tableau pour l'annuler.");
                return;
            }

            if (paiement.EstAnnule)
            {
                Alerte("Ce paiement est deja annule.");
                return;
            }

            // Seul le Boss peut annuler
            if (_operateurConnecte?.Role != "Boss")
            {
                Alerte("Seul le Boss peut annuler un paiement.");
                return;
            }

            // Fenetre de saisie du motif + confirmation mot de passe
            string? motif = DemanderMotifAnnulation(paiement);
            if (motif == null) return; // annulation de l'annulation

            // Confirmation finale
            var confirmation = MessageBox.Show(
                $"ATTENTION : Cette action est irreversible !\n\n" +
                $"Paiement  : {paiement.RecuNumero}\n" +
                $"Montant   : {paiement.MontantPaye:N0} FCFA\n" +
                $"Operateur : {paiement.NomOperateur}\n" +
                $"Motif     : {motif}\n\n" +
                $"Confirmer l'annulation ?",
                "Confirmation annulation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes) return;

            try
            {
                _paiementService.Annuler(
                    paiement.IdPaiement,
                    motif,
                    _operateurConnecte.IdEmploye,
                    _operateurConnecte.Prenom + " " + _operateurConnecte.Nom);

                ChargerPaiements();
                CmbCommande_SelectionChanged(null!, null!);

                MessageBox.Show(
                    $"Paiement {paiement.RecuNumero} annule.\n" +
                    $"Le montant de {paiement.MontantPaye:N0} FCFA est desormais\n" +
                    $"reintegre dans le solde de la commande.",
                    "Annulation effectuee",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                Alerte("Erreur : " + ex.Message);
            }
        }

        // ----------------------------------------------------------------
        // Generer le recu imprimable
        // ----------------------------------------------------------------

        private void BtnGenererRecu_Click(object sender, RoutedEventArgs e)
        {
            if (GridPaiements.SelectedItem is not Paiement paiement)
            {
                Alerte("Selectionnez un paiement dans le tableau.");
                return;
            }

            if (paiement.EstAnnule)
            {
                Alerte("Ce paiement est annule. Impossible d'imprimer un recu annule.");
                return;
            }

            var commande = _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.MaterielSupplements)
                .FirstOrDefault(c => c.IdCommande == paiement.IdCommande);

            if (commande == null) { Alerte("Commande introuvable."); return; }

            // Les mesures sont maintenant portées par chaque pièce (Étape 1b-i).
            // On les rassemble depuis toutes les pièces pour rétrocompatibilité
            // avec FenetreRecu qui attend encore une liste plate de Mesure.
            var mesures = commande.Pieces.SelectMany(p => p.Mesures).ToList();

            var fenetre = new FenetreRecu(commande, paiement, mesures, paiement.NomOperateur, _receiptService);
            fenetre.Owner = Window.GetWindow(this);
            fenetre.Show();
        }

        // ----------------------------------------------------------------
        // Fenetre de saisie du motif d'annulation
        // ----------------------------------------------------------------

        private string? DemanderMotifAnnulation(Paiement paiement)
        {
            var dialog = new Window
            {
                Title = "Motif d'annulation",
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                Owner = Window.GetWindow(this)
            };

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            // Info paiement
            panel.Children.Add(new TextBlock
            {
                Text = $"Paiement : {paiement.RecuNumero}",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Montant : {paiement.MontantPaye:N0} FCFA  |  Operateur : {paiement.NomOperateur}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            // Champ motif
            panel.Children.Add(new TextBlock
            {
                Text = "Motif d'annulation * (obligatoire)",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 5)
            });

            var txMotif = new TextBox
            {
                Height = 80,
                FontSize = 13,
                Padding = new Thickness(8),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 6)
            };
            panel.Children.Add(txMotif);

            var msgErreur = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(msgErreur);

            // Mot de passe Boss
            panel.Children.Add(new TextBlock
            {
                Text = "Mot de passe Boss (confirmation)",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 5)
            });

            var pwBox = new PasswordBox
            {
                Height = 38,
                FontSize = 13,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            panel.Children.Add(pwBox);

            panel.Children.Add(new Separator
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Margin = new Thickness(0, 0, 0, 14)
            });

            // Boutons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnConfirmer = new Button
            {
                Content = "Confirmer",
                Width = 120,
                Height = 38,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand
            };

            var btnAnnulerDialog = new Button
            {
                Content = "Annuler",
                Width = 100,
                Height = 38,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Cursor = Cursors.Hand
            };

            btnConfirmer.Click += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(txMotif.Text))
                {
                    msgErreur.Text = "Le motif est obligatoire.";
                    txMotif.Focus();
                    return;
                }
                if (txMotif.Text.Trim().Length < 10)
                {
                    msgErreur.Text = "Le motif doit contenir au moins 10 caracteres.";
                    txMotif.Focus();
                    return;
                }

                // Verification mot de passe Boss
                string mdpSaisi = pwBox.Password.Trim();
                if (string.IsNullOrEmpty(mdpSaisi))
                {
                    msgErreur.Text = "Le mot de passe Boss est obligatoire.";
                    pwBox.Focus();
                    return;
                }

                // Avec un hachage salé, on ne peut plus comparer les hash directement en SQL :
                // on charge les comptes Boss puis on vérifie le mot de passe en mémoire.
                var boss = _context.Employes
                    .Where(emp => emp.Role == "Boss" && emp.Statut == "Actif")
                    .AsEnumerable()
                    .FirstOrDefault(emp =>
                        GestionCoutureApp.Helpers.PasswordHasher.EstAncienFormatSha256(emp.MotDePasse)
                            ? emp.MotDePasse == GestionCoutureApp.Helpers.PasswordHasher.HasherAncienSha256(mdpSaisi)
                            : GestionCoutureApp.Helpers.PasswordHasher.Verifier(mdpSaisi, emp.MotDePasse));

                if (boss == null)
                {
                    msgErreur.Text = "Mot de passe Boss incorrect.";
                    pwBox.Clear();
                    pwBox.Focus();
                    return;
                }

                dialog.Tag = txMotif.Text.Trim();
                dialog.DialogResult = true;
                dialog.Close();
            };

            btnAnnulerDialog.Click += (s, ev) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            btnPanel.Children.Add(btnConfirmer);
            btnPanel.Children.Add(btnAnnulerDialog);
            panel.Children.Add(btnPanel);
            dialog.Content = panel;

            bool? result = dialog.ShowDialog();
            if (result == true && dialog.Tag is string motif)
                return motif;

            return null;
        }

        // ----------------------------------------------------------------
        // ✅ CORRECTIF AUDIT #1 : Gestion des changements de commande
        // ----------------------------------------------------------------

        /// <summary>
        /// Appelé automatiquement lorsqu'une commande est modifiée ailleurs
        /// (ex: ajout de pièce depuis CommandesView).
        /// Rafraîchit l'affichage si c'est la commande actuellement sélectionnée.
        /// </summary>
        private void OnCommandeChanged(object? sender, CommandeChangedEventArgs e)
        {
            // Ne rien faire si aucune commande sélectionnée ou si c'est une autre commande
            if (_commandeSelectionnee == null || _commandeSelectionnee.IdCommande != e.IdCommande)
                return;

            // Rafraîchir sur le thread UI
            Dispatcher.Invoke(() =>
            {
                // Recharger la commande depuis la base
                _commandeSelectionnee = _commandeService.ObtenirParId(e.IdCommande);

                if (_commandeSelectionnee == null)
                {
                    // La commande a été supprimée
                    TxtInfoMontant.Text = "—";
                    TxtInfoReste.Text = "—";
                    TxtInfoClient.Text = "Commande supprimée";
                    return;
                }

                // Mettre à jour l'affichage
                TxtInfoMontant.Text = _commandeSelectionnee.MontantTotalAvecMateriaux.ToString("N0") + " FCFA";
                TxtInfoReste.Text = _commandeSelectionnee.ResteAPayer.ToString("N0") + " FCFA";
                
                decimal dejaEncaisse = _commandeSelectionnee.Paiements
                    .Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);
                TxtInfoDejaPaye.Text = $"Deja paye : {dejaEncaisse:N0} FCFA";

                // Couleur du reste à payer
                if (_commandeSelectionnee.ResteAPayer <= 0.01m)
                {
                    TxtInfoReste.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                    TxtInfoReste.Text += " ✓";
                }
                else
                {
                    TxtInfoReste.Foreground = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));
                }

                // Afficher un toast informatif temporaire
                var originalClient = TxtInfoClient.Text;
                TxtInfoClient.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                TxtInfoClient.Text = $"⚡ {e.TypeChangement}";

                // Retour au texte normal après 3 secondes
                var timer = new System.Windows.Threading.DispatcherTimer 
                { 
                    Interval = TimeSpan.FromSeconds(3) 
                };
                timer.Tick += (s, _) =>
                {
                    TxtInfoClient.Text = originalClient;
                    TxtInfoClient.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    timer.Stop();
                };
                timer.Start();

                // Recharger aussi la liste des commandes (ComboBox) et paiements
                ChargerCommandes();
                ChargerPaiements();
            });
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private void Alerte(string message)
        {
            MessageBox.Show(message, "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ✅ Validation des montants décimaux
        private void TxtMontant_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtMontant_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            ValidationHelper.TextBox_Pasting(sender, e);
        }
    }
}
