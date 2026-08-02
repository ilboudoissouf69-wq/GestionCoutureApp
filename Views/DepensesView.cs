using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    public partial class DepensesView : Page
    {
        private readonly IDepenseService _depenseService;

        public DepensesView()
        {
            InitializeComponent();

            if (!RoleAutorise()) return;

            _depenseService = App.Services.GetRequiredService<IDepenseService>();

            // Date par defaut : aujourd'hui
            DateDepense.SelectedDate = DateTime.Today;

            Loaded += (s, e) => ChargerDonnees();
        }

        private bool RoleAutorise()
        {
            var authService = App.Services.GetRequiredService<IAuthService>();
            if (authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Acces reserve au Boss.");
            return true;
        }

        private void ChargerDonnees()
        {
            try
            {
                var depenses = _depenseService.ObtenirTous();
                GridDepenses.ItemsSource = depenses;

                // Statistiques
                var maintenant = DateTime.Now;
                var debutMois = new DateTime(maintenant.Year, maintenant.Month, 1);
                var finMois = debutMois.AddMonths(1).AddDays(-1);

                var totalMois = _depenseService.TotalParPeriode(debutMois, finMois);
                var totalJour = _depenseService.TotalParPeriode(maintenant, maintenant);

                TxtTotalMois.Text = totalMois.ToString("N0");
                TxtTotalJour.Text = totalJour.ToString("N0");
                TxtNbDepenses.Text = depenses
                    .Count(d => d.DateDepense.Date >= debutMois
                             && d.DateDepense.Date <= finMois)
                    .ToString();
            }
            catch (Exception ex)
            {
                TxtMessage.Text = "Erreur : " + ex.Message;
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void BtnAjouter_Click(object sender, RoutedEventArgs e)
        {
            // Validation type
            if (CmbTypeDepense.SelectedItem == null)
            {
                TxtMessage.Text = "Selectionnez un type de depense.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            // Validation montant
            if (!decimal.TryParse(TxtMontant.Text, out decimal montant) || montant <= 0)
            {
                TxtMessage.Text = "Saisissez un montant valide (superieur a 0).";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            // Validation date
            if (DateDepense.SelectedDate == null)
            {
                TxtMessage.Text = "Selectionnez une date.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            try
            {
                var authService = App.Services.GetRequiredService<IAuthService>();
                var operateur = authService.UtilisateurConnecte;

                var depense = new Depense
                {
                    TypeDepense = ((ComboBoxItem)CmbTypeDepense.SelectedItem).Content.ToString()!,
                    Montant = montant,
                    DateDepense = DateDepense.SelectedDate.Value,
                    Description = TxtDescription.Text.Trim(),
                    NomOperateur = operateur != null
                        ? $"{operateur.Prenom} {operateur.Nom}"
                        : ""
                };

                _depenseService.Ajouter(depense);

                // Reinitialiser le formulaire
                CmbTypeDepense.SelectedIndex = -1;
                TxtMontant.Text = string.Empty;
                TxtDescription.Text = string.Empty;

                TxtMessage.Text = "Depense enregistree.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Green;

                ChargerDonnees();
            }
            catch (Exception ex)
            {
                TxtMessage.Text = "Erreur : " + ex.Message;
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        // CORRECTIF (audit) : IDepenseService.Supprimer n'existe plus (Décision
        // 3.1 — une dépense ne disparaît jamais physiquement, elle est
        // annulée avec motif obligatoire et trace, comme un paiement ou une
        // commission). Le bouton XAML garde le nom "BtnSupprimer_Click" pour
        // ne pas modifier le XAML, mais son comportement est maintenant celui
        // d'une annulation.
        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (GridDepenses.SelectedItem is not Depense depense)
            {
                MessageBox.Show("Selectionnez une depense a annuler.",
                    "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (depense.EstAnnulee)
            {
                MessageBox.Show("Cette depense est deja annulee.",
                    "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? motif = DemanderMotifAnnulation(depense);
            if (motif == null) return; // la secretaire/le Boss a annule l'annulation

            var result = MessageBox.Show(
                $"Confirmer l'annulation de cette depense de {depense.Montant:N0} FCFA " +
                $"({depense.TypeDepense}) ?\n\nMotif : {motif}",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var authService = App.Services.GetRequiredService<IAuthService>();
                var operateur = authService.UtilisateurConnecte;
                string nomAnnulateur = operateur != null
                    ? $"{operateur.Prenom} {operateur.Nom}"
                    : "";

                _depenseService.Annuler(depense.IdDepense, motif, nomAnnulateur);
                TxtMessage.Text = "Depense annulee.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Green;
                ChargerDonnees();
            }
            catch (Exception ex)
            {
                TxtMessage.Text = "Erreur : " + ex.Message;
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        // CORRECTIF (audit) : petit dialogue de saisie du motif, sur le même
        // principe que PaiementsView.DemanderMotifAnnulation — simplifié ici
        // (pas de confirmation par mot de passe : la Décision 3.1 exige un
        // motif obligatoire, pas une double authentification, contrairement
        // à l'annulation d'un paiement qui touche directement la caisse).
        private string? DemanderMotifAnnulation(Depense depense)
        {
            var dialog = new Window
            {
                Title = "Motif d'annulation",
                Width = 420,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel { Margin = new Thickness(16) };

            panel.Children.Add(new TextBlock
            {
                Text = $"Depense : {depense.Montant:N0} FCFA ({depense.TypeDepense})",
                Margin = new Thickness(0, 0, 0, 8),
                FontWeight = System.Windows.FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Motif d'annulation * (obligatoire, 10 caracteres minimum)",
                Margin = new Thickness(0, 0, 0, 4)
            });

            var txMotif = new TextBox
            {
                Height = 60,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                AcceptsReturn = true,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(txMotif);

            var btnPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var btnValider = new Button
            {
                Content = "Confirmer",
                Width = 100,
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnValider.Click += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(txMotif.Text) || txMotif.Text.Trim().Length < 10)
                {
                    MessageBox.Show("Le motif doit contenir au moins 10 caracteres.",
                        "Motif insuffisant", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txMotif.Focus();
                    return;
                }
                dialog.Tag = txMotif.Text.Trim();
                dialog.DialogResult = true;
            };

            var btnAnnulerDialog = new Button { Content = "Annuler", Width = 100 };
            btnAnnulerDialog.Click += (s, ev) => { dialog.DialogResult = false; };

            btnPanel.Children.Add(btnValider);
            btnPanel.Children.Add(btnAnnulerDialog);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;

            return dialog.ShowDialog() == true ? (string)dialog.Tag : null;
        }
    }
}