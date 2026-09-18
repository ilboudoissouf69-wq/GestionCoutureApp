using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class DepensesView : Page
    {
        private readonly IDepenseService _depenseService;
        private readonly IAuthService _authService;
        private bool _initialise = false;

        // Période courante
        private DateTime _debutPeriode;
        private DateTime _finPeriode;

        public DepensesView()
        {
            _authService = App.Services.GetRequiredService<IAuthService>();
            if (_authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            InitializeComponent();

            _depenseService = App.Services.GetRequiredService<IDepenseService>();

            DateDepense.SelectedDate  = DateTime.Today;
            DateDebutFiltre.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateFinFiltre.SelectedDate   = DateTime.Today;

            // Période par défaut : ce mois-ci
            CalculerPeriode("mois");

            _initialise = true;
            Loaded += (s, e) => ChargerTout();
        }

        // ==================================================================
        // Calcul de la période selon le sélecteur
        // ==================================================================
        private void CalculerPeriode(string tag)
        {
            var today = DateTime.Today;
            switch (tag)
            {
                case "jour":
                    _debutPeriode = today;
                    _finPeriode   = today;
                    break;
                case "semaine":
                    int diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
                    if (diff < 0) diff += 7;
                    _debutPeriode = today.AddDays(-diff);
                    _finPeriode   = _debutPeriode.AddDays(6);
                    break;
                case "trimestre":
                    int moisDebTrim = ((today.Month - 1) / 3) * 3 + 1;
                    _debutPeriode = new DateTime(today.Year, moisDebTrim, 1);
                    _finPeriode   = _debutPeriode.AddMonths(3).AddDays(-1);
                    break;
                case "annee":
                    _debutPeriode = new DateTime(today.Year, 1, 1);
                    _finPeriode   = new DateTime(today.Year, 12, 31);
                    break;
                case "custom":
                    _debutPeriode = DateDebutFiltre.SelectedDate ?? today.AddMonths(-1);
                    _finPeriode   = DateFinFiltre.SelectedDate   ?? today;
                    break;
                default: // mois
                    _debutPeriode = new DateTime(today.Year, today.Month, 1);
                    _finPeriode   = _debutPeriode.AddMonths(1).AddDays(-1);
                    break;
            }
            TxtPeriodeLabel.Text = $"Période : {_debutPeriode:dd/MM/yyyy} → {_finPeriode:dd/MM/yyyy}";
        }

        // ==================================================================
        // Chargement principal
        // ==================================================================
        private void ChargerTout()
        {
            ChargerKpi();
            AppliquerFiltreHistorique();
        }

        private void ChargerKpi()
        {
            try
            {
                var stats = _depenseService.ObtenirStats(_debutPeriode, _finPeriode);

                TxtCaEncaisse.Text  = stats.CaEncaisse.ToString("N0");
                TxtCharges.Text     = stats.ChargesExploitation.ToString("N0");
                TxtChargesDetail.Text = $"FCFA (dép. {stats.TotalDepenses:N0} + sal. {stats.SalaireSecretaire:N0})";
                TxtMargeBrute.Text  = stats.MargeBrute.ToString("N0");
                TxtBeneficeNet.Text = stats.BeneficeNet.ToString("N0");

                // Couleur Bénéfice Net
                TxtBeneficeNet.Foreground = stats.BeneficeNet >= 0
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));

                // Détail ligne sous les cartes
                TxtDetailCommissions.Text = stats.TotalCommissions.ToString("N0") + " FCFA";
                TxtDetailMateriaux.Text   = stats.TotalMateriaux.ToString("N0")   + " FCFA";
                TxtDetailDepenses.Text    = stats.TotalDepenses.ToString("N0")    + " FCFA";
                TxtDetailSalaire.Text     = stats.SalaireSecretaire.ToString("N0") + " FCFA";
            }
            catch (Exception ex)
            {
                TxtMessage.Text       = "Erreur KPI : " + ex.Message;
                TxtMessage.Foreground = Brushes.Red;
            }
        }

        private void AppliquerFiltreHistorique()
        {
            if (!_initialise) return;
            try
            {
                string cat    = (CmbFiltreCategorie.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Toutes";
                string statut = (CmbFiltreStatut.SelectedItem    as ComboBoxItem)?.Tag?.ToString() ?? "Tous";

                var liste = _depenseService.Filtrer(_debutPeriode, _finPeriode,
                    cat == "Toutes" ? null : cat,
                    statut == "Tous" ? null : statut);

                GridDepenses.ItemsSource = null;
                GridDepenses.ItemsSource = liste;

                decimal totalFiltre = liste.Where(d => !d.EstAnnulee && d.StatutValidation == "Validee")
                                           .Sum(d => d.Montant);
                TxtTotalFiltre.Text = totalFiltre.ToString("N0") + " FCFA";
                TxtNbFiltre.Text    = $"  — {liste.Count} dépense(s)";
            }
            catch (Exception ex)
            {
                TxtMessage.Text       = "Erreur : " + ex.Message;
                TxtMessage.Foreground = Brushes.Red;
            }
        }

        // ==================================================================
        // Events sélecteur période
        // ==================================================================
        private void CmbPeriode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialise) return;
            string tag = (CmbPeriode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mois";
            PanelDateCustom.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;
            CalculerPeriode(tag);
            ChargerTout();
        }

        private void DateFiltre_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialise) return;
            CalculerPeriode("custom");
            ChargerTout();
        }

        private void FiltreChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialise) return;
            AppliquerFiltreHistorique();
        }

        // ==================================================================
        // Catégorie → remplir les types
        // ==================================================================
        private void CmbCategorie_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialise || CmbCategorie.SelectedItem == null) return;
            string cat = ((ComboBoxItem)CmbCategorie.SelectedItem).Tag?.ToString() ?? "Divers";
            CmbTypeDepense.ItemsSource = null;
            if (Depense.CatalogueTypes.TryGetValue(cat, out var types))
                CmbTypeDepense.ItemsSource = types;
            CmbTypeDepense.SelectedIndex = -1;
        }

        // ==================================================================
        // Ajouter une dépense
        // ==================================================================
        private void BtnAjouter_Click(object sender, RoutedEventArgs e)
        {
            TxtMessage.Text = "";

            if (CmbCategorie.SelectedItem == null)
            { AficherErreur("Sélectionnez une catégorie."); return; }

            string type = CmbTypeDepense.Text.Trim();
            if (string.IsNullOrEmpty(type))
            { AficherErreur("Sélectionnez ou saisissez un type de dépense."); return; }

            if (!decimal.TryParse(TxtMontant.Text.Replace(" ", ""), out decimal montant) || montant <= 0)
            { AficherErreur("Saisissez un montant valide (supérieur à 0)."); return; }

            if (DateDepense.SelectedDate == null)
            { AficherErreur("Sélectionnez une date."); return; }

            string cat    = ((ComboBoxItem)CmbCategorie.SelectedItem).Tag?.ToString() ?? "Divers";
            string statut = (CmbStatutSaisie.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Validee";
            var op = _authService.UtilisateurConnecte;

            var depense = new Depense
            {
                Categorie       = cat,
                TypeDepense     = type,
                Montant         = montant,
                DateDepense     = DateDepense.SelectedDate.Value,
                Description     = TxtDescription.Text.Trim(),
                StatutValidation = statut,
                NomOperateur    = op != null ? $"{op.Prenom} {op.Nom}" : ""
            };

            try
            {
                _depenseService.Ajouter(depense);
                CmbCategorie.SelectedIndex    = -1;
                CmbTypeDepense.SelectedIndex  = -1;
                CmbTypeDepense.Text           = "";
                TxtMontant.Text               = "";
                TxtDescription.Text           = "";
                CmbStatutSaisie.SelectedIndex = 0;
                DateDepense.SelectedDate      = DateTime.Today;

                TxtMessage.Text       = "✅  Dépense enregistrée.";
                TxtMessage.Foreground = Brushes.Green;
                ChargerTout();
            }
            catch (Exception ex)
            {
                AficherErreur("Erreur : " + ex.Message);
            }
        }

        // ==================================================================
        // Valider une dépense "En attente"
        // ==================================================================
        private void BtnValider_Click(object sender, RoutedEventArgs e)
        {
            if (GridDepenses.SelectedItem is not Depense dep)
            { MessageBox.Show("Sélectionnez une dépense.", "Attention",
                  MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            if (dep.EstAnnulee)
            { MessageBox.Show("Impossible de valider une dépense annulée.", "Attention",
                  MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            if (dep.StatutValidation == "Validee")
            { MessageBox.Show("Cette dépense est déjà validée.", "Information",
                  MessageBoxButton.OK, MessageBoxImage.Information); return; }

            var op = _authService.UtilisateurConnecte;
            string nomBoss = op != null ? $"{op.Prenom} {op.Nom}" : "Boss";

            try
            {
                _depenseService.Valider(dep.IdDepense, nomBoss);
                TxtMessage.Text       = "✅  Dépense validée.";
                TxtMessage.Foreground = Brushes.Green;
                ChargerTout();
            }
            catch (Exception ex)
            {
                AficherErreur("Erreur : " + ex.Message);
            }
        }

        // ==================================================================
        // Annuler une dépense
        // ==================================================================
        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (GridDepenses.SelectedItem is not Depense depense)
            { MessageBox.Show("Sélectionnez une dépense à annuler.", "Attention",
                  MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            if (depense.EstAnnulee)
            { MessageBox.Show("Cette dépense est déjà annulée.", "Attention",
                  MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            string? motif = DemanderMotif(depense);
            if (motif == null) return;

            var r = MessageBox.Show(
                $"Confirmer l'annulation de {depense.Montant:N0} FCFA ({depense.TypeDepense}) ?\n\nMotif : {motif}",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                var op = _authService.UtilisateurConnecte;
                string nom = op != null ? $"{op.Prenom} {op.Nom}" : "";
                _depenseService.Annuler(depense.IdDepense, motif, nom);
                TxtMessage.Text       = "Dépense annulée.";
                TxtMessage.Foreground = Brushes.Green;
                ChargerTout();
            }
            catch (Exception ex)
            {
                AficherErreur("Erreur : " + ex.Message);
            }
        }

        // ==================================================================
        // Export CSV
        // ==================================================================
        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var items = GridDepenses.ItemsSource as IEnumerable<Depense>;
                if (items == null || !items.Any())
                { MessageBox.Show("Aucune donnée à exporter.", "Export",
                      MessageBoxButton.OK, MessageBoxImage.Information); return; }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title      = "Exporter les dépenses",
                    Filter     = "CSV (*.csv)|*.csv|Tous (*.*)|*.*",
                    FileName   = $"Depenses_{_debutPeriode:yyyyMMdd}_{_finPeriode:yyyyMMdd}.csv",
                    DefaultExt = ".csv"
                };
                if (dlg.ShowDialog() != true) return;

                var sb = new StringBuilder();
                // En-tête BOM UTF-8 pour Excel
                sb.Append('\uFEFF');
                sb.AppendLine("Date;Catégorie;Type;Montant (FCFA);Description;Par;Statut");

                foreach (var d in items)
                {
                    sb.AppendLine(
                        $"{d.DateAffichee};" +
                        $"{d.Categorie};" +
                        $"{d.TypeDepense};" +
                        $"{d.Montant:N0};" +
                        $"{d.Description.Replace(";", ",")};" +
                        $"{d.NomOperateur};" +
                        $"{d.StatutAffiche}");
                }

                // Ajouter le bilan en bas
                sb.AppendLine();
                sb.AppendLine($";;BILAN PÉRIODE {_debutPeriode:dd/MM/yyyy} → {_finPeriode:dd/MM/yyyy};;;");
                var stats = _depenseService.ObtenirStats(_debutPeriode, _finPeriode);
                sb.AppendLine($";;CA Encaissé;{stats.CaEncaisse:N0};;");
                sb.AppendLine($";;Commissions;{stats.TotalCommissions:N0};;");
                sb.AppendLine($";;Matériaux;{stats.TotalMateriaux:N0};;");
                sb.AppendLine($";;Dépenses;{stats.TotalDepenses:N0};;");
                sb.AppendLine($";;Salaire Secrétaire;{stats.SalaireSecretaire:N0};;");
                sb.AppendLine($";;MARGE BRUTE;{stats.MargeBrute:N0};;");
                sb.AppendLine($";;BÉNÉFICE NET;{stats.BeneficeNet:N0};;");

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);

                var res = MessageBox.Show(
                    $"Export réussi !\n{dlg.FileName}\n\nOuvrir le fichier ?",
                    "Export CSV", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = dlg.FileName,
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                AficherErreur("Erreur export : " + ex.Message);
            }
        }

        // ==================================================================
        // Helpers
        // ==================================================================
        private void AficherErreur(string msg)
        {
            TxtMessage.Text       = msg;
            TxtMessage.Foreground = Brushes.Red;
        }

        private string? DemanderMotif(Depense depense)
        {
            var dialog = new Window
            {
                Title = "Motif d'annulation",
                Width = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                SizeToContent = SizeToContent.Height
            };
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock
            {
                Text = $"Dépense : {depense.Montant:N0} FCFA — {depense.TypeDepense}",
                FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "Motif d'annulation * (10 caractères minimum)",
                Margin = new Thickness(0, 0, 0, 4)
            });
            var txMotif = new TextBox
            {
                Height = 60, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, Margin = new Thickness(0, 0, 0, 12)
            };
            sp.Children.Add(txMotif);
            var err = new TextBlock
            {
                Foreground = Brushes.Red, Height = 16,
                Margin = new Thickness(0, 0, 0, 10)
            };
            sp.Children.Add(err);
            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var btnOk = new Button
            {
                Content = "Confirmer", Width = 100, Height = 34,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var btnAnn = new Button { Content = "Annuler", Width = 90, Height = 34 };
            btnOk.Click  += (s, ev) =>
            {
                if (txMotif.Text.Trim().Length < 10) { err.Text = "Motif trop court."; return; }
                dialog.Tag = txMotif.Text.Trim();
                dialog.DialogResult = true; dialog.Close();
            };
            btnAnn.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            btns.Children.Add(btnOk);
            btns.Children.Add(btnAnn);
            sp.Children.Add(btns);
            dialog.Content = sp;
            return dialog.ShowDialog() == true ? (string)dialog.Tag : null;
        }
    }
}
