using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class PaiementsView : Page
    {
        private readonly IPaiementService _paiementService;
        private readonly ICommandeService _commandeService;
        private readonly IAuthService _authService;
        private readonly ApplicationDbContext _context;
        private Commande? _commandeSelectionnee;
        private Employe? _operateurConnecte;

        public PaiementsView()
        {
            InitializeComponent();

            _paiementService = App.Services.GetRequiredService<IPaiementService>();
            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _authService = App.Services.GetRequiredService<IAuthService>();
            _context = App.Services.GetRequiredService<ApplicationDbContext>();

            _operateurConnecte = _authService.UtilisateurConnecte;

            // Affiche le nom de l'operateur connecte dans le bandeau
            if (_operateurConnecte != null)
                TxtOperateurConnecte.Text =
                    "Operateur : " + _operateurConnecte.Prenom + " " + _operateurConnecte.Nom;

            ChargerCommandes();
            ChargerPaiements();
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
                DisplayText = (c.Client?.Nom ?? "") + " " + (c.Client?.Prenom ?? "")
                              + " — " + c.TypeVetement
                              + "  (" + c.MontantTotal.ToString("N0") + " FCFA)"
            }).ToList();
            CmbCommande.SelectedValuePath = "IdCommande";
        }

        private void ChargerPaiements()
        {
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

            double totalValide = _paiementService.TotalValideParCommande(idCmd);
            double reste = _commandeSelectionnee.MontantTotal - totalValide;

            TxtInfoMontant.Text = "Montant total : " + _commandeSelectionnee.MontantTotal.ToString("N0") + " FCFA";
            TxtInfoDejaPaye.Text = "Deja paye : " + totalValide.ToString("N0") + " FCFA";
            TxtInfoReste.Text = "Reste : " + Math.Max(0, reste).ToString("N0") + " FCFA";

            // Historique detaille
            var historique = _paiementService.ObtenirParCommande(idCmd);
            ListeHistorique.ItemsSource = historique
                .Select(p => p.AffichageHistorique)
                .ToList();

            // Desactive le champ montant si tout est paye
            TxtMontant.IsEnabled = reste > 0.01;
            BtnEnregistrer.IsEnabled = reste > 0.01;
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

            if (!double.TryParse(TxtMontant.Text.Replace(" ", ""), out double montant) || montant <= 0)
            { Alerte("Le montant doit etre un nombre positif."); return; }

            if (_commandeSelectionnee == null)
            { Alerte("Commande introuvable."); return; }

            if (_operateurConnecte == null)
            { Alerte("Aucun operateur connecte."); return; }

            // Verification solde en temps reel
            double totalValide = _paiementService.TotalValideParCommande(_commandeSelectionnee.IdCommande);
            double reste = _commandeSelectionnee.MontantTotal - totalValide;

            if (reste <= 0.01)
            { Alerte("Cette commande est deja entierement payee."); return; }

            if (montant > reste + 0.01)
            {
                Alerte($"Le montant saisi ({montant:N0} FCFA) depasse\nle reste a payer ({reste:N0} FCFA).");
                return;
            }

            // Confirmation avant enregistrement
            string modeChoisi = ((ComboBoxItem)CmbModePaiement.SelectedItem).Content?.ToString() ?? "Especes";
            var confirmation = MessageBox.Show(
                $"Confirmer l'enregistrement du paiement ?\n\n" +
                $"Client  : {_commandeSelectionnee.Client?.Nom} {_commandeSelectionnee.Client?.Prenom}\n" +
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
                    IdCommande = _commandeSelectionnee.IdCommande,
                    MontantPaye = montant,
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
                .Include(c => c.Couturier)
                .FirstOrDefault(c => c.IdCommande == paiement.IdCommande);

            if (commande == null) { Alerte("Commande introuvable."); return; }

            var mesures = _context.Mesures
                .Where(m => m.IdCommande == commande.IdCommande)
                .ToList();

            var fenetre = new FenetreRecu(commande, paiement, mesures, paiement.NomOperateur);
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
        // Helpers
        // ----------------------------------------------------------------

        private void Alerte(string message)
        {
            MessageBox.Show(message, "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
